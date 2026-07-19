// IBoardsClient implementation over the official Azure DevOps Work Item Tracking client (PAT auth).
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

namespace DBAIAzure.Web.Integrations.AzureDevOps;

/// <summary>
/// Creates and updates Azure DevOps Boards work items using the official
/// <see cref="WorkItemTrackingHttpClient"/> behind the project's <see cref="IBoardsClient"/> seam.
/// Authenticates with a PAT resolved from <see cref="IConnectorConfigRepository"/> at each method
/// entry (hot-reload — FR-014); falls back to <see cref="AzureDevOpsOptions"/> when the repo is
/// unavailable or has no stored credentials.
/// </summary>
public sealed class AzureDevOpsBoardsClient : IBoardsClient, IDisposable
{
    private const string TitleField = "/fields/System.Title";
    private const string DescriptionField = "/fields/System.Description";
    private const string HistoryField = "/fields/System.History";
    private const string AreaPathField = "/fields/System.AreaPath";
    private const string IterationPathField = "/fields/System.IterationPath";
    private const string ParentRelation = "System.LinkTypes.Hierarchy-Reverse";

    // Reference name (not a JSON-Patch path) — used to look up and merge tags in UpdateFieldsAsync.
    private const string TagsField = "System.Tags";

    private readonly AzureDevOpsOptions _options;
    private readonly IConnectorConfigRepository? _configRepo;

    // Cached connection — rebuilt only when the org URL or PAT changes (hot-reload).
    private WorkItemTrackingHttpClient? _cachedClient;
    private VssConnection? _connection;
    private string _cachedConnectionKey = string.Empty;
    private readonly SemaphoreSlim _clientLock = new(1, 1);

    public AzureDevOpsBoardsClient(IOptions<AzureDevOpsOptions> options, IConnectorConfigRepository? configRepo = null)
    {
        _options = options.Value;
        _configRepo = configRepo;
    }

    // ── IBoardsClient ────────────────────────────────────────────────────────────

    public async Task<CreatedWorkItemRef> CreateWorkItemAsync(
        string workItemType, string title, string description, int? parentId,
        CancellationToken cancellationToken = default)
    {
        var (client, resolvedOptions) = await GetClientAsync(cancellationToken);

        var patch = new JsonPatchDocument
        {
            AddField(TitleField, title),
            AddField(DescriptionField, description),
        };

        AppendOptionalPaths(patch, resolvedOptions);

        if (parentId is { } parent)
            patch.Add(BuildParentRelation(parent, resolvedOptions.OrganizationUrl));

        var created = await client.CreateWorkItemAsync(
            patch, resolvedOptions.Project, workItemType, cancellationToken: cancellationToken);

        return ToRef(created, workItemType, wasUpdated: false);
    }

    public async Task<CreatedWorkItemRef> UpsertWorkItemAsync(
        int workItemId, string title, string description, string appendComment,
        CancellationToken cancellationToken = default)
    {
        var (client, _) = await GetClientAsync(cancellationToken);

        var patch = new JsonPatchDocument
        {
            AddField(TitleField, title),
            AddField(DescriptionField, description),
            AddField(HistoryField, appendComment),
        };

        var updated = await client.UpdateWorkItemAsync(
            patch, workItemId, cancellationToken: cancellationToken);

        var type = updated.Fields.TryGetValue("System.WorkItemType", out var value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;

        return ToRef(updated, type, wasUpdated: true);
    }

    public async Task AppendDiscussionCommentAsync(
        int workItemId, string comment, CancellationToken cancellationToken = default)
    {
        var (client, _) = await GetClientAsync(cancellationToken);
        var patch = new JsonPatchDocument { AddField(HistoryField, comment) };
        await client.UpdateWorkItemAsync(patch, workItemId, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateFieldsAsync(
        int workItemId, IReadOnlyDictionary<string, object?> fields,
        CancellationToken cancellationToken = default)
    {
        if (fields.Count == 0)
            return;

        var (client, _) = await GetClientAsync(cancellationToken);
        var patch = new JsonPatchDocument();

        foreach (var (referenceName, value) in fields)
        {
            // System.Tags is a single delimited string — merge so we never clobber the user's tags.
            var fieldValue = string.Equals(referenceName, TagsField, StringComparison.OrdinalIgnoreCase)
                ? await MergeTagsAsync(client, workItemId, value?.ToString(), cancellationToken)
                : value;

            patch.Add(new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = $"/fields/{referenceName}",
                Value = fieldValue,
            });
        }

        await client.UpdateWorkItemAsync(patch, workItemId, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Reads the work item's current tags and appends <paramref name="newTag"/> if not already present,
    /// returning the merged semicolon-delimited set so existing tags are preserved (non-destructive).
    /// </summary>
    private static async Task<string> MergeTagsAsync(
        WorkItemTrackingHttpClient client, int workItemId, string? newTag, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newTag))
            return string.Empty;

        var existing = await client.GetWorkItemAsync(
            workItemId, new[] { TagsField }, cancellationToken: cancellationToken);
        var currentTags = existing.Fields.TryGetValue(TagsField, out var tagsValue)
            ? tagsValue?.ToString() ?? string.Empty
            : string.Empty;

        var tags = currentTags
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (!tags.Contains(newTag, StringComparer.OrdinalIgnoreCase))
            tags.Add(newTag);

        return string.Join("; ", tags);
    }

    /// <summary>
    /// Verifies the stored PAT can read the configured project from the ADO REST API (FR-009).
    /// Uses a plain <see cref="HttpClient"/> so the test is independent of the SDK client cache.
    /// </summary>
    public async Task<ConnectorTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var resolvedOptions = await ResolveAllConfigAsync(ct);
        var orgUrl = resolvedOptions.OrganizationUrl;
        var project = resolvedOptions.Project;
        var pat = resolvedOptions.Pat;

        ConnectorTestResult Fail(string message) =>
            new(ConnectorType.AzureDevOps, false, message, DateTimeOffset.UtcNow);

        if (string.IsNullOrEmpty(pat) || string.IsNullOrEmpty(orgUrl) || string.IsNullOrEmpty(project))
            return Fail("Connector is not configured — no credentials stored.");

        using var http = new HttpClient();
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        http.Timeout = TimeSpan.FromSeconds(30);

        var url = $"{orgUrl.TrimEnd('/')}/_apis/projects/{Uri.EscapeDataString(project)}?api-version=7.1";

        try
        {
            var response = await http.GetAsync(url, ct);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                var name = doc.RootElement.TryGetProperty("name", out var nameProp)
                    ? nameProp.GetString() ?? project
                    : project;
                return new ConnectorTestResult(
                    ConnectorType.AzureDevOps, true,
                    $"Authenticated — project '{name}' confirmed in {orgUrl}.",
                    DateTimeOffset.UtcNow);
            }

            return (int)response.StatusCode switch
            {
                401 or 203 => Fail("Authentication failed — PAT rejected. Check the token value and scope (needs 'Project: Read')."),
                403        => Fail("Insufficient permissions — PAT lacks read access to this project (403 Forbidden)."),
                404        => Fail($"Project '{project}' not found in {orgUrl}. Check the project name."),
                _          => Fail($"Unexpected response from Azure DevOps: {(int)response.StatusCode} {response.StatusCode}."),
            };
        }
        catch (TaskCanceledException)
        {
            return Fail("Test timed out after 30 seconds — check network connectivity to Azure DevOps.");
        }
        catch (HttpRequestException ex)
        {
            return Fail($"Service unreachable — could not connect to {orgUrl}. {ex.Message}");
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the cached <see cref="WorkItemTrackingHttpClient"/>, rebuilding it if the PAT or
    /// organization URL has changed since the last call (hot-reload, FR-014).
    /// </summary>
    private async Task<(WorkItemTrackingHttpClient Client, AzureDevOpsOptions ResolvedOptions)> GetClientAsync(
        CancellationToken ct)
    {
        var resolvedOptions = await ResolveAllConfigAsync(ct);
        var connectionKey = $"{resolvedOptions.OrganizationUrl}|{resolvedOptions.Pat}";

        await _clientLock.WaitAsync(ct);
        try
        {
            if (_cachedClient is null || connectionKey != _cachedConnectionKey)
            {
                _connection?.Dispose();
                var credentials = new VssBasicCredential(string.Empty, resolvedOptions.Pat);
                _connection = new VssConnection(new Uri(resolvedOptions.OrganizationUrl), credentials);
                _cachedClient = _connection.GetClient<WorkItemTrackingHttpClient>();
                _cachedConnectionKey = connectionKey;
            }

            return (_cachedClient, resolvedOptions);
        }
        finally
        {
            _clientLock.Release();
        }
    }

    /// <summary>
    /// Returns the effective ADO options, preferring DB-stored values over IConfiguration fallbacks
    /// (hot-reload — FR-014). Area/iteration paths are not in the modal and always come from config.
    /// </summary>
    private async Task<AzureDevOpsOptions> ResolveAllConfigAsync(CancellationToken ct)
    {
        if (_configRepo is null)
            return _options;

        var resolved = new AzureDevOpsOptions
        {
            OrganizationUrl = _options.OrganizationUrl,
            Project = _options.Project,
            Pat = _options.Pat,
            AreaPath = _options.AreaPath,
            IterationPath = _options.IterationPath,
        };

        try
        {
            // spec-020: ADO config now lives on the generic WorkTracker connector (provider=AzureDevOps).
            var configResult = await _configRepo.GetAsync(ConnectorType.WorkTracker, ct);
            if (configResult?.NonSecretConfig is { } nonSecretJson)
            {
                var nonSecret = JsonSerializer.Deserialize<AzureDevOpsConnectorConfig>(
                    nonSecretJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (nonSecret is not null)
                {
                    if (!string.IsNullOrEmpty(nonSecret.OrganizationUrl))
                        resolved.OrganizationUrl = nonSecret.OrganizationUrl;
                    if (!string.IsNullOrEmpty(nonSecret.ProjectName))
                        resolved.Project = nonSecret.ProjectName;
                }
            }

            var secretsJson = await _configRepo.GetDecryptedSecretsAsync(ConnectorType.WorkTracker, ct);
            if (secretsJson is not null)
            {
                var secrets = JsonSerializer.Deserialize<AzureDevOpsSecrets>(
                    secretsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (!string.IsNullOrEmpty(secrets?.PersonalAccessToken))
                    resolved.Pat = secrets.PersonalAccessToken;
            }
        }
        catch
        {
            // On any DB error, fall back to IConfiguration values already set above.
        }

        return resolved;
    }

    private static void AppendOptionalPaths(JsonPatchDocument patch, AzureDevOpsOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AreaPath))
            patch.Add(AddField(AreaPathField, options.AreaPath));
        if (!string.IsNullOrWhiteSpace(options.IterationPath))
            patch.Add(AddField(IterationPathField, options.IterationPath));
    }

    private static JsonPatchOperation AddField(string path, string value) => new()
    {
        Operation = Operation.Add,
        Path = path,
        Value = value,
    };

    private static JsonPatchOperation BuildParentRelation(int parentId, string orgUrl) => new()
    {
        Operation = Operation.Add,
        Path = "/relations/-",
        Value = new
        {
            rel = ParentRelation,
            url = $"{orgUrl}/_apis/wit/workItems/{parentId}",
        },
    };

    private static CreatedWorkItemRef ToRef(WorkItem workItem, string workItemType, bool wasUpdated) => new()
    {
        WorkItemId = DBAIAzure.Core.Models.WorkTracker.WorkItemRef.From(workItem.Id ?? 0),
        WorkItemType = workItemType,
        Url = workItem.Url ?? string.Empty,
        WasUpdated = wasUpdated,
    };

    public void Dispose()
    {
        _clientLock.Dispose();
        _connection?.Dispose();
    }

    // ── Private secret shape ─────────────────────────────────────────────────────

    private sealed record AzureDevOpsSecrets(string? PersonalAccessToken);
}
