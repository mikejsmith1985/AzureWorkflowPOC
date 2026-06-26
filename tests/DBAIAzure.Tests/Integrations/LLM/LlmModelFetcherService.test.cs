// Unit tests for LlmModelFetcherService — verifies request headers and response parsing.
using System.Net;
using System.Text;
using DBAIAzure.Web.Integrations.LLM;
using Xunit;

namespace DBAIAzure.Tests.Integrations.LLM;

/// <summary>
/// Verifies that <see cref="LlmModelFetcherService"/> builds the correct HTTP requests for
/// each provider and parses model IDs from their respective response schemas.
/// </summary>
public sealed class LlmModelFetcherServiceTests
{
    // ── Anthropic ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FetchModelsAsync_Anthropic_SendsCorrectHeaders()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"data":[{"id":"claude-opus-4-8"},{"id":"claude-sonnet-4-6"}]}""");

        var service = BuildService(handler);
        await service.FetchModelsAsync("anthropic", "test-key");

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.Contains("x-api-key"));
        Assert.True(handler.LastRequest.Headers.Contains("anthropic-version"));
    }

    [Fact]
    public async Task FetchModelsAsync_Anthropic_ParsesModelIds()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"data":[{"id":"claude-opus-4-8"},{"id":"claude-sonnet-4-6"},{"id":"claude-haiku-4-5"}]}""");

        var service = BuildService(handler);
        var models = await service.FetchModelsAsync("anthropic", "test-key");

        Assert.Equal(3, models.Count);
        Assert.Contains("claude-opus-4-8", models);
        Assert.Contains("claude-sonnet-4-6", models);
    }

    [Fact]
    public async Task FetchModelsAsync_Anthropic_ReturnsSortedList()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"data":[{"id":"claude-z-model"},{"id":"claude-a-model"}]}""");

        var service = BuildService(handler);
        var models = await service.FetchModelsAsync("anthropic", "key");

        Assert.Equal("claude-a-model", models[0]);
        Assert.Equal("claude-z-model", models[1]);
    }

    [Fact]
    public async Task FetchModelsAsync_Anthropic_ThrowsOnUnauthorized()
    {
        var handler = new CapturingHandler(HttpStatusCode.Unauthorized, """{"error":"invalid key"}""");
        var service = BuildService(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.FetchModelsAsync("anthropic", "bad-key"));
    }

    // ── OpenAI ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task FetchModelsAsync_OpenAi_SendsBearerAuth()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"object":"list","data":[{"id":"gpt-4o"},{"id":"gpt-4o-mini"}]}""");

        var service = BuildService(handler);
        await service.FetchModelsAsync("openai", "sk-test");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("sk-test", handler.LastRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task FetchModelsAsync_OpenAi_ParsesModelIds()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"object":"list","data":[{"id":"gpt-4o"},{"id":"gpt-4o-mini"},{"id":"o3"}]}""");

        var service = BuildService(handler);
        var models = await service.FetchModelsAsync("openai", "sk-test");

        Assert.Equal(3, models.Count);
        Assert.Contains("gpt-4o", models);
    }

    // ── Unknown provider ─────────────────────────────────────────────────────

    [Fact]
    public async Task FetchModelsAsync_UnknownProvider_ThrowsNotSupported()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}");
        var service = BuildService(handler);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => service.FetchModelsAsync("bigcorp-ai", "key"));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static LlmModelFetcherService BuildService(CapturingHandler handler)
    {
        var factory = new SingleHandlerHttpClientFactory(handler);
        return new LlmModelFetcherService(factory);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public HttpRequestMessage? LastRequest { get; private set; }

        public CapturingHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body   = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class SingleHandlerHttpClientFactory : IHttpClientFactory
    {
        private readonly CapturingHandler _handler;
        public SingleHandlerHttpClientFactory(CapturingHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }
}
