// Unit tests for the hot-reloading chat client + provider registry (spec-019 T009/T007): the client
// re-resolves the active provider/model per call and rebuilds only on change; the registry fails loud
// (naming the provider) on an unknown provider with no silent fallback.
using DBAIAzure.Connectors.Ai;
using DBAIAzure.Core.Exceptions;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.Ai;
using Microsoft.Extensions.AI;
using Xunit;

namespace DBAIAzure.Tests.Ai;

/// <summary>
/// Verifies hot-reload behaviour with hand-rolled fakes (no network): delegation to the active
/// provider's client, single build when configuration is unchanged, rebuild when it changes, and a
/// loud, provider-named failure for an unknown provider.
/// </summary>
public sealed class HotReloadChatClientTests
{
    // A provider that hands back a distinct recording client per Create call so the test can observe
    // both delegation and rebuilds.
    private sealed class FakeProvider(string providerId) : IChatClientProvider
    {
        public string ProviderId { get; } = providerId;
        public int CreateCount { get; private set; }

        public IChatClient Create(AiProviderConfig config)
        {
            CreateCount++;
            return new RecordingChatClient($"{config.ProviderId}:{config.Model}");
        }
    }

    private sealed class RecordingChatClient(string tag) : IChatClient
    {
        private readonly string _tag = tag;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _tag)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private static ChatClientProviderRegistry Registry(params IChatClientProvider[] providers) => new(providers);

    private static ChatMessage[] Prompt(string text) => new[] { new ChatMessage(ChatRole.User, text) };

    [Fact]
    public async Task Delegates_ToActiveProviderClient()
    {
        var provider = new FakeProvider("anthropic");
        var config = new AiProviderConfig("anthropic", "claude-opus-4-8", "k");
        using var hot = new HotReloadChatClient(Registry(provider), () => config);

        var response = await hot.GetResponseAsync(Prompt("hi"));

        Assert.Equal("anthropic:claude-opus-4-8", response.Text);
        Assert.Equal(1, provider.CreateCount);
    }

    [Fact]
    public async Task ReusesClient_WhenConfigUnchanged()
    {
        var provider = new FakeProvider("anthropic");
        var config = new AiProviderConfig("anthropic", "claude-opus-4-8", "k");
        using var hot = new HotReloadChatClient(Registry(provider), () => config);

        await hot.GetResponseAsync(Prompt("a"));
        await hot.GetResponseAsync(Prompt("b"));

        Assert.Equal(1, provider.CreateCount); // built once, reused
    }

    [Fact]
    public async Task RebuildsClient_WhenConfigChanges()
    {
        var provider = new FakeProvider("anthropic");
        var current = new AiProviderConfig("anthropic", "claude-opus-4-8", "k");
        using var hot = new HotReloadChatClient(Registry(provider), () => current);

        await hot.GetResponseAsync(Prompt("a"));
        current = current with { Model = "claude-haiku-4-5" };
        var response = await hot.GetResponseAsync(Prompt("b"));

        Assert.Equal(2, provider.CreateCount);
        Assert.Equal("anthropic:claude-haiku-4-5", response.Text);
    }

    [Fact]
    public void Registry_UnknownProvider_FailsLoud()
    {
        var registry = Registry(new FakeProvider("anthropic"));
        var config = new AiProviderConfig("openai", "gpt", "k");

        var exception = Assert.Throws<AiProviderException>(() => registry.CreateActive(config));

        Assert.Equal("openai", exception.ProviderId);
        Assert.Contains("openai", exception.Message);
    }
}
