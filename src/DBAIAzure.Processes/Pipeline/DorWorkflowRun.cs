// In-memory handle for a live DoR workflow run (spec-021 US2/US3): coordinates the out-of-band resumption (a
// human reply, an escalation, or a manual exit) and signals suspension/completion so the orchestrator's
// background drive loop and callers can rendezvous.
using DBAIAzure.Core.Models.DorWorkflow;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// Tracks one running DoR workflow. The drive loop, at each human gate, arms a fresh resumption slot, releases
/// the suspension signal, and awaits the resumption; a resumption arrives out-of-band via
/// <see cref="Provide"/> (from the chat reply pump, the SLA sweeper, or a caller). Callers await
/// <see cref="WaitSuspendedAsync"/> once per suspension (a counting semaphore, so multi-turn conversations never
/// race) and <see cref="Completion"/> for the terminal state.
/// </summary>
public sealed class DorWorkflowRun
{
    private readonly SemaphoreSlim _suspended = new(0);
    private readonly TaskCompletionSource<bool> _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<DorResumption> _resumption = NewResumption();

    public DorWorkflowRun(string runId) => RunId = runId;

    /// <summary>The run id (also the MAF session/checkpoint key).</summary>
    public string RunId { get; }

    /// <summary>The current state (updated by the drive loop as the run progresses).</summary>
    public DorState State { get; set; } = DorState.Created;

    /// <summary>Completes when the run reaches a terminal state.</summary>
    public Task Completion => _completed.Task;

    /// <summary>Awaits the next suspension (one permit released per human gate).</summary>
    public Task WaitSuspendedAsync(CancellationToken ct = default) => _suspended.WaitAsync(ct);

    /// <summary>Awaited by the drive loop at a human gate — completes when a resumption is provided.</summary>
    public Task<DorResumption> WaitForResumptionAsync() => _resumption.Task;

    /// <summary>Arms a fresh resumption slot for the upcoming gate.</summary>
    public void ArmResumption() => _resumption = NewResumption();

    /// <summary>Releases the suspension signal so a waiter learns the run is ready for a resumption.</summary>
    public void SignalSuspended() => _suspended.Release();

    /// <summary>Supplies the resumption, unblocking the waiting drive loop.</summary>
    public void Provide(DorResumption resumption) => _resumption.TrySetResult(resumption);

    /// <summary>Marks the run complete.</summary>
    public void Complete() => _completed.TrySetResult(true);

    private static TaskCompletionSource<DorResumption> NewResumption() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
