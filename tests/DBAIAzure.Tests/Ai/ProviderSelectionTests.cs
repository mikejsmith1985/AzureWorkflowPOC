// BYO-AI provider selection (spec-019 T040/T043): the active provider is chosen by config id, both built-in
// providers resolve, and an unknown/misconfigured provider fails loud (named) with no silent fallback.
using DBAIAzure.Connectors.Ai;
using DBAIAzure.Core.Exceptions;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.Ai;
using Xunit;

namespace DBAIAzure.Tests.Ai;

/// <summary>
/// Verifies provider selection over the real registry with both built-in providers registered: Claude and
/// the OpenAI-compatible provider each resolve by their config id (client construction is offline, so no
/// network call), and an unknown provider throws a provider-named <see cref="AiProviderException"/>.
/// </summary>
public sealed class ProviderSelectionTests
{
    private static ChatClientProviderRegistry BuiltInRegistry() => new(new IChatClientProvider[]
    {
        new AnthropicChatClientProvider(),
        new OpenAiChatClientProvider(),
    });

    [Fact]
    public void OpenAiProvider_HasExpectedId_AndBuildsWithConfig()
    {
        var provider = new OpenAiChatClientProvider();
        Assert.Equal(AiProviderConfig.OpenAiProviderId, provider.ProviderId);

        var client = provider.Create(new AiProviderConfig("openai", "gpt-4o-mini", "sk-test", Endpoint: "https://example.invalid/v1"));
        Assert.NotNull(client); // constructed, not called — no network access
    }

    [Fact]
    public void OpenAiProvider_MissingKey_Throws()
    {
        var provider = new OpenAiChatClientProvider();
        Assert.Throws<ArgumentException>(() => provider.Create(new AiProviderConfig("openai", "gpt-4o-mini", "")));
    }

    [Fact]
    public void DefaultProvider_IsAnthropic_AndResolves()
    {
        var registry = BuiltInRegistry();
        var client = registry.CreateActive(new AiProviderConfig(AiProviderConfig.DefaultProviderId, "claude-opus-4-8", "key"));
        Assert.NotNull(client);
    }

    [Fact]
    public void OpenAiProvider_SelectedByConfigId_Resolves()
    {
        var registry = BuiltInRegistry();
        var client = registry.CreateActive(new AiProviderConfig(AiProviderConfig.OpenAiProviderId, "gpt-4o", "key"));
        Assert.NotNull(client);
    }

    [Fact]
    public void UnknownProvider_FailsLoud_NoSilentFallback()
    {
        var registry = BuiltInRegistry();

        var exception = Assert.Throws<AiProviderException>(
            () => registry.CreateActive(new AiProviderConfig("gemini", "model", "key")));

        Assert.Equal("gemini", exception.ProviderId);
        Assert.Contains("gemini", exception.Message);
    }
}
