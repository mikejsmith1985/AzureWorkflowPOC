// Tests for JiraFieldProvisioner (spec-018 US2) — find-or-create field + ensure context; idempotent.
using System.Net;
using System.Text;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Web.Integrations.Jira;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

public sealed class JiraFieldProvisionerTests
{
    [Fact]
    public async Task Provision_MissingField_CreatesFieldAndContext()
    {
        var handler = new FakeHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/rest/api/3/field") return Json("[]");             // none exist
            if (request.Method == HttpMethod.Post && path == "/rest/api/3/field") return Json("""{"id":"customfield_900"}""", HttpStatusCode.Created);
            if (request.Method == HttpMethod.Get && path.Contains("/context")) return Json("""{"values":[]}""");  // no context yet
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        var result = await new JiraFieldProvisioner(Client(handler), NullLogger.Instance)
            .ProvisionAsync(MinimalConfig());

        Assert.True(result.IsSuccess);
        Assert.Contains("AIRuntimeCostUSD", result.FieldsReady);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.Path == "/rest/api/3/field");
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.Path.Contains("/context"));
    }

    [Fact]
    public async Task Provision_ExistingFieldWithContext_IsNoOp()
    {
        var handler = new FakeHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/rest/api/3/field")
                return Json("""[{"id":"customfield_900","name":"AIRuntimeCostUSD"}]""");
            if (request.Method == HttpMethod.Get && path.Contains("/context"))
                return Json("""{"values":[{"id":"ctx1"}]}""");
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await new JiraFieldProvisioner(Client(handler), NullLogger.Instance)
            .ProvisionAsync(MinimalConfig());

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.Path == "/rest/api/3/field");
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.Path.Contains("/context"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HttpClient Client(FakeHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://x.atlassian.net") };

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static AdoTelemetryFieldConfig MinimalConfig() => new()
    {
        Version = "1.0",
        WorkItemTypes = new Dictionary<string, AdoTelemetryWorkItemTypeConfig>
        {
            ["UserStory"] = new()
            {
                WorkItemTypeName = "User Story",
                Fields = new List<AdoTelemetryFieldDefinition>
                {
                    new() { Name = "AI Runtime Cost USD", ReferenceName = "Custom.AIRuntimeCostUSD", FieldType = AdoFieldType.Double, Required = false },
                },
            },
        },
        FallbackStrategy = new Dictionary<AdoFieldType, string?> { [AdoFieldType.Double] = null },
        TagsEncoding = "pipe_separated_kv",
    };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            return Task.FromResult(_responder(request));
        }
    }
}
