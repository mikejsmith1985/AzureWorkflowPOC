// Unit tests for the Jira connection test behind the health-checker seam (spec-020, T031).
using System.Net;
using System.Text;
using DBAIAzure.Connectors;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

/// <summary>
/// Verifies <see cref="JiraConnectorTester"/> reports actionable pass/fail from the /myself + /project probes,
/// creates no issue, and fails cleanly when unconfigured — using a fake Jira REST handler + a stub store.
/// </summary>
public class JiraConnectorTesterTests
{
    private const string JiraJson = """{"provider":"Jira","siteUrl":"https://x.atlassian.net","email":"a@b.c","projectKey":"PROJ"}""";
    private const string SecretJson = """{"apiToken":"tok"}""";

    [Fact]
    public async Task Passes_WhenAuthAndProjectSucceed()
    {
        var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/myself")
                ? Json("""{"displayName":"Ada Lovelace"}""")
                : Json("""{"key":"PROJ","name":"Project"}"""));
        var result = await Build(handler, JiraJson, SecretJson).TestConnectionAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains("Ada Lovelace", result.Message);
        Assert.Contains("PROJ", result.Message);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);   // no issue created
    }

    [Fact]
    public async Task Fails_WithActionableMessage_WhenTokenRejected()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var result = await Build(handler, JiraJson, SecretJson).TestConnectionAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("Authentication failed", result.Message);
    }

    [Fact]
    public async Task Fails_WhenProjectNotFound()
    {
        var handler = new FakeHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/myself")
                ? Json("""{"displayName":"Ada"}""")
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        var result = await Build(handler, JiraJson, SecretJson).TestConnectionAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("PROJ", result.Message);
    }

    [Fact]
    public async Task Fails_WhenNotConfigured()
    {
        var result = await Build(new FakeHandler(_ => Json("{}")), nonSecret: null, secret: null).TestConnectionAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("not fully configured", result.Message);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JiraConnectorTester Build(FakeHandler handler, string? nonSecret, string? secret) =>
        new(new StubRepo(nonSecret, secret), new SingleClientFactory(new HttpClient(handler)));

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

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

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubRepo : IConnectorConfigRepository
    {
        private readonly string? _nonSecret;
        private readonly string? _secret;
        public StubRepo(string? nonSecret, string? secret) { _nonSecret = nonSecret; _secret = secret; }

        public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult<ConnectorConfig?>(_nonSecret is null
                ? null
                : new ConnectorConfig(type, _nonSecret, true, true, default, null));
        public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult(_secret);
        public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConnectorConfig>>([]);
        public Task SaveAsync(ConnectorType type, string? nonSecretConfigJson, string? plaintextSecretsJson, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
