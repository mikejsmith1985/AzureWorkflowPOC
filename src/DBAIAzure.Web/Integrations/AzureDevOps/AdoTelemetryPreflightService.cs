// ADO Telemetry Field Bootstrap preflight service — creates custom fields or builds fallback mappings (spec-009).
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using Microsoft.Extensions.Options;

namespace DBAIAzure.Web.Integrations.AzureDevOps;

/// <summary>
/// Runs the ADO telemetry preflight step: detects whether the target organization
/// supports custom field creation (Bootstrap Mode) or requires a native-field fallback
/// mapping (Adaptive Mode). Writes the outcome to a manifest JSON file on disk.
/// PAT and org URL are resolved from <see cref="IConnectorConfigRepository"/> with fallback
/// to <see cref="AzureDevOpsOptions"/> — never hard-coded (Article IX).
/// </summary>
public sealed class AdoTelemetryPreflightService : IAdoTelemetryPreflightService
{
    private const int MaxRetryAttempts = 3;

    // Overridable for unit tests to avoid real delays. Production always uses exponential backoff.
    public static Func<int, TimeSpan> RetryDelayFactory =
        attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
    private const string ApiVersion = "7.1";

    // ADO Agile process templateTypeId (Microsoft well-known system GUID — does not change across orgs).
    // NOTE: these were previously swapped, which mis-detected Agile projects (and Agile-inherited ones,
    // whose parentProcessTypeId is this Agile GUID) as Scrum, so fields attached to ProductBacklogItem
    // (a Scrum-only WIT) and silently failed with NotFound on every Agile project.
    private const string AgileTemplateTypeId = "adcc42ab-9882-485e-a3ed-7678f01f66bc";
    // ADO Scrum process templateTypeId.
    private const string ScrumTemplateTypeId = "6b724908-ef14-45cf-84f8-768b5384da45";

    // Work item type reference names by process type.
    private static readonly Dictionary<AdoProcessType, Dictionary<string, string>> WitRefNames = new()
    {
        [AdoProcessType.Agile] = new()
        {
            ["UserStory"] = "Microsoft.VSTS.WorkItemTypes.UserStory",
            ["Task"] = "Microsoft.VSTS.WorkItemTypes.Task",
            ["Epic"] = "Microsoft.VSTS.WorkItemTypes.Epic",
            ["Bug"] = "Microsoft.VSTS.WorkItemTypes.Bug",
        },
        [AdoProcessType.Scrum] = new()
        {
            ["UserStory"] = "Microsoft.VSTS.WorkItemTypes.ProductBacklogItem",
            ["Task"] = "Microsoft.VSTS.WorkItemTypes.Task",
            ["Epic"] = "Microsoft.VSTS.WorkItemTypes.Epic",
            ["Bug"] = "Microsoft.VSTS.WorkItemTypes.Bug",
        },
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AzureDevOpsOptions _defaultOptions;
    private readonly IConnectorConfigRepository? _configRepo;
    private readonly ManifestPathResolver _manifestPathResolver;
    private readonly ILogger<AdoTelemetryPreflightService> _logger;

    public AdoTelemetryPreflightService(
        IHttpClientFactory httpClientFactory,
        IOptions<AzureDevOpsOptions> options,
        IConnectorConfigRepository? configRepo,
        ManifestPathResolver manifestPathResolver,
        ILogger<AdoTelemetryPreflightService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _defaultOptions = options.Value;
        _configRepo = configRepo;
        _manifestPathResolver = manifestPathResolver;
        _logger = logger;
    }

    // ── IAdoTelemetryPreflightService ────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<PreflightResult> RunPreflightAsync(
        AdoTelemetryFieldConfig? overrideConfig,
        CancellationToken cancellationToken = default)
    {
        var (orgUrl, project, pat) = await ResolveCredentialsAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(orgUrl))
            return PreflightResult.Fail("ADO org URL is not configured. Set it in the Connector Settings panel.");

        if (string.IsNullOrWhiteSpace(pat))
            return PreflightResult.Fail("ADO Personal Access Token is not configured.");

        if (string.IsNullOrWhiteSpace(project))
            return PreflightResult.Fail("ADO project name is not configured.");

        using var http = BuildHttpClient(pat);

        // Detect process type first — fail fast on unsupported processes.
        var (processType, templateTypeId, detectError) = await DetectProcessTypeAsync(http, orgUrl, project, cancellationToken);
        if (detectError is not null)
            return PreflightResult.Fail(detectError);

        var fieldConfig = await ResolveFieldConfigAsync(overrideConfig, cancellationToken);

        // Admin probe doubles as the source for the inherited processId.
        var (mode, processId, probeError) = await ProbeAdminAccessAsync(http, orgUrl, templateTypeId!, cancellationToken);
        if (probeError is not null)
            return PreflightResult.Fail(probeError);

        PreflightManifestBase manifest;
        if (mode == PreflightMode.Bootstrap)
            manifest = await RunBootstrapAsync(http, orgUrl, project, processType, processId!, fieldConfig, cancellationToken);
        else
            manifest = await RunAdaptiveAsync(http, orgUrl, project, processType, fieldConfig, cancellationToken);

        await WriteManifestAsync(manifest, cancellationToken);
        _logger.LogInformation("ADO preflight complete: {Mode}, org={OrgUrl}", mode, orgUrl);
        return PreflightResult.Succeed(manifest);
    }

    // ── Public static helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Loads the embedded default telemetry field config baked into <c>DBAIAzure.Core</c>.
    /// Public so that tests can verify the embedded JSON independently of the service.
    /// </summary>
    public static async Task<AdoTelemetryFieldConfig> LoadDefaultConfigAsync(CancellationToken ct = default)
    {
        // The resource lives in DBAIAzure.Core — use a type from that assembly to locate it.
        var asm = typeof(AdoTelemetryFieldConfig).Assembly;
        var resourceName = "DBAIAzure.Core.Resources.default-telemetry-config.json";
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found in {asm.GetName().Name}.");
        var json = await JsonSerializer.DeserializeAsync<FieldConfigJson>(stream, JsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to deserialize default telemetry config.");
        return MapToDomain(json);
    }

    // ── Private orchestration ────────────────────────────────────────────────────

    private async Task<AdoTelemetryFieldConfig> ResolveFieldConfigAsync(
        AdoTelemetryFieldConfig? overrideConfig,
        CancellationToken ct)
    {
        if (overrideConfig is not null) return overrideConfig;

        var configPath = Environment.GetEnvironmentVariable("AdoTelemetry__ConfigPath")
                         ?? _defaultOptions.GetType()
                            .GetProperty("AdoTelemetryConfigPath")?.GetValue(_defaultOptions) as string;

        if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
        {
            try
            {
                using var fs = File.OpenRead(configPath);
                var json = await JsonSerializer.DeserializeAsync<FieldConfigJson>(fs, JsonOptions, ct);
                if (json is not null) return MapToDomain(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load custom config from {Path} — falling back to embedded default.", configPath);
            }
        }

        return await LoadDefaultConfigAsync(ct);
    }

    private async Task<BootstrapManifest> RunBootstrapAsync(
        HttpClient http,
        string orgUrl,
        string project,
        AdoProcessType processType,
        string processId,
        AdoTelemetryFieldConfig config,
        CancellationToken ct)
    {
        var fieldsCreated = new List<string>();
        var fieldsExisting = new List<string>();
        var fieldsFailed = new List<FieldBootstrapFailure>();

        // The work item types currently on the process — used to resolve the attachable reference name.
        var processWits = await GetProcessWorkItemTypesAsync(http, orgUrl, processId, ct);

        foreach (var (witKey, witConfig) in config.WorkItemTypes)
        {
            // In an inherited process a system WIT must be materialized as an inherited override before
            // fields can be added to it; this returns that customizable reference name (e.g. "MyProc.Task").
            var systemRefName = ResolveWitRefName(processType, witKey);
            var witRefName = await ResolveCustomizableWitRefNameAsync(
                http, orgUrl, processId, systemRefName, processWits, ct) ?? systemRefName;

            foreach (var field in witConfig.Fields)
            {
                // Create the org-level field only if missing — but a field can exist org-wide yet still
                // need attaching to THIS work item type (e.g. a prior run created it but failed to attach,
                // or it was added manually). So the attach below runs for existing fields too.
                var exists = await CheckFieldExistsAsync(http, orgUrl, field.ReferenceName, ct);
                if (exists)
                {
                    fieldsExisting.Add(field.ReferenceName);
                }
                else
                {
                    // For picklist fields, create the picklist first.
                    string? picklistId = null;
                    if (field.FieldType == AdoFieldType.PicklistString && field.PicklistValues is { Count: > 0 } picklist)
                        picklistId = await CreatePicklistAsync(http, orgUrl, field.ReferenceName, picklist, ct);

                    // Create the org-level field with retry.
                    var createError = await RetryWithBackoffAsync(
                        async attempt =>
                        {
                            await CreateFieldOrgLevelAsync(http, orgUrl, field, picklistId, ct);
                            return true;
                        },
                        ct);

                    if (createError is not null)
                    {
                        fieldsFailed.Add(new FieldBootstrapFailure
                        {
                            ReferenceName = field.ReferenceName,
                            Error = createError,
                            AttemptsExhausted = MaxRetryAttempts,
                        });
                        continue;   // cannot attach a field that was not created
                    }

                    fieldsCreated.Add(field.ReferenceName);
                }

                // Attach the field to the WIT — for both newly-created AND pre-existing fields. ADO's
                // add-field-to-WIT is idempotent (a no-op/conflict when already attached, logged not thrown).
                await RetryWithBackoffAsync(
                    async attempt =>
                    {
                        await AttachFieldToWitAsync(http, orgUrl, processId, witRefName, field.ReferenceName, ct);
                        return true;
                    },
                    ct);
            }
        }

        return new BootstrapManifest
        {
            Timestamp = DateTimeOffset.UtcNow,
            OrgUrl = orgUrl,
            Project = project,
            ProcessType = processType,
            FieldsCreated = fieldsCreated,
            FieldsExisting = fieldsExisting,
            FieldsFailed = fieldsFailed,
        };
    }

    private async Task<AdaptiveManifest> RunAdaptiveAsync(
        HttpClient http,
        string orgUrl,
        string project,
        AdoProcessType processType,
        AdoTelemetryFieldConfig config,
        CancellationToken ct)
    {
        var availableFields = await FetchAvailableFieldsAsync(http, orgUrl, project, ct);
        var mapping = new Dictionary<string, string>();
        var unmatchedFields = new List<string>();
        var logOnlyFields = new List<string>();

        foreach (var witConfig in config.WorkItemTypes.Values)
        {
            foreach (var field in witConfig.Fields)
            {
                var targetRef = BuildAdaptiveMapping(field, availableFields, config);
                if (targetRef is null)
                    unmatchedFields.Add(field.ReferenceName);
                else if (targetRef == string.Empty)
                    logOnlyFields.Add(field.ReferenceName);
                else if (!mapping.ContainsKey(field.ReferenceName))
                    mapping[field.ReferenceName] = targetRef;
            }
        }

        return new AdaptiveManifest
        {
            Timestamp = DateTimeOffset.UtcNow,
            OrgUrl = orgUrl,
            Project = project,
            ProcessType = processType,
            Mapping = mapping,
            UnmatchedFields = unmatchedFields,
            LogOnlyFields = logOnlyFields,
        };
    }

    // ── Private HTTP helpers ─────────────────────────────────────────────────────

    private async Task<(string OrgUrl, string Project, string Pat)> ResolveCredentialsAsync(CancellationToken ct)
    {
        var orgUrl = _defaultOptions.OrganizationUrl;
        var project = _defaultOptions.Project;
        var pat = _defaultOptions.Pat;

        if (_configRepo is null) return (orgUrl, project, pat);

        try
        {
            var configResult = await _configRepo.GetAsync(ConnectorType.AzureDevOps, ct);
            if (configResult?.NonSecretConfig is { } nsJson)
            {
                using var doc = JsonDocument.Parse(nsJson);
                if (doc.RootElement.TryGetProperty("organizationUrl", out var urlProp)
                    && !string.IsNullOrEmpty(urlProp.GetString()))
                    orgUrl = urlProp.GetString()!;
                if (doc.RootElement.TryGetProperty("projectName", out var projProp)
                    && !string.IsNullOrEmpty(projProp.GetString()))
                    project = projProp.GetString()!;
            }

            var secretsJson = await _configRepo.GetDecryptedSecretsAsync(ConnectorType.AzureDevOps, ct);
            if (secretsJson is not null)
            {
                using var doc = JsonDocument.Parse(secretsJson);
                if (doc.RootElement.TryGetProperty("personalAccessToken", out var patProp)
                    && !string.IsNullOrEmpty(patProp.GetString()))
                    pat = patProp.GetString()!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve ADO credentials from config repository — using IConfiguration fallback.");
        }

        return (orgUrl, project, pat);
    }

    private HttpClient BuildHttpClient(string pat)
    {
        var http = _httpClientFactory.CreateClient(nameof(AdoTelemetryPreflightService));
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        if (http.Timeout == TimeSpan.FromSeconds(100)) // default timeout unchanged
            http.Timeout = TimeSpan.FromSeconds(30);
        return http;
    }

    private async Task<(AdoProcessType ProcessType, string? TemplateTypeId, string? Error)> DetectProcessTypeAsync(
        HttpClient http, string orgUrl, string project, CancellationToken ct)
    {
        // Use the projects capabilities endpoint — the only documented source for templateTypeId.
        // _apis/work/process/configuration returns backlog config, not the process GUID.
        var url = $"{orgUrl.TrimEnd('/')}/_apis/projects/{Uri.EscapeDataString(project)}?includeCapabilities=true&api-version={ApiVersion}";
        try
        {
            var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return (AdoProcessType.Unsupported, null, $"Could not retrieve project capabilities: {(int)response.StatusCode} {response.ReasonPhrase}");

            var body = await response.Content.ReadAsStringAsync(ct);
            if (!LooksLikeJson(response, body))
                return (AdoProcessType.Unsupported, null,
                    "ADO returned an HTML page instead of JSON — the Personal Access Token is likely "
                    + "invalid or expired, or the organization URL is wrong. Update them in Connector Settings.");

            using var doc = JsonDocument.Parse(body);

            // capabilities.processTemplate.templateTypeId
            var templateTypeId =
                doc.RootElement.TryGetProperty("capabilities", out var caps) &&
                caps.TryGetProperty("processTemplate", out var pt) &&
                pt.TryGetProperty("templateTypeId", out var p)
                    ? p.GetString()
                    : null;

            var processType = ResolveKnownProcessType(templateTypeId);

            // Custom inherited processes don't match built-in GUIDs — walk one level up to the parent.
            if (processType == AdoProcessType.Unsupported && templateTypeId is not null)
                processType = await ResolveInheritedParentTypeAsync(http, orgUrl, templateTypeId, ct);

            if (processType == AdoProcessType.Unsupported)
            {
                var typeName = templateTypeId ?? "not found — check PAT scope includes 'Project: Read'";
                return (AdoProcessType.Unsupported, null,
                    $"The ADO project uses an unsupported process type (templateTypeId: {typeName}). " +
                    "Only Agile and Scrum inherited processes are supported.");
            }

            return (processType, templateTypeId, null);
        }
        catch (Exception ex)
        {
            return (AdoProcessType.Unsupported, null, $"ADO process detection failed: {ex.Message}");
        }
    }

    private async Task<(PreflightMode Mode, string? ProcessId, string? Error)> ProbeAdminAccessAsync(
        HttpClient http, string orgUrl, string templateTypeId, CancellationToken ct)
    {
        var url = $"{orgUrl.TrimEnd('/')}/_apis/process/processes?api-version={ApiVersion}";
        try
        {
            var response = await http.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return (PreflightMode.Adaptive, null, null);

            if (!response.IsSuccessStatusCode)
                return (PreflightMode.Bootstrap, null,
                    $"Could not probe ADO admin access: {(int)response.StatusCode} {response.ReasonPhrase}");

            // Extract the processId for the inherited process matching our templateTypeId.
            var body = await response.Content.ReadAsStringAsync(ct);
            var processId = ExtractProcessId(body, templateTypeId);

            return (PreflightMode.Bootstrap, processId ?? "unknown-process-id", null);
        }
        catch (Exception ex)
        {
            return (PreflightMode.Bootstrap, null, $"ADO admin probe failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Detects the HTML sign-in page Azure DevOps serves with HTTP 200 (instead of a 401) when a PAT
    /// is invalid/expired or the org URL is wrong, so callers surface an actionable error rather than
    /// the raw "'&lt;' is an invalid start of a value" JSON parse failure.
    /// </summary>
    private static bool LooksLikeJson(HttpResponseMessage response, string body)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            return false;

        var trimmed = body.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static string? ExtractProcessId(string processList, string templateTypeId)
    {
        try
        {
            using var doc = JsonDocument.Parse(processList);
            if (!doc.RootElement.TryGetProperty("value", out var values)) return null;
            foreach (var proc in values.EnumerateArray())
            {
                // The _apis/process/processes endpoint returns the process GUID in "id" and leaves
                // "typeId" empty; the project's templateTypeId equals that id. Match on either so we
                // find the process across both API shapes, and always return the concrete "id".
                var id = proc.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var typeId = proc.TryGetProperty("typeId", out var typeProp) ? typeProp.GetString() : null;

                if (string.Equals(id, templateTypeId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(typeId, templateTypeId, StringComparison.OrdinalIgnoreCase))
                    return id ?? typeId;
            }
        }
        catch { /* best-effort parse */ }
        return null;
    }

    private static AdoProcessType ResolveKnownProcessType(string? templateTypeId) =>
        templateTypeId?.ToLowerInvariant() switch
        {
            string id when id == AgileTemplateTypeId.ToLowerInvariant() => AdoProcessType.Agile,
            string id when id == ScrumTemplateTypeId.ToLowerInvariant() => AdoProcessType.Scrum,
            _ => AdoProcessType.Unsupported,
        };

    /// <summary>
    /// Calls _apis/work/processes/{typeId} (the inherited-process API) to read parentProcessTypeId.
    /// _apis/process/processes returns the basic Process object which omits parentProcessTypeId.
    /// </summary>
    private async Task<AdoProcessType> ResolveInheritedParentTypeAsync(
        HttpClient http, string orgUrl, string typeId, CancellationToken ct)
    {
        var url = $"{orgUrl.TrimEnd('/')}/_apis/work/processes/{Uri.EscapeDataString(typeId)}?api-version={ApiVersion}";
        try
        {
            var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return AdoProcessType.Unsupported;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);

            var parentId = doc.RootElement.TryGetProperty("parentProcessTypeId", out var p)
                ? p.GetString()
                : null;

            return ResolveKnownProcessType(parentId);
        }
        catch
        {
            return AdoProcessType.Unsupported;
        }
    }

    private async Task<bool> CheckFieldExistsAsync(HttpClient http, string orgUrl, string refName, CancellationToken ct)
    {
        var url = $"{orgUrl.TrimEnd('/')}/_apis/wit/fields/{Uri.EscapeDataString(refName)}?api-version={ApiVersion}";
        var response = await http.GetAsync(url, ct);
        return response.StatusCode == HttpStatusCode.OK;
    }

    private async Task<string?> CreatePicklistAsync(
        HttpClient http, string orgUrl, string fieldRefName, IReadOnlyList<string> values, CancellationToken ct)
    {
        var url = $"{orgUrl.TrimEnd('/')}/_apis/work/processes/lists?api-version={ApiVersion}";
        var body = JsonSerializer.Serialize(new
        {
            name = $"{fieldRefName}_picklist",
            items = values,
            type = "String",
            isSuggested = false,
        });
        var response = await http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // Picklist already exists — retrieve its ID.
            var listUrl = $"{orgUrl.TrimEnd('/')}/_apis/work/processes/lists?api-version={ApiVersion}";
            var listResp = await http.GetAsync(listUrl, ct);
            if (listResp.IsSuccessStatusCode)
            {
                var listBody = await listResp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(listBody);
                foreach (var list in doc.RootElement.GetProperty("value").EnumerateArray())
                {
                    if (list.TryGetProperty("name", out var nameProp)
                        && nameProp.GetString() == $"{fieldRefName}_picklist"
                        && list.TryGetProperty("id", out var idProp))
                        return idProp.GetString();
                }
            }
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Picklist creation for {Field} returned {Status} — falling back to plain string type.", fieldRefName, response.StatusCode);
            return null;
        }
        var respBody = await response.Content.ReadAsStringAsync(ct);
        using var respDoc = JsonDocument.Parse(respBody);
        return respDoc.RootElement.TryGetProperty("id", out var pid) ? pid.GetString() : null;
    }

    private async Task CreateFieldOrgLevelAsync(
        HttpClient http, string orgUrl, AdoTelemetryFieldDefinition field, string? picklistId, CancellationToken ct)
    {
        var url = $"{orgUrl.TrimEnd('/')}/_apis/wit/fields?api-version={ApiVersion}";
        var adoType = field.FieldType switch
        {
            AdoFieldType.String => "string",
            AdoFieldType.Integer => "integer",
            AdoFieldType.Double => "double",
            AdoFieldType.PicklistString when picklistId is not null => "picklistString",
            AdoFieldType.PicklistString => "string", // picklist creation failed — downgrade
            _ => "string",
        };

        object payload = picklistId is not null && field.FieldType == AdoFieldType.PicklistString
            ? new { name = field.Name, referenceName = field.ReferenceName, type = adoType, isPicklist = true, picklistId }
            : new { name = field.Name, referenceName = field.ReferenceName, type = adoType };

        var response = await http.PostAsync(url,
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task AttachFieldToWitAsync(
        HttpClient http, string orgUrl, string processId, string witRefName, string fieldRefName, CancellationToken ct)
    {
        var url = $"{orgUrl.TrimEnd('/')}/_apis/work/processes/{processId}/workItemTypes/{witRefName}/fields?api-version={ApiVersion}";
        var payload = JsonSerializer.Serialize(new { referenceName = fieldRefName });
        var response = await http.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("Field attach for {Field} to {Wit} returned {Status}.", fieldRefName, witRefName, response.StatusCode);
    }

    /// <summary>A work item type as it currently exists on an inherited process.</summary>
    private sealed record ProcessWit(
        string ReferenceName, string Name, string Customization, string? Inherits, string? Color, string? Icon);

    /// <summary>Reads the work item types currently on the process (empty list on any failure).</summary>
    private async Task<List<ProcessWit>> GetProcessWorkItemTypesAsync(
        HttpClient http, string orgUrl, string processId, CancellationToken ct)
    {
        var result = new List<ProcessWit>();
        var url = $"{orgUrl.TrimEnd('/')}/_apis/work/processes/{processId}/workitemtypes?api-version={ApiVersion}";
        try
        {
            var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return result;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("value", out var values)) return result;
            foreach (var w in values.EnumerateArray())
            {
                result.Add(new ProcessWit(
                    w.TryGetProperty("referenceName", out var rn) ? rn.GetString() ?? "" : "",
                    w.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
                    w.TryGetProperty("customization", out var cz) ? cz.GetString() ?? "" : "",
                    w.TryGetProperty("inherits", out var ih) ? ih.GetString() : null,
                    w.TryGetProperty("color", out var cl) ? cl.GetString() : null,
                    w.TryGetProperty("icon", out var ic) ? ic.GetString() : null));
            }
        }
        catch { /* best-effort — fall back to the system reference name */ }
        return result;
    }

    /// <summary>
    /// Returns a work item type reference name that fields can be attached to. In an inherited process a
    /// system type (customization == "system") cannot take new fields until it is MATERIALIZED as an
    /// inherited override — without this, the add-field call fails with VS402805 on every inherited
    /// process (the common real-world case). Already-inherited/custom types are returned as-is.
    /// </summary>
    private async Task<string?> ResolveCustomizableWitRefNameAsync(
        HttpClient http, string orgUrl, string processId, string systemRefName,
        List<ProcessWit> processWits, CancellationToken ct)
    {
        var match = processWits.FirstOrDefault(w =>
            string.Equals(w.ReferenceName, systemRefName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(w.Inherits, systemRefName, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return null;   // unknown — caller falls back to the system reference name

        // Already customizable (inherited or custom) — fields can be attached directly.
        if (!string.Equals(match.Customization, "system", StringComparison.OrdinalIgnoreCase))
            return match.ReferenceName;

        // Materialize an inherited override so the type becomes customizable. Reuse the system type's
        // own colour/icon so the inherited copy looks identical to the user.
        var payload = JsonSerializer.Serialize(new
        {
            name = match.Name,
            inheritsFrom = systemRefName,
            isDisabled = false,
            color = match.Color ?? "f6546a",
            icon = match.Icon ?? "icon_clipboard",
        });
        var url = $"{orgUrl.TrimEnd('/')}/_apis/work/processes/{processId}/workitemtypes?api-version={ApiVersion}";
        var response = await http.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Could not materialize inherited work item type {Wit}: {Status}.",
                systemRefName, response.StatusCode);
            return null;
        }

        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return created.RootElement.TryGetProperty("referenceName", out var rn) ? rn.GetString() : null;
    }

    private async Task<HashSet<string>> FetchAvailableFieldsAsync(
        HttpClient http, string orgUrl, string project, CancellationToken ct)
    {
        var url = $"{orgUrl.TrimEnd('/')}/{Uri.EscapeDataString(project)}/_apis/wit/fields?api-version={ApiVersion}";
        var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var body = await response.Content.ReadAsStringAsync(ct);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("value", out var values))
                foreach (var item in values.EnumerateArray())
                    if (item.TryGetProperty("referenceName", out var refProp) && refProp.GetString() is { } refName)
                        result.Add(refName);
        }
        catch { /* best-effort */ }
        return result;
    }

    /// <summary>
    /// Returns the target reference name for a field in Adaptive Mode:
    /// (1) exact match in available fields, (2) FallbackReferenceName if available, (3) log-only (empty string), (4) null=unmatched.
    /// </summary>
    private static string? BuildAdaptiveMapping(
        AdoTelemetryFieldDefinition field,
        HashSet<string> availableFields,
        AdoTelemetryFieldConfig config)
    {
        // Priority 1: exact match — the custom field already exists.
        if (availableFields.Contains(field.ReferenceName))
            return field.ReferenceName;

        // Priority 2: use the field-level fallback reference.
        if (field.FallbackReferenceName is { Length: > 0 } fb && availableFields.Contains(fb))
            return fb;

        // Priority 3: type-level fallback from config.
        if (config.FallbackStrategy.TryGetValue(field.FieldType, out var typeFallback))
        {
            if (typeFallback is { Length: > 0 } && availableFields.Contains(typeFallback))
                return typeFallback;
            if (typeFallback is null)
                return string.Empty; // log-only
        }

        return null; // truly unmatched
    }

    private async Task WriteManifestAsync(PreflightManifestBase manifest, CancellationToken ct)
    {
        var path = _manifestPathResolver.Resolve();
        var json = JsonSerializer.Serialize(manifest, ManifestSerializerOptions);
        await File.WriteAllTextAsync(path, json, ct);
        _logger.LogInformation("ADO bootstrap manifest written to {Path}", path);
    }

    /// <summary>
    /// Retries the given action up to <see cref="MaxRetryAttempts"/> times using exponential backoff.
    /// Retries on <see cref="HttpRequestException"/>, <see cref="TaskCanceledException"/> (transient),
    /// and HTTP 429/503. Returns null on success, or the final error message on exhaustion.
    /// </summary>
    private static async Task<string?> RetryWithBackoffAsync(
        Func<int, Task<bool>> action,
        CancellationToken ct)
    {
        string? lastError = null;
        for (var attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            try
            {
                await action(attempt);
                return null; // success
            }
            catch (HttpRequestException ex) when (IsTransient(ex))
            {
                lastError = ex.Message;
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                lastError = $"Request timed out: {ex.Message}";
            }

            if (attempt < MaxRetryAttempts - 1)
                await Task.Delay(RetryDelayFactory(attempt), ct);
        }
        return lastError ?? "Unknown error after retries exhausted.";
    }

    private static bool IsTransient(HttpRequestException ex)
    {
        if (ex.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            return true;
        return ex.StatusCode is null; // no status code = network-level failure
    }

    private static string ResolveWitRefName(AdoProcessType processType, string witKey)
    {
        if (WitRefNames.TryGetValue(processType, out var map) && map.TryGetValue(witKey, out var refName))
            return refName;
        return witKey;
    }

    // ── JSON loading ─────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static AdoTelemetryFieldConfig MapToDomain(FieldConfigJson json)
    {
        var fallbackStrategy = json.FallbackStrategy
            .ToDictionary(
                kvp => ParseFieldType(kvp.Key),
                kvp => kvp.Value);

        var workItemTypes = json.WorkItemTypes
            .ToDictionary(
                kvp => kvp.Key,
                kvp => new AdoTelemetryWorkItemTypeConfig
                {
                    WorkItemTypeName = kvp.Key, // resolved at runtime from process type
                    Fields = kvp.Value.Fields
                        .Select(f => new AdoTelemetryFieldDefinition
                        {
                            Name = f.Name,
                            ReferenceName = f.Reference,
                            FieldType = ParseFieldType(f.Type),
                            PicklistValues = f.Picklist,
                            Required = f.Required,
                            FallbackReferenceName = f.FallbackReference,
                            FallbackDisplayName = f.FallbackDisplayName,
                        })
                        .ToList(),
                });

        return new AdoTelemetryFieldConfig
        {
            Version = json.Version,
            WorkItemTypes = workItemTypes,
            FallbackStrategy = fallbackStrategy,
            TagsEncoding = json.TagsEncoding,
        };
    }

    private static AdoFieldType ParseFieldType(string typeStr) => typeStr switch
    {
        "string" => AdoFieldType.String,
        "integer" => AdoFieldType.Integer,
        "double" => AdoFieldType.Double,
        "picklistString" => AdoFieldType.PicklistString,
        _ => throw new InvalidOperationException($"Unknown ADO field type: '{typeStr}'"),
    };

    // ── Private JSON DTOs (file deserialization only — not in domain model) ──────

    private sealed record FieldConfigJson(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("work_item_types")] Dictionary<string, WitConfigJson> WorkItemTypes,
        [property: JsonPropertyName("fallback_strategy")] Dictionary<string, string?> FallbackStrategy,
        [property: JsonPropertyName("tags_encoding")] string TagsEncoding);

    private sealed record WitConfigJson(
        [property: JsonPropertyName("fields")] List<FieldDefinitionJson> Fields);

    private sealed record FieldDefinitionJson(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("picklist")] List<string>? Picklist,
        [property: JsonPropertyName("required")] bool Required,
        [property: JsonPropertyName("fallback_reference")] string? FallbackReference,
        [property: JsonPropertyName("fallback_display_name")] string? FallbackDisplayName);
}
