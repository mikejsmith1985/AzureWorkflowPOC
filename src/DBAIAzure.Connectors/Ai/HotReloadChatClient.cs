// A chat client that re-resolves the active provider/model/key from configuration on each call and
// rebuilds the underlying client only when that resolved configuration changes (spec-019 hot-reload).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.Ai;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Connectors.Ai;

/// <summary>
/// An <see cref="IChatClient"/> that resolves the active AI configuration on every call and rebuilds the
/// underlying provider client only when the resolved provider/model/key changes (spec-019 FR-009). This
/// is what lets a visitor-supplied key/model take effect without a restart and makes provider selection
/// a pure configuration concern. It replaces the retired SK-based <c>HotReloadAnthropicService</c>.
///
/// Implemented as a direct <see cref="IChatClient"/> (rather than a fixed-inner delegating client)
/// because the inner client is intentionally swapped at runtime when configuration changes.
/// </summary>
public sealed class HotReloadChatClient : IChatClient
{
    private readonly IChatClientProviderRegistry _registry;
    private readonly Func<AiProviderConfig> _resolveActiveConfig;
    private readonly object _gate = new();

    private AiProviderConfig? _currentConfig;
    private IChatClient? _currentClient;

    /// <summary>Creates the hot-reloading client over a provider registry and a configuration resolver.</summary>
    public HotReloadChatClient(IChatClientProviderRegistry registry, Func<AiProviderConfig> resolveActiveConfig)
    {
        _registry = registry;
        _resolveActiveConfig = resolveActiveConfig;
    }

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => ResolveCurrentClient().GetResponseAsync(messages, options, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => ResolveCurrentClient().GetStreamingResponseAsync(messages, options, cancellationToken);

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        // Answer for this wrapper itself, else defer to the active underlying client.
        if (serviceKey is null && serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        return ResolveCurrentClient().GetService(serviceType, serviceKey);
    }

    /// <summary>Returns the current client, rebuilding it when the resolved configuration has changed.</summary>
    private IChatClient ResolveCurrentClient()
    {
        var config = _resolveActiveConfig();

        lock (_gate)
        {
            // AiProviderConfig is a value record, so equality covers provider, model, key, and token cap.
            var hasChanged = _currentClient is null || !config.Equals(_currentConfig);
            if (hasChanged)
            {
                _currentClient?.Dispose();
                _currentClient = _registry.CreateActive(config);
                _currentConfig = config;
            }

            return _currentClient;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _currentClient?.Dispose();
        }
    }
}
