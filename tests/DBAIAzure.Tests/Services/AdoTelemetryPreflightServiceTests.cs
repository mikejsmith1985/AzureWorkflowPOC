// Unit tests for AdoTelemetryPreflightService covering US1, US2, and US4 (spec-009).
using System.Net;
using System.Text;
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Web.Integrations.AzureDevOps;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DBAIAzure.Tests.Services;

/// <summary>
/// Covers the ADO telemetry preflight service in isolation.
/// All HTTP calls are intercepted by a <see cref="FakeHttpHandler"/> — no live ADO calls are made.
/// Integration tests (live ADO) are gated with [Trait("Category","Integration")] and excluded from CI.
/// </summary>
public sealed class AdoTelemetryPreflightServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static AdoTelemetryFieldConfig MinimalFieldConfig(
        string? fallbackRefForString = "System.Tags",
        string? fallbackRefForInteger = null) =>
        new()
        {
            Version = "1.0",
            WorkItemTypes = new Dictionary<string, AdoTelemetryWorkItemTypeConfig>
            {
                ["UserStory"] = new()
                {
                    WorkItemTypeName = "User Story",
                    Fields = new List<AdoTelemetryFieldDefinition>
                    {
                        new()
                        {
                            Name = "AI Session ID",
                            ReferenceName = "Custom.AISessionID",
                            FieldType = AdoFieldType.String,
                            Required = false,
                            FallbackReferenceName = fallbackRefForString,
                        },
                        new()
                        {
                            Name = "AI Input Tokens",
                            ReferenceName = "Custom.AIInputTokens",
                            FieldType = AdoFieldType.Integer,
                            Required = false,
                            FallbackReferenceName = fallbackRefForInteger,
                        },
                    },
                },
            },
            FallbackStrategy = new Dictionary<AdoFieldType, string?>
            {
                [AdoFieldType.String] = "System.Tags",
                [AdoFieldType.Integer] = null,
                [AdoFieldType.Double] = null,
                [AdoFieldType.PicklistString] = "System.Tags",
            },
            TagsEncoding = "pipe_separated_kv",
        };

    private static IOptions<AzureDevOpsOptions> OptionsWithOrg(string orgUrl = "https://dev.azure.com/testorg", string project = "TestProject") =>
        Options.Create(new AzureDevOpsOptions { OrganizationUrl = orgUrl, Project = project, Pat = "fake-pat" });

    private static AdoTelemetryPreflightService BuildService(
        FakeHttpHandler handler,
        IOptions<AzureDevOpsOptions>? options = null,
        IConnectorConfigRepository? repo = null,
        ManifestPathResolver? manifestResolver = null)
    {
        var httpFactory = new FakeHttpClientFactory(handler);
        return new AdoTelemetryPreflightService(
            httpFactory,
            options ?? OptionsWithOrg(),
            repo,
            manifestResolver ?? new NullManifestPathResolver(),
            NullLogger<AdoTelemetryPreflightService>.Instance);
    }

    // ── T013: Missing org URL → fail fast, no HTTP calls ─────────────────────────

    [Fact]
    public async Task RunPreflightAsync_WhenOrgUrlBlank_ReturnsFail_NoHttpCalls()
    {
        var handler = new FakeHttpHandler();
        var service = BuildService(handler, options: Options.Create(new AzureDevOpsOptions
        {
            OrganizationUrl = "",
            Project = "TestProject",
            Pat = "fake-pat",
        }));

        var result = await service.RunPreflightAsync(MinimalFieldConfig());

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(0, handler.CallCount);
    }

    // ── T014: Process type detection ──────────────────────────────────────────────

    [Fact]
    public async Task RunPreflightAsync_WhenProcessIsAgile_DetectsAgile()
    {
        var handler = new FakeHttpHandler();
        handler.AddResponse(
            url => url.Contains("/_apis/projects/") && url.Contains("includeCapabilities=true"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"capabilities":{"processTemplate":{"templateTypeId":"6b724908-ef14-45cf-84f8-768b5384da45"}}}""",
                    Encoding.UTF8, "application/json"),
            });
        handler.AddDefaultAdminOkResponse();
        handler.AddDefaultFieldExistsResponse(HttpStatusCode.NotFound);
        handler.AddDefaultFieldCreateResponse();
        handler.AddDefaultFieldAttachResponse();
        handler.AddDefaultManifestResponse();

        var service = BuildService(handler);
        var result = await service.RunPreflightAsync(MinimalFieldConfig());

        Assert.True(result.IsSuccess);
        Assert.IsType<BootstrapManifest>(result.Manifest);
        var manifest = (BootstrapManifest)result.Manifest!;
        Assert.Equal(AdoProcessType.Agile, manifest.ProcessType);
    }

    [Fact]
    public async Task RunPreflightAsync_WhenProcessIsScrum_DetectsScrum()
    {
        var handler = new FakeHttpHandler();
        handler.AddResponse(
            url => url.Contains("/_apis/projects/") && url.Contains("includeCapabilities=true"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"capabilities":{"processTemplate":{"templateTypeId":"adcc42ab-9882-485e-a3ed-7678f01f66bc"}}}""",
                    Encoding.UTF8, "application/json"),
            });
        handler.AddDefaultAdminOkResponse();
        handler.AddDefaultFieldExistsResponse(HttpStatusCode.NotFound);
        handler.AddDefaultFieldCreateResponse();
        handler.AddDefaultFieldAttachResponse();
        handler.AddDefaultManifestResponse();

        var service = BuildService(handler);
        var result = await service.RunPreflightAsync(MinimalFieldConfig());

        Assert.True(result.IsSuccess);
        var manifest = (BootstrapManifest)result.Manifest!;
        Assert.Equal(AdoProcessType.Scrum, manifest.ProcessType);
    }

    [Fact]
    public async Task RunPreflightAsync_WhenProcessIsCmmi_ReturnsFail_WithCmmiInError()
    {
        var handler = new FakeHttpHandler();
        handler.AddResponse(
            url => url.Contains("/_apis/projects/") && url.Contains("includeCapabilities=true"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"capabilities":{"processTemplate":{"templateTypeId":"27450541-8e31-4150-9947-dc59f998fc01"}}}""",
                    Encoding.UTF8, "application/json"),
            });

        var service = BuildService(handler);
        var result = await service.RunPreflightAsync(MinimalFieldConfig());

        Assert.False(result.IsSuccess);
        Assert.Contains("unsupported process type", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunPreflightAsync_WhenProcessIsCustomInheritedFromAgile_DetectsAgile()
    {
        const string customGuid  = "b8a3a935-7e91-48b8-a94c-606d37c3e9f2";
        const string agileParent = "6b724908-ef14-45cf-84f8-768b5384da45";
        var handler = new FakeHttpHandler();
        handler.AddResponse(
            url => url.Contains("/_apis/projects/") && url.Contains("includeCapabilities=true"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"capabilities\":{{\"processTemplate\":{{\"templateTypeId\":\"{customGuid}\"}}}}}}",
                    Encoding.UTF8, "application/json"),
            });
        // Service resolves inherited parent via _apis/process/processes/{guid}
        handler.AddResponse(
            url => url.Contains($"/_apis/process/processes/{customGuid}"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"id\":\"{customGuid}\",\"type\":\"inherited\",\"parentProcessTypeId\":\"{agileParent}\"}}",
                    Encoding.UTF8, "application/json"),
            });
        handler.AddDefaultAdminOkResponse();
        handler.AddDefaultFieldExistsResponse(HttpStatusCode.NotFound);
        handler.AddDefaultFieldCreateResponse();
        handler.AddDefaultFieldAttachResponse();
        handler.AddDefaultManifestResponse();

        var service = BuildService(handler);
        var result  = await service.RunPreflightAsync(MinimalFieldConfig());

        Assert.True(result.IsSuccess);
        Assert.Equal(AdoProcessType.Agile, ((BootstrapManifest)result.Manifest!).ProcessType);
    }

    // ── T015: Bootstrap field operations ─────────────────────────────────────────

    [Fact]
    public async Task RunPreflightAsync_WhenFieldDoesNotExist_CreatesField()
    {
        var handler = new FakeHttpHandler();
        handler.AddDefaultProcessResponse(isAgile: true);
        handler.AddDefaultAdminOkResponse();
        handler.AddDefaultFieldExistsResponse(HttpStatusCode.NotFound);
        handler.AddDefaultFieldCreateResponse();
        handler.AddDefaultFieldAttachResponse();

        var service = BuildService(handler);
        var result = await service.RunPreflightAsync(MinimalFieldConfig());

        Assert.True(result.IsSuccess);
        var manifest = (BootstrapManifest)result.Manifest!;
        Assert.Contains("Custom.AISessionID", manifest.FieldsCreated);
    }

    [Fact]
    public async Task RunPreflightAsync_WhenFieldAlreadyExists_SkipsCreation()
    {
        var handler = new FakeHttpHandler();
        handler.AddDefaultProcessResponse(isAgile: true);
        handler.AddDefaultAdminOkResponse();
        handler.AddDefaultFieldExistsResponse(HttpStatusCode.OK);

        var service = BuildService(handler);
        var result = await service.RunPreflightAsync(MinimalFieldConfig());

        Assert.True(result.IsSuccess);
        var manifest = (BootstrapManifest)result.Manifest!;
        Assert.Contains("Custom.AISessionID", manifest.FieldsExisting);
        Assert.Empty(manifest.FieldsCreated);
    }

    [Fact]
    public async Task RunPreflightAsync_WhenFieldCreation429TwiceThenSucceeds_RetriesAndCreates()
    {
        var handler = new FakeHttpHandler();
        handler.AddDefaultProcessResponse(isAgile: true);
        handler.AddDefaultAdminOkResponse();
        handler.AddDefaultFieldExistsResponse(HttpStatusCode.NotFound);
        // First two POST attempts → 429, third → 200
        handler.AddSequencedResponse(
            url => url.Contains("/_apis/wit/fields") && !url.Contains("/"),
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"referenceName":"Custom.AISessionID"}""", Encoding.UTF8, "application/json"),
            });
        handler.AddDefaultFieldAttachResponse();

        var service = BuildService(handler, options: OptionsWithOrg());
        var result = await service.RunPreflightAsync(MinimalFieldConfig());

        Assert.True(result.IsSuccess);
        var manifest = (BootstrapManifest)result.Manifest!;
        Assert.Contains("Custom.AISessionID", manifest.FieldsCreated);
    }

    [Fact]
    public async Task RunPreflightAsync_WhenFieldCreation429ThreeTimes_RecordsFieldFailed_RunContinues()
    {
        // Eliminate real backoff delays so the unit test completes in milliseconds.
        AdoTelemetryPreflightService.RetryDelayFactory = _ => TimeSpan.Zero;

        var handler = new FakeHttpHandler();
        handler.AddDefaultProcessResponse(isAgile: true);
        handler.AddDefaultAdminOkResponse();
        handler.AddDefaultFieldExistsResponse(HttpStatusCode.NotFound);
        // All three POST attempts → 429
        handler.AddSequencedResponse(
            url => url.Contains("/_apis/wit/fields") && !url.Contains("Custom."),
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var service = BuildService(handler, options: OptionsWithOrg());
        var result = await service.RunPreflightAsync(MinimalFieldConfig());

        Assert.True(result.IsSuccess, "Partial failure → IsSuccess=true, run continues");
        var manifest = (BootstrapManifest)result.Manifest!;
        Assert.Contains(manifest.FieldsFailed, f => f.ReferenceName == "Custom.AISessionID");
    }

    // ── T038: Adaptive mode — 403 probe ──────────────────────────────────────────

    [Fact]
    public async Task RunPreflightAsync_WhenAdminProbeReturns403_SwitchesToAdaptiveMode()
    {
        var handler = new FakeHttpHandler();
        handler.AddDefaultProcessResponse(isAgile: true);
        // Admin probe → 403
        handler.AddResponse(
            url => url.Contains("/_apis/projects") || url.Contains("/_apis/process"),
            new HttpResponseMessage(HttpStatusCode.Forbidden));
        handler.AddDefaultAvailableFieldsResponse(new[] { "System.Tags", "Microsoft.VSTS.Scheduling.StoryPoints" });

        var service = BuildService(handler);
        var result = await service.RunPreflightAsync(MinimalFieldConfig());

        Assert.True(result.IsSuccess);
        Assert.IsType<AdaptiveManifest>(result.Manifest);
    }

    [Fact]
    public async Task RunPreflightAsync_Adaptive_StringField_MapsToSystemTags()
    {
        var handler = new FakeHttpHandler();
        handler.AddDefaultProcessResponse(isAgile: true);
        handler.AddAdminForbiddenResponse();
        handler.AddDefaultAvailableFieldsResponse(new[] { "System.Tags" });

        var service = BuildService(handler);
        var result = await service.RunPreflightAsync(MinimalFieldConfig(fallbackRefForString: "System.Tags"));

        var manifest = (AdaptiveManifest)result.Manifest!;
        Assert.Equal("System.Tags", manifest.Mapping["Custom.AISessionID"]);
    }

    [Fact]
    public async Task RunPreflightAsync_Adaptive_IntegerField_NullFallback_GoesToLogOnly()
    {
        var handler = new FakeHttpHandler();
        handler.AddDefaultProcessResponse(isAgile: true);
        handler.AddAdminForbiddenResponse();
        handler.AddDefaultAvailableFieldsResponse(new[] { "System.Tags" });

        var service = BuildService(handler);
        var result = await service.RunPreflightAsync(MinimalFieldConfig(fallbackRefForInteger: null));

        var manifest = (AdaptiveManifest)result.Manifest!;
        Assert.Contains("Custom.AIInputTokens", manifest.LogOnlyFields);
    }

    // ── T039: Adaptive exact match takes priority ─────────────────────────────────

    [Fact]
    public async Task RunPreflightAsync_Adaptive_ExactCustomFieldMatch_UsesThatFieldNotTags()
    {
        var handler = new FakeHttpHandler();
        handler.AddDefaultProcessResponse(isAgile: true);
        handler.AddAdminForbiddenResponse();
        // Custom.AISessionID already exists as a custom field
        handler.AddDefaultAvailableFieldsResponse(new[] { "System.Tags", "Custom.AISessionID" });

        var service = BuildService(handler);
        var result = await service.RunPreflightAsync(MinimalFieldConfig());

        var manifest = (AdaptiveManifest)result.Manifest!;
        Assert.Equal("Custom.AISessionID", manifest.Mapping["Custom.AISessionID"]);
    }

    // ── T045: Config loading ──────────────────────────────────────────────────────

    [Fact]
    public async Task LoadDefaultConfig_EmbeddedResource_Deserializes_With12UserStoryAnd2TaskFields()
    {
        var config = await AdoTelemetryPreflightService.LoadDefaultConfigAsync(CancellationToken.None);

        Assert.Equal("1.0", config.Version);
        Assert.True(config.WorkItemTypes.ContainsKey("UserStory"));
        Assert.True(config.WorkItemTypes.ContainsKey("Task"));
        Assert.Equal(12, config.WorkItemTypes["UserStory"].Fields.Count);
        Assert.Equal(2, config.WorkItemTypes["Task"].Fields.Count);

        var speckitPhase = config.WorkItemTypes["UserStory"].Fields
            .First(f => f.ReferenceName == "Custom.SpeckitPhase");
        Assert.Equal(AdoFieldType.PicklistString, speckitPhase.FieldType);
        Assert.NotNull(speckitPhase.PicklistValues);
        Assert.Contains("Spec", speckitPhase.PicklistValues!);
    }

    [Fact]
    public async Task RunPreflightAsync_WhenOverrideConfigProvided_UsesOverride()
    {
        var handler = new FakeHttpHandler();
        handler.AddDefaultProcessResponse(isAgile: true);
        handler.AddDefaultAdminOkResponse();
        handler.AddDefaultFieldExistsResponse(HttpStatusCode.NotFound);
        handler.AddDefaultFieldCreateResponse();
        handler.AddDefaultFieldAttachResponse();

        var customConfig = MinimalFieldConfig(); // only 2 fields
        var service = BuildService(handler);
        var result = await service.RunPreflightAsync(customConfig);

        Assert.True(result.IsSuccess);
        var manifest = (BootstrapManifest)result.Manifest!;
        // Only the 2 fields from customConfig should appear — not the full 14 from embedded default
        Assert.Equal(2, manifest.FieldsCreated.Count + manifest.FieldsExisting.Count + manifest.FieldsFailed.Count);
    }
}

// ── Fake HTTP infrastructure ──────────────────────────────────────────────────

/// <summary>
/// In-memory HTTP handler that serves scripted responses matched by URL predicate.
/// Supports sequenced multi-response per predicate for retry testing.
/// </summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly List<(Func<string, bool> Match, Queue<HttpResponseMessage> Responses)> _rules = new();
    public int CallCount { get; private set; }

    public void AddResponse(Func<string, bool> match, HttpResponseMessage response)
    {
        var queue = new Queue<HttpResponseMessage>();
        queue.Enqueue(response);
        _rules.Add((match, queue));
    }

    public void AddSequencedResponse(Func<string, bool> match, params HttpResponseMessage[] responses)
    {
        var queue = new Queue<HttpResponseMessage>(responses);
        _rules.Add((match, queue));
    }

    public void AddDefaultProcessResponse(bool isAgile)
    {
        var templateId = isAgile
            ? "6b724908-ef14-45cf-84f8-768b5384da45"
            : "adcc42ab-9882-485e-a3ed-7678f01f66bc";
        // Service now calls _apis/projects/{project}?includeCapabilities=true for templateTypeId.
        AddResponse(
            url => url.Contains("/_apis/projects/") && url.Contains("includeCapabilities=true"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"capabilities\":{{\"processTemplate\":{{\"templateTypeId\":\"{templateId}\"}}}}}}",
                    Encoding.UTF8, "application/json"),
            });
    }

    // Returns process list with a process entry whose typeId matches both Agile and Scrum GUIDs
    // so the service can find the processId regardless of which was detected.
    public void AddDefaultAdminOkResponse() =>
        AddResponse(
            url => url.Contains("/_apis/process/processes"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"value":[{"id":"test-process-id","typeId":"6b724908-ef14-45cf-84f8-768b5384da45","name":"TestAgile"},{"id":"test-scrum-id","typeId":"adcc42ab-9882-485e-a3ed-7678f01f66bc","name":"TestScrum"}],"count":2}""",
                    Encoding.UTF8, "application/json"),
            });

    public void AddAdminForbiddenResponse() =>
        AddResponse(
            url => url.Contains("/_apis/process/processes"),
            new HttpResponseMessage(HttpStatusCode.Forbidden));

    public void AddDefaultFieldExistsResponse(HttpStatusCode status) =>
        AddResponse(
            url => url.Contains("/_apis/wit/fields/Custom."),
            new HttpResponseMessage(status)
            {
                Content = status == HttpStatusCode.OK
                    ? new StringContent("""{"referenceName":"Custom.AISessionID"}""", Encoding.UTF8, "application/json")
                    : null,
            });

    public void AddDefaultFieldCreateResponse() =>
        AddResponse(
            url => url.Contains("/_apis/wit/fields") && !url.Contains("Custom."),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"referenceName":"Custom.AISessionID"}""", Encoding.UTF8, "application/json"),
            });

    public void AddDefaultFieldAttachResponse() =>
        AddResponse(
            url => url.Contains("/workItemTypes/") && url.Contains("/fields"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });

    public void AddDefaultManifestResponse() { /* manifest write is file I/O, not HTTP */ }

    public void AddDefaultAvailableFieldsResponse(IEnumerable<string> referenceNames)
    {
        var fields = referenceNames.Select(r => $"{{\"referenceName\":\"{r}\"}}");
        var json = $"{{\"value\":[{string.Join(",", fields)}],\"count\":{fields.Count()}}}";
        AddResponse(
            url => url.Contains("/_apis/wit/fields") && !url.Contains("Custom."),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        var url = request.RequestUri?.ToString() ?? "";
        foreach (var (match, responses) in _rules)
        {
            if (!match(url)) continue;
            if (responses.TryDequeue(out var response))
            {
                // Re-enqueue last response so it stays available for subsequent identical calls
                if (responses.Count == 0) responses.Enqueue(CloneResponse(response));
                return Task.FromResult(response);
            }
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
    }

    private static HttpResponseMessage CloneResponse(HttpResponseMessage original)
    {
        var clone = new HttpResponseMessage(original.StatusCode);
        if (original.Content != null)
        {
            var content = original.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            clone.Content = new StringContent(content,
                Encoding.UTF8,
                original.Content.Headers.ContentType?.MediaType ?? "application/json");
        }
        return clone;
    }
}

internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly FakeHttpHandler _handler;
    public FakeHttpClientFactory(FakeHttpHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler) { Timeout = TimeSpan.FromSeconds(5) };
}

/// <summary>No-op manifest path resolver — avoids file system writes in unit tests.</summary>
internal sealed class NullManifestPathResolver : ManifestPathResolver
{
    public NullManifestPathResolver() : base(null!, null!) { }
    public override string Resolve() => Path.Combine(Path.GetTempPath(), "null-manifest.json");
}
