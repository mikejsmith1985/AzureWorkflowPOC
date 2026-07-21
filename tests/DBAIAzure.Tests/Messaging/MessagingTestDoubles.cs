// Shared test doubles for Messaging delivery tests — a mutable config repo and a capturing HTTP handler.
using System.Net;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Tests.Messaging;

/// <summary>
/// Minimal <see cref="IConnectorConfigRepository"/> fake whose Messaging non-secret config and decrypted
/// secrets are mutable, so tests can prove per-call resolution (hot-reload). Counts reads.
/// </summary>
internal sealed class StubConnectorConfigRepository : IConnectorConfigRepository
{
    public string? NonSecretConfigJson { get; set; }
    public string? DecryptedSecretsJson { get; set; }
    public int GetAsyncCallCount { get; private set; }

    public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default)
    {
        GetAsyncCallCount++;
        var config = NonSecretConfigJson is null
            ? null
            : new ConnectorConfig(type, NonSecretConfigJson, HasSecrets: DecryptedSecretsJson is not null,
                IsConfigured: true, LastUpdatedAt: DateTimeOffset.UtcNow, LastTestResult: null);
        return Task.FromResult(config);
    }

    public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) =>
        Task.FromResult(DecryptedSecretsJson);

    public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task SaveAsync(ConnectorType type, string? nonSecretConfigJson, string? plaintextSecretsJson,
        CancellationToken ct = default) => throw new NotImplementedException();

    public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result,
        CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Captures the last request body/URI and returns a canned response (or throws if configured).</summary>
internal sealed class CapturingHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _responseBody;
    private readonly bool _throwTransport;

    public CapturingHttpHandler(HttpStatusCode status, string responseBody, bool throwTransport = false)
    {
        _status = status;
        _responseBody = responseBody;
        _throwTransport = throwTransport;
    }

    public string? LastRequestBody { get; private set; }
    public Uri? LastRequestUri { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        if (_throwTransport)
            throw new HttpRequestException("simulated transport failure");

        return new HttpResponseMessage(_status) { Content = new StringContent(_responseBody) };
    }
}

/// <summary>Hands out a single <see cref="HttpClient"/> wired to the given handler.</summary>
internal sealed class SingleHandlerHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public SingleHandlerHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

/// <summary>Records the MCP send request and returns a preset result — no real MCP server involved.</summary>
internal sealed class FakeMcpMessageGateway : DBAIAzure.Connectors.Messaging.IMcpMessageGateway
{
    private readonly bool _succeeds;
    private readonly string _message;

    public FakeMcpMessageGateway(bool succeeds, string message = "ok")
    {
        _succeeds = succeeds;
        _message = message;
    }

    public DBAIAzure.Connectors.Messaging.McpSendRequest? LastRequest { get; private set; }

    public Task<DBAIAzure.Connectors.Messaging.McpSendResult> SendAsync(
        DBAIAzure.Connectors.Messaging.McpSendRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(new DBAIAzure.Connectors.Messaging.McpSendResult(_succeeds, _message));
    }

    // The send-path tests don't read threads; return an empty content success so the interface is satisfied.
    public Task<DBAIAzure.Connectors.Messaging.McpReadResult> ReadAsync(
        DBAIAzure.Connectors.Messaging.McpReadRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DBAIAzure.Connectors.Messaging.McpReadResult(_succeeds, null, _message));
}
