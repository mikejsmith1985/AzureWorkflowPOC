// Resolves the active AI provider by id and builds its IChatClient (spec-019 "bring your own AI").
// An unknown or failing provider is surfaced loudly, named — never silently swapped for another.
using DBAIAzure.Core.Exceptions;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.Ai;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Connectors.Ai;

/// <summary>
/// Registry of the available <see cref="IChatClientProvider"/> factories, keyed by provider id
/// (case-insensitive). A single active provider applies per deployment instance (spec-019
/// Clarifications Q4). An unknown provider id, or a provider that fails to initialise, throws an
/// <see cref="AiProviderException"/> naming the provider — the system never silently falls back
/// (spec-019 FR-009d).
/// </summary>
public sealed class ChatClientProviderRegistry : IChatClientProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IChatClientProvider> _providersById;

    /// <summary>Builds the registry from all registered providers, keyed by their id.</summary>
    public ChatClientProviderRegistry(IEnumerable<IChatClientProvider> providers)
    {
        _providersById = providers.ToDictionary(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IChatClientProvider Get(string providerId)
    {
        if (!_providersById.TryGetValue(providerId, out var provider))
        {
            var known = string.Join(", ", _providersById.Keys);
            throw new AiProviderException(providerId,
                $"No AI provider is registered for '{providerId}'. Configured providers: {known}.");
        }

        return provider;
    }

    /// <inheritdoc />
    public IChatClient CreateActive(AiProviderConfig config)
    {
        var provider = Get(config.ProviderId); // throws AiProviderException (named) if unknown

        try
        {
            return provider.Create(config);
        }
        catch (Exception exception) when (exception is not AiProviderException)
        {
            // Wrap any initialisation failure so the caller sees which provider failed and why.
            throw new AiProviderException(config.ProviderId,
                $"Failed to initialise AI provider '{config.ProviderId}' (model '{config.Model}'): {exception.Message}",
                exception);
        }
    }
}
