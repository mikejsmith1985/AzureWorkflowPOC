// Unit tests for the default (Anthropic/Claude) AI provider (spec-019 T008): it advertises the
// "anthropic" id and builds a provider-neutral IChatClient from configuration, failing on a missing key.
using DBAIAzure.Connectors.Ai;
using DBAIAzure.Core.Models.Ai;
using Microsoft.Extensions.AI;
using Xunit;

namespace DBAIAzure.Tests.Ai;

/// <summary>
/// Verifies the Anthropic provider factory: correct provider id, a non-null <see cref="IChatClient"/>
/// for a valid configuration, and a guard-clause failure when the API key is absent. No network call is
/// made — only client construction is exercised.
/// </summary>
public sealed class AnthropicChatClientProviderTests
{
    [Fact]
    public void ProviderId_IsAnthropic()
    {
        var provider = new AnthropicChatClientProvider();
        Assert.Equal("anthropic", provider.ProviderId);
    }

    [Fact]
    public void Create_WithValidConfig_ReturnsChatClient()
    {
        var provider = new AnthropicChatClientProvider();
        var config = new AiProviderConfig("anthropic", "claude-opus-4-8", "sk-ant-test-key");

        IChatClient client = provider.Create(config);

        Assert.NotNull(client);
    }

    [Fact]
    public void Create_WithMissingKey_Throws()
    {
        var provider = new AnthropicChatClientProvider();
        var config = new AiProviderConfig("anthropic", "claude-opus-4-8", "");

        Assert.Throws<ArgumentException>(() => provider.Create(config));
    }
}
