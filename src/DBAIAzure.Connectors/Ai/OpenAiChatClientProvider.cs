// The alternative built-in AI provider (spec-019 T043 "bring your own AI"): reaches any OpenAI-compatible
// endpoint (OpenAI, Azure OpenAI, Ollama, LM Studio) through the GA OpenAI SDK, exposed as a provider-neutral
// Microsoft.Extensions.AI IChatClient. Registering it required no change to any pipeline or executor.
using System.ClientModel;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.Ai;
using Microsoft.Extensions.AI;
using OpenAI;

namespace DBAIAzure.Connectors.Ai;

/// <summary>
/// Builds an <see cref="IChatClient"/> backed by an OpenAI-compatible chat endpoint via the official OpenAI
/// SDK's <c>AsIChatClient</c> extension. The optional <see cref="AiProviderConfig.Endpoint"/> points at any
/// compatible service (Azure OpenAI, a local Ollama/LM Studio server, …); when null the OpenAI default is
/// used. This exists to prove the provider seam is truly provider-neutral — the pipelines never reference
/// this type — and is selected purely by configuration (<c>AI:Provider = "openai"</c>).
/// </summary>
public sealed class OpenAiChatClientProvider : IChatClientProvider
{
    /// <inheritdoc />
    public string ProviderId => AiProviderConfig.OpenAiProviderId;

    /// <inheritdoc />
    public IChatClient Create(AiProviderConfig config)
    {
        // A missing key is a configuration error; the registry rethrows it as a provider-named failure.
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new ArgumentException("OpenAI API key is not configured.", nameof(config));
        }

        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(config.Endpoint))
        {
            options.Endpoint = new Uri(config.Endpoint);
        }

        var client = new OpenAIClient(new ApiKeyCredential(config.ApiKey), options);
        return client.GetChatClient(config.Model).AsIChatClient();
    }
}
