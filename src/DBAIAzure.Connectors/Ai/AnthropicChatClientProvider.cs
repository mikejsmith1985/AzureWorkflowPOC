// The default AI provider (spec-019 "bring your own AI"): reaches Anthropic/Claude through the official
// Anthropic SDK and exposes it as a provider-neutral Microsoft.Extensions.AI IChatClient.
using Anthropic;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.Ai;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Connectors.Ai;

/// <summary>
/// Builds an <see cref="IChatClient"/> backed by Anthropic/Claude via the official Anthropic SDK's
/// <c>AsIChatClient</c> extension. Uses the generally-available SDK deliberately — not the prerelease
/// <c>Microsoft.Agents.AI.Anthropic</c> connector — so the execution path stays on GA packages
/// (spec-019 D5 / FR-003). This is the product's default provider; others are added by registering more
/// <see cref="IChatClientProvider"/> implementations, with no change to pipelines or steps.
/// </summary>
public sealed class AnthropicChatClientProvider : IChatClientProvider
{
    /// <inheritdoc />
    public string ProviderId => AiProviderConfig.DefaultProviderId;

    /// <inheritdoc />
    public IChatClient Create(AiProviderConfig config)
    {
        // A missing key is a configuration error; the registry rethrows it as a provider-named failure.
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new ArgumentException("Anthropic API key is not configured.", nameof(config));
        }

        var anthropicClient = new AnthropicClient(new Anthropic.Core.ClientOptions { ApiKey = config.ApiKey });
        return anthropicClient.AsIChatClient(
            defaultModelId: config.Model,
            defaultMaxOutputTokens: config.MaxOutputTokens);
    }
}
