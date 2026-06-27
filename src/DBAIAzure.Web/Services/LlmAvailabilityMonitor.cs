// Monitors LLM reachability with periodic probes so the UI can react to outages
// without requiring a page reload — wires directly to ILlmAvailabilityMonitor.

using DBAIAzure.Core.Interfaces;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Probes the configured LLM endpoint every 30 seconds using a minimal chat request.
/// Fires <see cref="StateChanged"/> on every availability transition so Blazor components
/// can show or dismiss the "assistant unavailable" banner without polling.
/// </summary>
public sealed class LlmAvailabilityMonitor : ILlmAvailabilityMonitor, IDisposable
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(30);

    private readonly IChatCompletionService _chatService;
    private readonly ILogger<LlmAvailabilityMonitor> _logger;

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private volatile bool _isAvailable = true;

    /// <inheritdoc/>
    public bool IsAvailable => _isAvailable;

    /// <inheritdoc/>
    public event Action<bool>? StateChanged;

    /// <summary>
    /// Initialises the monitor with the Semantic Kernel chat completion service and
    /// a logger for availability transition events.
    /// </summary>
    public LlmAvailabilityMonitor(
        IChatCompletionService chatService,
        ILogger<LlmAvailabilityMonitor> logger)
    {
        _chatService = chatService;
        _logger      = logger;
    }

    /// <inheritdoc/>
    public Task StartMonitoringAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitorTask = RunProbeLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopMonitoringAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
            await _cts.CancelAsync().ConfigureAwait(false);

        if (_monitorTask is not null)
        {
            try
            {
#pragma warning disable VSTHRD003  // _monitorTask is owned by this class — safe to await from any context
                await _monitorTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected during graceful shutdown — not an error.
            }
        }
    }

    private async Task RunProbeLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var wasAvailable = _isAvailable;
            var isNowAvailable = await ProbeAsync(token).ConfigureAwait(false);

            if (isNowAvailable != wasAvailable)
            {
                _isAvailable = isNowAvailable;
                _logger.LogInformation(
                    "LLM availability changed: {State}",
                    isNowAvailable ? "available" : "unavailable");
                StateChanged?.Invoke(isNowAvailable);
            }

            try
            {
                await Task.Delay(ProbeInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<bool> ProbeAsync(CancellationToken token)
    {
        try
        {
            // Minimal single-turn probe — does not affect conversation history.
            var probeHistory = new ChatHistory();
            probeHistory.AddUserMessage("ping");
            var result = await _chatService
                .GetChatMessageContentAsync(probeHistory, cancellationToken: token)
                .ConfigureAwait(false);
            return result is not null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "LLM probe failed; treating endpoint as unavailable.");
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
