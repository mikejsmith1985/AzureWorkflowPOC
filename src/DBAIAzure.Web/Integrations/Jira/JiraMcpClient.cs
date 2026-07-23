// MCP-first transport for Jira. Reaches Jira through a remote MCP server's tools instead of its REST API,
// mirroring how the Messaging connector already prefers MCP over a direct webhook. Only the operations the DoR
// workflow depends on are covered — read an issue, transition it, and find newly-created issues.
using System.Text.Json;
using DBAIAzure.Connectors.Messaging;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.WorkTracker;

namespace DBAIAzure.Web.Integrations.Jira;

/// <summary>One newly-created issue seen by the MCP trigger poll.</summary>
/// <param name="IssueKey">The Jira issue key (<c>PROJ-123</c>).</param>
/// <param name="ProjectKey">Project key the issue belongs to, when the search result exposed it.</param>
/// <param name="IssueType">Issue type name, when the search result exposed it.</param>
/// <param name="CreatedAt">Creation instant, when the search result exposed a parseable one.</param>
public sealed record JiraIssueSummary(string IssueKey, string ProjectKey, string IssueType, DateTimeOffset? CreatedAt);

/// <summary>
/// Calls Jira through a configured MCP server. Every method is best-effort and returns a "not available" result
/// (null / false / empty) when MCP is not configured or the call fails, so callers transparently fall back to the
/// Jira REST API rather than failing the run.
/// </summary>
public interface IJiraMcpClient
{
    /// <summary>True when a server URL is configured, i.e. the MCP path should be attempted at all.</summary>
    Task<bool> IsEnabledAsync(CancellationToken ct = default);

    /// <summary>Reads one issue over MCP; null when MCP is unavailable for reads or the call/parse failed.</summary>
    Task<WorkItemFields?> TryReadIssueAsync(
        string issueKey, IReadOnlyCollection<string> watchFields, CancellationToken ct = default);

    /// <summary>Transitions an issue over MCP; false when MCP is unavailable for transitions or the call failed.</summary>
    Task<bool> TryTransitionAsync(string issueKey, string transitionId, CancellationToken ct = default);

    /// <summary>Runs a JQL search over MCP and returns the matching issues; empty when unavailable or on failure.</summary>
    Task<IReadOnlyList<JiraIssueSummary>> SearchAsync(string jql, int maxResults, CancellationToken ct = default);
}

/// <inheritdoc cref="IJiraMcpClient"/>
public sealed class JiraMcpClient : IJiraMcpClient
{
    // Generic argument shapes. Every MCP server names its arguments differently, so these are only sensible
    // starting points — the operator overrides them per tool on the connector when their server differs.
    private const string DefaultReadArguments = """{"issueIdOrKey":"{{issueKey}}","fields":"{{fields}}"}""";
    private const string DefaultTransitionArguments = """{"issueIdOrKey":"{{issueKey}}","transition":{"id":"{{transitionId}}"}}""";
    private const string DefaultSearchArguments = """{"jql":"{{jql}}","maxResults":{{maxResults}}}""";

    private const string IssueKeyPlaceholder = "{{issueKey}}";
    private const string FieldsPlaceholder = "{{fields}}";
    private const string TransitionIdPlaceholder = "{{transitionId}}";
    private const string JqlPlaceholder = "{{jql}}";
    private const string MaxResultsPlaceholder = "{{maxResults}}";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IWorkTrackerConfigResolver _configResolver;
    private readonly IMcpMessageGateway _gateway;   // a generic MCP tool invoker; ReadAsync calls any tool by name
    private readonly ILogger<JiraMcpClient> _logger;

    public JiraMcpClient(
        IWorkTrackerConfigResolver configResolver, IMcpMessageGateway gateway, ILogger<JiraMcpClient> logger)
    {
        _configResolver = configResolver;
        _gateway = gateway;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> IsEnabledAsync(CancellationToken ct = default)
    {
        var (config, _) = await ResolveAsync(ct);
        return !string.IsNullOrWhiteSpace(config?.McpServerUrl);
    }

    /// <inheritdoc/>
    public async Task<WorkItemFields?> TryReadIssueAsync(
        string issueKey, IReadOnlyCollection<string> watchFields, CancellationToken ct = default)
    {
        var (config, authToken) = await ResolveAsync(ct);
        if (config is null || !HasTool(config.McpServerUrl, config.McpReadToolName))
            return null;

        var arguments = BuildReadArguments(config.McpReadArgumentTemplate, issueKey, watchFields);
        var content = await CallToolAsync(config.McpServerUrl!, config.McpReadToolName!, arguments, authToken, ct);
        if (content is null)
            return null;

        var siteUrl = config.SiteUrl?.TrimEnd('/') ?? string.Empty;
        return ParseIssue(content, issueKey, siteUrl, watchFields);
    }

    /// <inheritdoc/>
    public async Task<bool> TryTransitionAsync(string issueKey, string transitionId, CancellationToken ct = default)
    {
        var (config, authToken) = await ResolveAsync(ct);
        if (config is null || !HasTool(config.McpServerUrl, config.McpTransitionToolName))
            return false;

        var arguments = BuildTransitionArguments(config.McpTransitionArgumentTemplate, issueKey, transitionId);
        var content = await CallToolAsync(
            config.McpServerUrl!, config.McpTransitionToolName!, arguments, authToken, ct);

        // A successful tool call is the signal; the tool's payload varies by server and carries nothing we need.
        return content is not null;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JiraIssueSummary>> SearchAsync(
        string jql, int maxResults, CancellationToken ct = default)
    {
        var (config, authToken) = await ResolveAsync(ct);
        if (config is null || !HasTool(config.McpServerUrl, config.McpSearchToolName))
            return Array.Empty<JiraIssueSummary>();

        var arguments = BuildSearchArguments(config.McpSearchArgumentTemplate, jql, maxResults);
        var content = await CallToolAsync(config.McpServerUrl!, config.McpSearchToolName!, arguments, authToken, ct);
        return content is null ? Array.Empty<JiraIssueSummary>() : ParseSearchResults(content);
    }

    // ── Pure helpers (unit-testable without an MCP server) ────────────────────

    /// <summary>Substitutes the issue key and the comma-joined watch fields into the read tool's arguments.</summary>
    public static string BuildReadArguments(
        string? templateJson, string issueKey, IReadOnlyCollection<string> watchFields)
    {
        var template = Blank(templateJson) ? DefaultReadArguments : templateJson!;
        return template
            .Replace(IssueKeyPlaceholder, JsonEscapeInner(issueKey))
            .Replace(FieldsPlaceholder, JsonEscapeInner(string.Join(",", watchFields)));
    }

    /// <summary>Substitutes the issue key and target transition id into the transition tool's arguments.</summary>
    public static string BuildTransitionArguments(string? templateJson, string issueKey, string transitionId)
    {
        var template = Blank(templateJson) ? DefaultTransitionArguments : templateJson!;
        return template
            .Replace(IssueKeyPlaceholder, JsonEscapeInner(issueKey))
            .Replace(TransitionIdPlaceholder, JsonEscapeInner(transitionId));
    }

    /// <summary>Substitutes the JQL and result cap into the search tool's arguments.</summary>
    public static string BuildSearchArguments(string? templateJson, string jql, int maxResults)
    {
        var template = Blank(templateJson) ? DefaultSearchArguments : templateJson!;
        return template
            .Replace(JqlPlaceholder, JsonEscapeInner(jql))
            .Replace(MaxResultsPlaceholder, maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Parses an MCP read-tool response into the flat field bag the DoR review consumes. Accepts either a bare
    /// Jira issue object or one wrapped in an <c>issue</c> property. Returns null when nothing issue-shaped is
    /// found, which sends the caller to the REST fallback rather than reviewing an empty ticket.
    /// </summary>
    public static WorkItemFields? ParseIssue(
        string contentJson, string requestedKey, string siteUrl, IReadOnlyCollection<string> watchFields)
    {
        if (!TryParse(contentJson, out var document))
            return null;

        using (document)
        {
            var issue = document!.RootElement;
            if (issue.ValueKind == JsonValueKind.Object
                && issue.TryGetProperty("issue", out var wrapped) && wrapped.ValueKind == JsonValueKind.Object)
            {
                issue = wrapped;
            }

            if (issue.ValueKind != JsonValueKind.Object || !issue.TryGetProperty("fields", out var fields))
                return null;

            var key = ReadString(issue, "key");
            if (key.Length == 0)
                key = requestedKey;

            var url = siteUrl.Length > 0 ? $"{siteUrl}/browse/{key}" : string.Empty;
            return new WorkItemFields(key, url, JiraFieldFlattener.FlattenFields(fields, watchFields));
        }
    }

    /// <summary>
    /// Parses an MCP search-tool response into issue summaries. Accepts a bare array of issues or an object with
    /// an <c>issues</c> array. Entries without a key are skipped; any parse failure yields an empty list.
    /// </summary>
    public static IReadOnlyList<JiraIssueSummary> ParseSearchResults(string contentJson)
    {
        if (!TryParse(contentJson, out var document))
            return Array.Empty<JiraIssueSummary>();

        using (document)
        {
            if (!TryGetIssueArray(document!.RootElement, out var issues))
                return Array.Empty<JiraIssueSummary>();

            var summaries = new List<JiraIssueSummary>();
            foreach (var issue in issues.EnumerateArray())
            {
                if (issue.ValueKind != JsonValueKind.Object)
                    continue;

                var key = ReadString(issue, "key");
                if (key.Length == 0)
                    continue;

                summaries.Add(new JiraIssueSummary(key, ReadProjectKey(issue, key), ReadIssueType(issue), ReadCreatedAt(issue)));
            }

            return summaries;
        }
    }

    /// <summary>
    /// Builds the JQL the trigger poll runs: newly-created issues in the configured projects and issue types.
    /// <paramref name="createdAfter"/> is rendered in Jira's minute-precision format; when null the window falls
    /// back to <paramref name="lookbackMinutes"/> so a cold start does not replay the whole backlog.
    /// </summary>
    public static string BuildCreatedSinceJql(
        IReadOnlyCollection<string> projectKeys, IReadOnlyCollection<string> issueTypes,
        DateTimeOffset? createdAfter, int lookbackMinutes)
    {
        var clauses = new List<string>();

        if (projectKeys.Count > 0)
            clauses.Add($"project in ({string.Join(", ", projectKeys.Select(QuoteJql))})");
        if (issueTypes.Count > 0)
            clauses.Add($"issuetype in ({string.Join(", ", issueTypes.Select(QuoteJql))})");

        clauses.Add(createdAfter is { } since
            ? $"created > \"{since.UtcDateTime:yyyy/MM/dd HH:mm}\""
            : $"created >= -{Math.Max(1, lookbackMinutes)}m");

        return string.Join(" AND ", clauses) + " ORDER BY created ASC";
    }

    // ── Private plumbing ──────────────────────────────────────────────────────

    /// <summary>Invokes one MCP tool and returns its text content, or null when the call was not successful.</summary>
    private async Task<string?> CallToolAsync(
        string serverUrl, string toolName, string argumentsJson, string? authToken, CancellationToken ct)
    {
        var result = await _gateway.ReadAsync(new McpReadRequest(serverUrl, toolName, argumentsJson, authToken), ct);
        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.ContentJson))
            return result.ContentJson;

        _logger.LogDebug("Jira MCP tool '{Tool}' returned no usable content: {Message}", toolName, result.Message);
        return null;
    }

    /// <summary>Resolves the live Jira connector config and its MCP auth token; (null, null) when Jira is not active.</summary>
    private async Task<(JiraConnectorConfig? Config, string? AuthToken)> ResolveAsync(CancellationToken ct)
    {
        try
        {
            var resolved = await _configResolver.ResolveActiveAsync(ct);
            if (!resolved.IsConfigured || resolved.Provider != WorkTrackerProvider.Jira
                || string.IsNullOrWhiteSpace(resolved.NonSecretJson))
            {
                return (null, null);
            }

            var config = JsonSerializer.Deserialize<JiraConnectorConfig>(resolved.NonSecretJson!, JsonOptions);
            return (config, ReadSecret(resolved.DecryptedSecret, "mcpAuthToken"));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve the Jira connector for the MCP transport.");
            return (null, null);
        }
    }

    private static bool HasTool(string? serverUrl, string? toolName) =>
        !Blank(serverUrl) && !Blank(toolName);

    private static string? ReadSecret(string? secretJson, string property)
    {
        if (Blank(secretJson))
            return null;
        try
        {
            using var document = JsonDocument.Parse(secretJson!);
            return document.RootElement.TryGetProperty(property, out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryParse(string json, out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    private static bool TryGetIssueArray(JsonElement root, out JsonElement issues)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            issues = root;
            return true;
        }
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("issues", out issues) && issues.ValueKind == JsonValueKind.Array)
        {
            return true;
        }
        issues = default;
        return false;
    }

    /// <summary>Reads the project key from the issue's fields, falling back to the prefix of its key.</summary>
    private static string ReadProjectKey(JsonElement issue, string issueKey)
    {
        if (issue.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object
            && fields.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Object)
        {
            var key = ReadString(project, "key");
            if (key.Length > 0)
                return key;
        }

        var dashIndex = issueKey.IndexOf('-');
        return dashIndex > 0 ? issueKey[..dashIndex] : string.Empty;
    }

    private static string ReadIssueType(JsonElement issue)
    {
        if (issue.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object
            && fields.TryGetProperty("issuetype", out var issueType) && issueType.ValueKind == JsonValueKind.Object)
        {
            return ReadString(issueType, "name");
        }
        return string.Empty;
    }

    private static DateTimeOffset? ReadCreatedAt(JsonElement issue)
    {
        if (!issue.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
            return null;

        var created = ReadString(fields, "created");
        return DateTimeOffset.TryParse(created, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>Wraps a JQL literal in quotes, escaping any embedded quote so a project key cannot break the query.</summary>
    private static string QuoteJql(string literal) => $"\"{literal.Replace("\"", "\\\"")}\"";

    // Serialize as a JSON string then strip the surrounding quotes, leaving escaped contents suitable for
    // placement inside an existing pair of quotes in an argument template.
    private static string JsonEscapeInner(string value)
    {
        var quoted = JsonSerializer.Serialize(value);
        return quoted.Length >= 2 ? quoted[1..^1] : quoted;
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}
