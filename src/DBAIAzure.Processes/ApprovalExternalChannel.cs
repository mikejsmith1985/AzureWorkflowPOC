// Bridges the phase-handler's AwaitApproval proxy event out of the process so the orchestrator can pause.
// When ApprovalPauseStep emits AwaitApproval, the process routes it through the proxy step, which
// calls this channel. The orchestrator inspects WasPaused after RunToEndAsync returns, then restarts
// the process with ApprovalDecided once the reviewer responds. Mirrors HitlExternalChannel exactly.
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes;

/// <summary>
/// <see cref="IExternalKernelProcessMessageChannel"/> for the phase handler's HITL pause.
/// Captures the proxy message emitted on <see cref="PhaseHandlerEvents.AwaitApproval"/> so the
/// orchestrator knows the run is awaiting an approve/reject decision.
/// </summary>
public sealed class ApprovalExternalChannel : IExternalKernelProcessMessageChannel
{
    private KernelProcessProxyMessage? _pausedMessage;

    /// <summary>True once the process has emitted an AwaitApproval external event.</summary>
    public bool WasPaused => _pausedMessage is not null;

    /// <summary>The proxy message received from the process; null until WasPaused is true.</summary>
    public KernelProcessProxyMessage? PausedMessage => _pausedMessage;

    public ValueTask Initialize() => ValueTask.CompletedTask;

    public ValueTask Uninitialize() => ValueTask.CompletedTask;

    public Task EmitExternalEventAsync(string externalTopicEvent, KernelProcessProxyMessage message)
    {
        if (externalTopicEvent == PhaseHandlerEvents.AwaitApproval)
            _pausedMessage = message;
        return Task.CompletedTask;
    }
}
