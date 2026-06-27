// Defines the contract for monitoring LLM reachability so UI layers can react to outages without polling.

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Monitors LLM health with periodic probes. UI components subscribe to StateChanged to show/hide the
/// 'assistant unavailable' banner without a page reload.
/// </summary>
public interface ILlmAvailabilityMonitor
{
    /// <summary>
    /// Gets the current health state of the LLM endpoint.
    /// Returns <see langword="true"/> when the LLM is reachable and ready to accept requests;
    /// <see langword="false"/> when a probe has failed and the service should be treated as unavailable.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Fires on every availability state transition, carrying the new <see cref="IsAvailable"/> value.
    /// Subscribe to drive UI banners or circuit-breaker logic without polling this interface on a timer.
    /// </summary>
    event Action<bool>? StateChanged;

    /// <summary>
    /// Starts the background probe loop that periodically checks LLM reachability and raises
    /// <see cref="StateChanged"/> whenever the health state transitions.
    /// The loop runs until the supplied <paramref name="cancellationToken"/> is cancelled
    /// or <see cref="StopMonitoringAsync"/> is called.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the monitoring loop.</param>
    Task StartMonitoringAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the background probe loop gracefully, waiting for any in-flight probe to complete
    /// before returning. Call this during application shutdown to avoid orphaned background work.
    /// </summary>
    /// <param name="cancellationToken">Token used to abort the shutdown wait if it takes too long.</param>
    Task StopMonitoringAsync(CancellationToken cancellationToken);
}
