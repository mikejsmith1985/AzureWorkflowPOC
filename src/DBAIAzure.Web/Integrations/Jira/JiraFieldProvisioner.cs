// Provisions the telemetry/cost custom fields on Jira Cloud (spec-018 US2). Idempotent: find-or-create
// each field by name, then ensure it has a (global) context so its value is writable on any issue.
using System.Net.Http.Json;
using System.Text.Json;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.WorkTracker;
using Microsoft.Extensions.Logging;

namespace DBAIAzure.Web.Integrations.Jira;

/// <summary>
/// Makes the logical telemetry/cost fields usable on Jira. Unlike ADO (process + work-item-type field
/// attachment), Jira fields are global; the steps are: (1) find-or-create the custom field by name,
/// (2) ensure it has a context (a global context makes it writable on every project + issue type via the
/// REST API). Screen association (UI visibility) is intentionally out of scope — API writes don't need it.
/// </summary>
public sealed class JiraFieldProvisioner
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public JiraFieldProvisioner(HttpClient http, ILogger logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<ProvisioningResult> ProvisionAsync(AdoTelemetryFieldConfig config, CancellationToken ct = default)
    {
        // The config repeats the same fields across work item types; Jira fields are global, so dedupe by name.
        var logicalFields = config.WorkItemTypes.Values
            .SelectMany(workItemType => workItemType.Fields)
            .Select(field => (Name: ToLogicalName(field.ReferenceName), field.FieldType))
            .GroupBy(field => field.Name)
            .Select(group => group.First())
            .ToList();

        var existingByName = await GetFieldIdsByNameAsync(ct);
        var ready = new List<string>();
        var failed = new List<FieldProvisioningFailure>();

        foreach (var (name, fieldType) in logicalFields)
        {
            try
            {
                var fieldId = existingByName.TryGetValue(name, out var id) ? id : await CreateFieldAsync(name, fieldType, ct);
                await EnsureGlobalContextAsync(fieldId, name, ct);
                ready.Add(name);
            }
            catch (Exception ex)
            {
                failed.Add(new FieldProvisioningFailure(name, ex.Message));
            }
        }

        return new ProvisioningResult
        {
            IsSuccess = failed.Count == 0,
            Mode = "JiraFieldContext",
            FieldsReady = ready,
            FieldsFailed = failed,
        };
    }

    private async Task<Dictionary<string, string>> GetFieldIdsByNameAsync(CancellationToken ct)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var response = await _http.GetAsync("/rest/api/3/field", ct);
        if (!response.IsSuccessStatusCode)
            return byName;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        foreach (var field in doc.RootElement.EnumerateArray())
        {
            var name = field.TryGetProperty("name", out var n) ? n.GetString() : null;
            var id = field.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (name is not null && id is not null)
                byName[name] = id;
        }
        return byName;
    }

    private async Task<string> CreateFieldAsync(string name, AdoFieldType fieldType, CancellationToken ct)
    {
        var (type, searcherKey) = ToJiraFieldType(fieldType);
        using var response = await _http.PostAsync("/rest/api/3/field",
            JsonContent.Create(new { name, type, searcherKey }), ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException($"Jira did not return an id for field '{name}'.");
    }

    private async Task EnsureGlobalContextAsync(string fieldId, string name, CancellationToken ct)
    {
        // A field with at least one context is writable; a freshly created field may have none.
        using var contextsResponse = await _http.GetAsync($"/rest/api/3/field/{fieldId}/context", ct);
        if (contextsResponse.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(await contextsResponse.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("values", out var values) && values.GetArrayLength() > 0)
                return;   // already has a context — idempotent no-op
        }

        // No context yet — create a global one (no projectIds/issueTypeIds → applies everywhere).
        using var createResponse = await _http.PostAsync($"/rest/api/3/field/{fieldId}/context",
            JsonContent.Create(new { name = $"{name} global context" }), ct);
        if (!createResponse.IsSuccessStatusCode)
            _logger.LogWarning("Jira context create for field {Field} returned {Status}.", name, createResponse.StatusCode);
    }

    /// <summary>Logical field name = the config reference name without the ADO <c>Custom.</c> prefix.</summary>
    private static string ToLogicalName(string referenceName) =>
        referenceName.StartsWith("Custom.", StringComparison.Ordinal) ? referenceName["Custom.".Length..] : referenceName;

    // Logical field type → Jira custom field type + searcher.
    private static (string Type, string SearcherKey) ToJiraFieldType(AdoFieldType fieldType) => fieldType switch
    {
        AdoFieldType.Integer or AdoFieldType.Double => (
            "com.atlassian.jira.plugin.system.customfieldtypes:float",
            "com.atlassian.jira.plugin.system.customfieldtypes:exactnumber"),
        AdoFieldType.PicklistString => (
            "com.atlassian.jira.plugin.system.customfieldtypes:select",
            "com.atlassian.jira.plugin.system.customfieldtypes:multiselectsearcher"),
        _ => (
            "com.atlassian.jira.plugin.system.customfieldtypes:textfield",
            "com.atlassian.jira.plugin.system.customfieldtypes:textsearcher"),
    };
}
