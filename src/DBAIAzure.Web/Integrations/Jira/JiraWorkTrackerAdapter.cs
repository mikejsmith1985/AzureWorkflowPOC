// Jira Cloud implementation of IWorkTrackerAdapter (spec-018, increment 3). Net-new code behind the
// contract proven by the ADO adapter — the pipeline/cost layers are unchanged.
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
/// are referenced by their issue key (<c>PROJ-123</c>). All operations best-effort (FR-012).
/// </summary>
public sealed class JiraWorkTrackerAdapter : IWorkTrackerAdapter
{
    private readonly HttpClient _http;            // pre-authed; base = the Jira site
    private readonly JiraOptions _options;
    private readonly IBindingWorkItemMap _bindingMap;
    private readonly ILogger<JiraWorkTrackerAdapter> _logger;

    private readonly SemaphoreSlim _fieldCacheLock = new(1, 1);
    private Dictionary<string, string>? _fieldIdByName;   // Jira field display name → customfield id

    public JiraWorkTrackerAdapter(
        HttpClient http, JiraOptions options, IBindingWorkItemMap bindingMap, ILogger<JiraWorkTrackerAdapter> logger)
    {
        _http = http;
        _options = options;
        _bindingMap = bindingMap;
        _logger = logger;
    }

    public string TrackerKey => "Jira";

    /// <inheritdoc/>
    public async Task<CreatedWorkItemRef> CreateWorkItemAsync(
        WorkItemType type, string title, string description, WorkItemRef? parent, CancellationToken ct = default)
    {
        var fields = new Dictionary<string, object?>
        {
            ["project"] = new { key = _options.ProjectKey },
            ["summary"] = title,
            ["issuetype"] = new { name = ToJiraIssueType(type) },
            ["description"] = ToAdf(description),
        };
        if (parent is { } p)
            fields["parent"] = new { key = p.Value };

        using var response = await _http.PostAsync("/rest/api/3/issue", JsonContent.Create(new { fields }), ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var key = doc.RootElement.GetProperty("key").GetString() ?? string.Empty;

        return new CreatedWorkItemRef
        {
            WorkItemId = new WorkItemRef(key),
            WorkItemType = ToJiraIssueType(type),
            Url = $"{_options.SiteUrl.TrimEnd('/')}/browse/{key}",
            WasUpdated = false,
        };
    }

    /// <inheritdoc/>
    public async Task<CreatedWorkItemRef> UpsertWorkItemAsync(
        WorkItemRef item, string title, string description, string appendComment, CancellationToken ct = default)
    {
        using var editResponse = await _http.PutAsync($"/rest/api/3/issue/{item.Value}",
            JsonContent.Create(new { fields = new Dictionary<string, object?> { ["summary"] = title, ["description"] = ToAdf(description) } }), ct);
        editResponse.EnsureSuccessStatusCode();

        await AppendCommentAsync(item, appendComment, ct);

        return new CreatedWorkItemRef
        {
            WorkItemId = item,
            WorkItemType = string.Empty,
            Url = $"{_options.SiteUrl.TrimEnd('/')}/browse/{item.Value}",
            WasUpdated = true,
        };
    }

    /// <inheritdoc/>
    public async Task AppendCommentAsync(WorkItemRef item, string comment, CancellationToken ct = default)
    {
        using var response = await _http.PostAsync(
            $"/rest/api/3/issue/{item.Value}/comment", JsonContent.Create(new { body = ToAdf(comment) }), ct);
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("Jira comment on {Issue} returned {Status}.", item.Value, response.StatusCode);
    }

    /// <inheritdoc/>
    public async Task SetFieldsAsync(
        WorkItemRef item, IReadOnlyDictionary<string, object?> logicalFields, CancellationToken ct = default)
    {
        var native = new Dictionary<string, object?>();
        foreach (var (logicalName, value) in logicalFields)
        {
            var fieldId = await ResolveFieldIdAsync(logicalName, ct);
            if (fieldId is null)
            {
                _logger.LogWarning("Jira field '{Field}' not found — skipped.", logicalName);
                continue;
            }
            native[fieldId] = value;
        }
        if (native.Count == 0)
            return;

        using var response = await _http.PutAsync($"/rest/api/3/issue/{item.Value}", JsonContent.Create(new { fields = native }), ct);
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("Jira field update on {Issue} returned {Status}.", item.Value, response.StatusCode);
    }

    /// <inheritdoc/>
    public Task<WorkItemRef?> ResolveByBindingKeyAsync(string bindingKey, CancellationToken ct = default) =>
        // Shared local map (populated at creation) — same resolution path as ADO; a JQL fallback over the
        // binding custom field is a later enhancement for cross-instance recovery.
        _bindingMap.ResolveAsync(bindingKey, ct);

    /// <inheritdoc/>
    public Task<ProvisioningResult> ProvisionFieldsAsync(AdoTelemetryFieldConfig fieldConfig, CancellationToken ct = default)
        => Task.FromResult(new ProvisioningResult
        {
            IsSuccess = false,
            Mode = "JiraNotProvisioned",
            FieldsFailed = [new FieldProvisioningFailure("(all)",
                "Jira field provisioning (field + context + screen) lands in a follow-up; create the fields manually for now.")],
        });

    /// <inheritdoc/>
    public RollupCapability GetRollupCapability() => new(
        RollupKind.RequiresAddOn, "Jira Advanced Roadmaps",
        "Hierarchical cost rollup on Jira requires Advanced Roadmaps (or a marketplace aggregation); the per-item cost fields are still populated.");

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Resolves a logical field name to its <c>customfield_*</c> id (cached after the first call).</summary>
    private async Task<string?> ResolveFieldIdAsync(string logicalName, CancellationToken ct)
    {
        if (_fieldIdByName is null)
        {
            await _fieldCacheLock.WaitAsync(ct);
            try
            {
                if (_fieldIdByName is null)
                {
                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    using var response = await _http.GetAsync("/rest/api/3/field", ct);
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
