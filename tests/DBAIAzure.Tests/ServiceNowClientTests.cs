// Unit tests for ServiceNowClient — verifies the Table API request is built against the instance
// origin even when the operator stored a full browser URL such as ".../login.do" (FR-008).
using System.Net;
using System.Text.Json;
using DBAIAzure.Connectors;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Confirms that <see cref="ServiceNowClient"/> normalizes the configured instance URL to its origin
/// (scheme + host) before appending the Table API path. Operators frequently paste the URL straight
/// from the browser address bar after logging in (e.g. <c>https://acme.service-now.com/login.do</c>),
/// and appending the API path to that would 302-redirect to the login page instead of authenticating.
/// </summary>
public sealed class ServiceNowClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("https://dev275188.service-now.com/login.do")]
    [InlineData("https://dev275188.service-now.com/nav_to.do?uri=%2Fhome.do")]
    [InlineData("https://dev275188.service-now.com/")]
    [InlineData("https://dev275188.service-now.com")]
    public async Task TestConnectionAsync_BuildsTableApiUrlAgainstInstanceOrigin(string storedInstanceUrl)
    {
        // Arrange — a capturing HTTP handler that records the URL the client actually requests
        // and returns a minimal valid Table API response so the test path reaches success.
        var capturingHandler = new CapturingHandler(
            responseBody: """{ "result": [ { "name": "glide.servlet.uri" } ] }""");

        var configRepo = new FakeConnectorConfigRepository(
            nonSecretConfigJson: JsonSerializer.Serialize(
                new ServiceNowConnectorConfig(storedInstanceUrl, "admin"), JsonOptions),
            decryptedSecretsJson: """{ "password": "irrelevant-for-url-test" }""");

        var client = new ServiceNowClient(
            new SingleHandlerHttpClientFactory(capturingHandler), configRepo);

        // Act
        var result = await client.TestConnectionAsync();

        // Assert — the request must target the instance origin, never the stored path/query.
        Assert.NotNull(capturingHandler.LastRequestUri);
        Assert.Equal(
            "https://dev275188.service-now.com/api/now/table/sys_properties?sysparm_limit=1",
            capturingHandler.LastRequestUri!.ToString());
        Assert.True(result.IsSuccess, result.Message);
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    /// <summary>Captures the last outgoing request URI and returns a canned 200 response.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public CapturingHandler(string responseBody) => _responseBody = responseBody;

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody),
            });
        }
    }

    /// <summary>Hands out a single <see cref="HttpClient"/> wired to the capturing handler.</summary>
    private sealed class SingleHandlerHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public SingleHandlerHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    /// <summary>Minimal repository fake returning preset ServiceNow config and decrypted secrets.</summary>
    private sealed class FakeConnectorConfigRepository : IConnectorConfigRepository
    {
        private readonly string _nonSecretConfigJson;
        private readonly string _decryptedSecretsJson;

        public FakeConnectorConfigRepository(string nonSecretConfigJson, string decryptedSecretsJson)
        {
            _nonSecretConfigJson  = nonSecretConfigJson;
            _decryptedSecretsJson = decryptedSecretsJson;
        }

        public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult<ConnectorConfig?>(new ConnectorConfig(
                type, _nonSecretConfigJson, HasSecrets: true, IsConfigured: true,
                LastUpdatedAt: DateTimeOffset.UtcNow, LastTestResult: null));

        public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult<string?>(_decryptedSecretsJson);

        public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task SaveAsync(ConnectorType type, string? nonSecretConfigJson,
            string? plaintextSecretsJson, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result,
            CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
