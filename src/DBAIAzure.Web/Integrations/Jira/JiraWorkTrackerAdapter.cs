// Jira Cloud implementation of IWorkTrackerAdapter (spec-018, increment 3; spec-020 per-run credentials).
// Net-new code behind the contract proven by the ADO adapter — the pipeline/cost layers are unchanged.
using System.Net.Http.Json;
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.WorkTracker;
using Microsoft.Extensions.Logging;

namespace DBAIAzure.Web.Integrations.Jira;

/// <summary>
/// Targets Jira Cloud REST v3: creates issues, sets custom fields (resolved logical→<c>customfield_*</c>
/// by name), appends comments, and resolves a binding key via the local map (shared with ADO). Work items
/// are referenced by their issue key (<c>PROJ-123</c>). All operations best-effort (FR-012). Credentials are
/// resolved per operation from the connector store via <see cref="IJiraConnectionFactory"/> (spec-020) so
/// UI-entered changes take effect without an application restart.
/// </summary>
public sealed class JiraWorkTrackerAdapter : IWorkTrackerAdapter
{
    private readonly IJiraConnectionFactory _connectionFactory;
    private readonly IBindingWorkItemMap _bindingMap;
    private readonly ILogger<JiraWorkTrackerAdapter> _logger;

    private readonly SemaphoreSlim _fieldCacheLock = new(1, 1);
    private Dictionary<string, string>? _fieldIdByName;   // Jira field display name → customfield id

    public JiraWorkTrackerAdapter(
        IJiraConnectionFactory connectionFactory, IBindingWorkItemMap bindingMap, ILogger<JiraWorkTrackerAdapter> logger)
    {
        _connectionFactory = connectionFactory;
        _bindingMap = bindingMap;
        _logger = logger;
    }

    public string TrackerKey => "Jira";

    /// <inheritdoc/>
    public async Task<CreatedWorkItemRef> CreateWorkItemAsync(
        WorkItemType type, string title, string description, WorkItemRef? parent, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);

        var fields = new Dictionary<string, object?>
        {
            ["project"] = new { key = connection.ProjectKey },
            ["summary"] = title,
            ["issuetype"] = new { name = ToJiraIssueType(type) },
            ["description"] = ToAdf(description),
        };
        if (parent is { } p)
            fields["parent"] = new { key = p.Value };

        using var response = await connection.Client.PostAsync("/rest/api/3/issue", JsonContent.Create(new { fields }), ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var key = doc.RootElement.GetProperty("key").GetString() ?? string.Empty;

        return new CreatedWorkItemRef
        {
            WorkItemId = new WorkItemRef(key),
            WorkItemType = ToJiraIssueType(type),
            Url = $"{connection.SiteUrl.TrimEnd('/')}/browse/{key}",
            WasUpdated = false,
        };
    }

    /// <inheritdoc/>
    public async Task<CreatedWorkItemRef> UpsertWorkItemAsync(
        WorkItemRef item, string title, string description, string appendComment, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);

        using var editResponse = await connection.Client.PutAsync($"/rest/api/3/issue/{item.Value}",
            JsonContent.Create(new { fields = new Dictionary<string, object?> { ["summary"] = title, ["description"] = ToAdf(description) } }), ct);
        editResponse.EnsureSuccessStatusCode();

        await AppendCommentAsync(item, appendComment, ct);

        return new CreatedWorkItemRef
        {
            WorkItemId = item,
            WorkItemType = string.Empty,
            Url = $"{connection.SiteUrl.TrimEnd('/')}/browse/{item.Value}",
            WasUpdated = true,
        };
    }

    /// <inheritdoc/>
    public async Task AppendCommentAsync(WorkItemRef item, string comment, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        using var response = await connection.Client.PostAsync(
            $"/rest/api/3/issue/{item.Value}/comment", JsonContent.Create(new { body = ToAdf(comment) }), ct);
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("Jira comment on {Issue} returned {Status}.", item.Value, response.StatusCode);
    }

    /// <inheritdoc/>
    public async Task SetFieldsAsync(
        WorkItemRef item, IReadOnlyDictionary<string, object?> logicalFields, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);

        var native = new Dictionary<string, object?>();
        foreach (var (logicalName, value) in logicalFields)
        {
            var fieldId = await ResolveFieldIdAsync(connection.Client, logicalName, ct);
            if (fieldId is null)
            {
                _logger.LogWarning("Jira field '{Field}' not found — skipped.", logicalName);
                continue;
            }
            native[fieldId] = value;
        }
        if (native.Count == 0)
            return;

        using var response = await connection.Client.PutAsync($"/rest/api/3/issue/{item.Value}", JsonContent.Create(new { fields = native }), ct);
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("Jira field update on {Issue} returned {Status}.", item.Value, response.StatusCode);
    }

    /// <inheritdoc/>
    public Task<WorkItemRef?> ResolveByBindingKeyAsync(string bindingKey, CancellationToken ct = default) =>
        // Shared local map (populated at creation) — same resolution path as ADO; a JQL fallback over the
        // binding custom field is a later enhancement for cross-instance recovery.
        _bindingMap.ResolveAsync(bindingKey, ct);

    /// <inheritdoc/>
    public async Task<ProvisioningResult> ProvisionFieldsAsync(AdoTelemetryFieldConfig fieldConfig, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        return await new JiraFieldProvisioner(connection.Client, _logger).ProvisionAsync(fieldConfig, ct);
    }

    /// <inheritdoc/>
    public RollupCapability GetRollupCapability() => new(
        RollupKind.RequiresAddOn, "Jira Advanced Roadmaps",
        "Hierarchical cost rollup on Jira requires Advanced Roadmaps (or a marketplace aggregation); the per-item cost fields are still populated.");

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Resolves a logical field name to its <c>customfield_*</c> id (cached after the first call).</summary>
    private async Task<string?> ResolveFieldIdAsync(HttpClient client, string logicalName, CancellationToken ct)
    {
        if (_fieldIdByName is null)
        {
            await _fieldCacheLock.WaitAsync(ct);
            try
            {
                if (_fieldIdByName is null)
                {
                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    using var response = await client.GetAsync("/rest/api/3/field", ct);
                    if (response.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                        foreach (var field in doc.RootElement.EnumerateArray())
                        {
                            var name = field.TryGetProperty("name", out var n) ? n.GetString() : null;
                            var id = field.TryGetProperty("id", out var i) ? i.GetString() : null;
                            if (name is not null && id is not null)
                                map[name] = id;
                        }
                    }
                    _fieldIdByName = map;
                }
            }
            finally { _fieldCacheLock.Release(); }
        }
        return _fieldIdByName.TryGetValue(logicalName, out var fieldId) ? fieldId : null;
    }

    // Jira issue type names for the logical types (Agile-equivalent hierarchy).
    private static string ToJiraIssueType(WorkItemType type) => type switch
    {
        WorkItemType.Epic => "Epic",
        WorkItemType.UserStory => "Story",
        WorkItemType.Task => "Task",
        WorkItemType.Bug => "Bug",
        _ => "Task",
    };

    // Minimal Atlassian Document Format wrapper — Jira Cloud v3 requires ADF for description/comment bodies.
    private static object ToAdf(string text) => new
    {
        type = "doc",
        version = 1,
        content = new object[]
        {
            new { type = "paragraph", content = new object[] { new { type = "text", text = string.IsNullOrEmpty(text) ? " " : text } } },
        },
    };
}
