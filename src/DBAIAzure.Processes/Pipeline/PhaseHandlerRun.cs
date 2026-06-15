// Live mutable container for a single phase-handler execution; the approval gate is a TaskCompletionSource.
using DBAIAzure.Core.Models;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// Live state for one phase-handler run. The background task and any UI/webhook caller access it
/// concurrently, so the approval decision flows through a <see cref="TaskCompletionSource{TResult}"/>
/// gate — exactly the pattern <c>PipelineRun</c> uses for HITL input.
/// </summary>
public sealed class PhaseHandlerRun
{
    private TaskCompletionSource<ApprovalDecision>? _approvalSource;
    private bool _hasPaused;
    private readonly object _gate = new();

    public string RunId { get; }

    /// <summary>The most recent state snapshot for this run.</summary>
    public PhaseHandlerState State { get; private set; }

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public PhaseHandlerRun(PhaseHandlerState initialState)
    {
        RunId = initialState.RunId;
        State = initialState;
    }

    /// <summary>Replaces the current state snapshot (called as the run advances).</summary>
    public void UpdateState(PhaseHandlerState state) => State = state;

    /// <summary>
    /// True once the run has actually reached the approval pause and a decision has not yet been
    /// provided. The gate is armed before the process runs (to avoid a callback race), so this also
    /// requires the pause to have been observed.
    /// </summary>
    public bool IsAwaitingApproval
    {
        get { lock (_gate) { return _hasPaused && _approvalSource is { Task.IsCompleted: false }; } }
    }

    /// <summary>Arms the approval gate; the background loop awaits the returned task.</summary>
    public void BeginAwaitingApproval()
    {
        lock (_gate)
        {
            _approvalSource = new TaskCompletionSource<ApprovalDecision>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Marks that the process has reached the approval pause (the gate is now live).</summary>
    public void MarkPaused()
    {
        lock (_gate) { _hasPaused = true; }
    }

    /// <summary>Awaited by the background loop while the reviewer decides.</summary>
    // VSTHRD003: the TCS uses RunContinuationsAsynchronously, so continuations run on the thread
    // pool rather than inline — the safe cross-context awaiting pattern (same as PipelineRun).
#pragma warning disable VSTHRD003
    public Task<ApprovalDecision> WaitForApprovalAsync()
    {
        lock (_gate)
        {
            if (_approvalSource is null)
                throw new InvalidOperationException("Run is not awaiting approval.");
            return _approvalSource.Task;
        }
    }
#pragma warning restore VSTHRD003

    /// <summary>
    /// Supplies the reviewer's decision and unblocks the background loop. Returns false if the run
    /// was not awaiting a decision (already decided or never paused) so the caller can map 404/409.
    /// </summary>
    public bool ProvideApproval(ApprovalDecision decision)
    {
        lock (_gate)
        {
            if (!_hasPaused || _approvalSource is null || _approvalSource.Task.IsCompleted) return false;
            return _approvalSource.TrySetResult(decision);
        }
    }
}
