// Unit tests for the DoR document source seam (spec-021 T020): inline, url fetch + cache, and the failure
// fallback (cached copy vs DorDocumentUnavailableException). No live network — a fake HTTP handler is used.
using System.Net;
using System.Text;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;
using DBAIAzure.Web.Services.Dor;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class DorDocumentSourceTests
{
    [Fact]
    public async Task Inline_ReturnsConfiguredMarkdown()
    {
        var source = Build(new DorDocConfig { SourceType = "inline", InlineMarkdown = "# DoR\n- Has AC" }, out _);

        var doc = await source.LoadAsync();

        Assert.Equal("inline", doc.SourceType);
        Assert.Contains("Has AC", doc.Text);
    }

    [Fact]
    public async Task Inline_Empty_Throws()
    {
        var source = Build(new DorDocConfig { SourceType = "inline", InlineMarkdown = "" }, out _);

        await Assert.ThrowsAsync<DorDocumentUnavailableException>(() => source.LoadAsync());
    }

    [Fact]
    public async Task Url_FetchesOnce_ThenServesFromCache()
    {
        var handler = new SequencedHandler(() => Ok("URL DOR"));
        var source = Build(new DorDocConfig { SourceType = "url", SourceUri = "https://x/dor", CacheTtlMinutes = 15 }, handler);

        var first = await source.LoadAsync();
        var second = await source.LoadAsync();

        Assert.Equal("URL DOR", first.Text);
        Assert.Equal("URL DOR", second.Text);
        Assert.Equal(1, handler.CallCount); // second load served from cache
    }

    [Fact]
    public async Task Url_FetchFailsButCacheExists_ServesCachedCopy()
    {
        // ttl=0 → always attempt a fetch; the second attempt fails and must fall back to the cached copy.
        var handler = new SequencedHandler(() => Ok("CACHED DOR"), () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var source = Build(new DorDocConfig { SourceType = "url", SourceUri = "https://x/dor", CacheTtlMinutes = 0 }, handler);

        var first = await source.LoadAsync();
        var second = await source.LoadAsync();

        Assert.Equal("CACHED DOR", first.Text);
        Assert.Equal("CACHED DOR", second.Text); // fell back to cache on the failed second fetch
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Url_FetchFailsWithNoCache_Throws()
    {
        var handler = new SequencedHandler(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var source = Build(new DorDocConfig { SourceType = "url", SourceUri = "https://x/dor", CacheTtlMinutes = 15 }, handler);

        await Assert.ThrowsAsync<DorDocumentUnavailableException>(() => source.LoadAsync());
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static DorDocumentSource Build(DorDocConfig dor, out SequencedHandler handler)
    {
        handler = new SequencedHandler(() => Ok(""));
        return Build(dor, handler);
    }

    private static DorDocumentSource Build(DorDocConfig dor, SequencedHandler handler) =>
        new(new StubConfigResolver(dor), new StubHttpClientFactory(handler), NullLogger<DorDocumentSource>.Instance);

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/markdown") };

    private sealed class StubConfigResolver : IDorConfigResolver
    {
        private readonly DorDocConfig _dor;
        public StubConfigResolver(DorDocConfig dor) => _dor = dor;
        public Task<DorWorkflowConfig> ResolveActiveAsync(CancellationToken ct = default) =>
            Task.FromResult(new DorWorkflowConfig { Dor = _dor, IsConfigured = true });
        public Task<DorWorkflowSecrets> ResolveSecretsAsync(CancellationToken ct = default) =>
            Task.FromResult(new DorWorkflowSecrets(null, null, null, null));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage>[] _responses;
        public int CallCount { get; private set; }
        public SequencedHandler(params Func<HttpResponseMessage>[] responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var factory = _responses[Math.Min(CallCount, _responses.Length - 1)];
            CallCount++;
            return Task.FromResult(factory());
        }
    }
}
