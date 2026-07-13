// The bring-your-own-AI provider seam (spec-019): a factory per AI provider that builds a
// provider-neutral IChatClient, and a registry that resolves the active one from configuration.
using DBAIAzure.Core.Models.Ai;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// A factory for one AI provider (spec-019 "bring your own AI"). Each provider id (e.g. "anthropic")
/// has exactly one provider that builds a provider-neutral <see cref="IChatClient"/> from an
/// <see cref="AiProviderConfig"/>. Adding a provider is a new implementation plus configuration — never
/// a change to the pipelines, steps, or orchestration engine.
/// </summary>
public interface IChatClientProvider
{
    /// <summary>The provider id this factory serves (matched case-insensitively), e.g. "anthropic".</summary>
    string ProviderId { get; }

    /// <summary>Builds an <see cref="IChatClient"/> for the given provider/model/key configuration.</summary>
    IChatClient Create(AiProviderConfig config);
}

/// <summary>
/// Resolves the active provider's <see cref="IChatClient"/> from configuration. Fails loud — naming the
/// provider — when the configured provider is unknown or cannot be initialised, and never silently
/// falls back to a different provider (spec-019 FR-009d). One active provider applies per deployment
/// instance (spec-019 Clarifications Q4).
/// </summary>
public interface IChatClientProviderRegistry
{
    /// <summary>Returns the provider registered for <paramref name="providerId"/>, or throws if none.</summary>
    IChatClientProvider Get(string providerId);

    /// <summary>Builds an <see cref="IChatClient"/> for the supplied active configuration.</summary>
    IChatClient CreateActive(AiProviderConfig config);
}
