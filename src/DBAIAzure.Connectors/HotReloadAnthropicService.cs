// Hot-reloading LLM service for the design-time features (Workflow Builder AI assistant, Node
// Realization). The execution paths already re-read the LLM key from the DB on every run; these
// design-time singletons used to capture the key from startup config, so with the key intentionally
// unseeded they would never see the key a visitor enters in the UI. This wrapper closes that gap by
// resolving the key+model from the LLM connector's stored config (DB-first, config fallback) on each
// call — so the single visitor-supplied key powers every LLM feature (research Decision 7;
// FR-003 / FR-004 / SC-006).
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DBAIAzure.Tests")]

namespace DBAIAzure.Connectors;

/// <summary>
/// An <see cref="IChatCompletionService"/> + <see cref="IStructuredCompletionService"/> that resolves
/// the Anthropic API key and model from the stored LLM connector configuration on each call, falling
/// back to the supplied configuration values when the connector is not yet configured. The underlying
/// <see cref="AnthropicChatCompletionService"/> is rebuilt only when the resolved key/model changes,
/// so repeated calls reuse a single <see cref="System.Net.Http.HttpClient"/>.
/// </summary>
public sealed class HotReloadAnthropicService : IChatCompletionService, IStructuredCompletionService, IDisposable
{
    private readonly IConnectorConfigRepository _configRepository;
    private readonly string _fallbackApiKey;
    private readonly string _fallbackModel;
    private readonly object _gate = new();

    private string? _currentApiKey;
    private string? _currentModel;
    private AnthropicChatCompletionService? _current;

    /// <summary>Creates the hot-reloading service with the configuration-derived fallback key and model.</summary>
    public HotReloadAnthropicService(IConnectorConfigRepository configRepository, string fallbackApiKey, string fallbackModel)
    {
        _configRepository = configRepository;
        _fallbackApiKey = fallbackApiKey;
        _fallbackModel = fallbackModel;
    }

    /// <summary>SK attribute bag — advertises the provider; model id varies per call so it is not pinned here.</summary>
    public IReadOnlyDictionary<string, object?> Attributes { get; } =
        new Dictionary<string, object?> { { "provider", "anthropic" } };

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var service = await ResolveServiceAsync(cancellationToken);
        return await service.GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var service = await ResolveServiceAsync(cancellationToken);
        await foreach (var chunk in service
            .GetStreamingChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <inheritdoc />
    public async Task<T> GetStructuredAsync<T>(
        string systemPrompt,
        string userMessage,
        string toolName,
        string toolDescription,
        string inputSchemaJson,
        CancellationToken cancellationToken = default)
    {
        var service = await ResolveServiceAsync(cancellationToken);
        return await service.GetStructuredAsync<T>(systemPrompt, userMessage, toolName, toolDescription, inputSchemaJson, cancellationToken);
    }

    /// <summary>Resolves credentials, then returns a cached or freshly built backing service for them.</summary>
    private async Task<AnthropicChatCompletionService> ResolveServiceAsync(CancellationToken cancellationToken)
    {
        var (apiKey, model) = await ResolveCredentialsAsync(cancellationToken);
        lock (_gate)
        {
            if (_current is null || apiKey != _currentApiKey || model != _currentModel)
            {
                _current?.Dispose();
                _current = new AnthropicChatCompletionService(apiKey, model);
                _currentApiKey = apiKey;
                _currentModel = model;
            }
            return _current;
        }
    }

    /// <summary>
    /// Resolves the effective (apiKey, model) — preferring the values stored on the LLM connector and
    /// falling back to the configuration values when the connector is not configured or unreadable.
    /// Internal so the resolution rule can be unit-tested without a network call.
    /// </summary>
    internal async Task<(string apiKey, string model)> ResolveCredentialsAsync(CancellationToken cancellationToken)
    {
        var apiKey = _fallbackApiKey;
        var model = _fallbackModel;
        try
        {
            var config = await _configRepository.GetAsync(ConnectorType.LLM, cancellationToken);
            if (config?.NonSecretConfig is { } nonSecretJson)
            {
                using var document = JsonDocument.Parse(nonSecretJson);
                if (document.RootElement.TryGetProperty("modelName", out var modelProperty) &&
                    modelProperty.GetString() is { Length: > 0 } resolvedModel)
                {
                    model = resolvedModel;
                }
            }

            var secretsJson = await _configRepository.GetDecryptedSecretsAsync(ConnectorType.LLM, cancellationToken);
            if (secretsJson is not null)
            {
                using var document = JsonDocument.Parse(secretsJson);
                if (document.RootElement.TryGetProperty("apiKey", out var keyProperty) &&
                    keyProperty.GetString() is { Length: > 0 } resolvedKey)
                {
                    apiKey = resolvedKey;
                }
            }
        }
        catch
        {
            // DB unavailable or no LLM config yet — fall back to the configuration values.
        }
        return (apiKey, model);
    }

    /// <summary>Disposes the cached backing service, if any.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _current?.Dispose();
            _current = null;
        }
    }
}
