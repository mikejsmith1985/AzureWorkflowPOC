using DBAIAzure.Core.Models;
using System.Collections.Concurrent;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// Live mutable state container for a single pipeline execution.
/// Background task and Blazor UI thread access it concurrently — all mutations
/// go through the dedicated setter methods to keep state transitions consistent.
/// </summary>
public sealed class PipelineRun
{
    private TaskCompletionSource<string>? _hitlInputSource;
    private readonly List<StepStateSnapshot> _snapshots = [];
    private readonly object _snapshotLock = new();

    public string RunId { get; }
    public TicketState InitialTicket { get; }
    public TicketState? CurrentTicket { get; private set; }
    public PipelineRunStatus Status { get; private set; } = PipelineRunStatus.Running;
    public ConcurrentQueue<PipelineEvent> Events { get; } = new();
    public ConcurrentQueue<StreamToken>   TokenStream { get; } = new();
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public string? ErrorMessage { get; private set; }

    /// <summary>Clarifying questions from GapAnalysisStep shown to the PO.</summary>
    public IReadOnlyList<string> HitlQuestions { get; private set; } = [];

    /// <summary>TicketState snapshots captured before/after each LLM step — powers state inspector.</summary>
    public IReadOnlyList<StepStateSnapshot> Snapshots
    {
        get { lock (_snapshotLock) { return [.. _snapshots]; } }
    }

    public PipelineRun(string runId, TicketState initialTicket)
    {
        RunId = runId;
        InitialTicket = initialTicket;
        CurrentTicket = initialTicket;
    }

    public void AddEvent(PipelineEvent pipelineEvent) => Events.Enqueue(pipelineEvent);
    public void AddToken(StreamToken token) => TokenStream.Enqueue(token);

    public void AddSnapshot(StepStateSnapshot snapshot)
    {
        lock (_snapshotLock) { _snapshots.Add(snapshot); }
    }

    public void SetRunning()
    {
        Status = PipelineRunStatus.Running;
        CurrentTicket = CurrentTicket; // no-op; signals re-render
    }

    /// <summary>
    /// Transitions to AwaitingHuman; exposes a Task the background loop awaits.
    /// The UI calls ProvideHitlInput to complete that Task and resume execution.
    /// </summary>
    public void SetAwaitingHuman(TicketState pausedTicket)
    {
        CurrentTicket = pausedTicket;
        HitlQuestions = pausedTicket.ClarifyingQuestions;
        Status = PipelineRunStatus.AwaitingHuman;
        _hitlInputSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Terminal state — Complete when story points assigned, Blocked when max rounds hit.</summary>
    public void SetComplete(TicketState finalTicket)
    {
        CurrentTicket = finalTicket;
        Status = finalTicket.StoryPoints.HasValue ? PipelineRunStatus.Complete : PipelineRunStatus.Blocked;
    }

    public void SetFailed(string errorMessage)
    {
        ErrorMessage = errorMessage;
        Status = PipelineRunStatus.Failed;
    }

    /// <summary>Awaited by the background loop while the UI collects PO input.</summary>
    // VSTHRD003: TCS uses RunContinuationsAsynchronously — continuations run on a thread-pool
    // thread, not inline, which is the safe pattern for cross-context awaiting.
#pragma warning disable VSTHRD003
    public Task<string> WaitForHitlInputAsync()
    {
        if (_hitlInputSource is null)
            throw new InvalidOperationException("Run is not in AwaitingHuman state.");
        return _hitlInputSource.Task;
    }
#pragma warning restore VSTHRD003

    /// <summary>Called by the UI once the PO has submitted their answer.</summary>
    public void ProvideHitlInput(string answer) =>
        _hitlInputSource?.TrySetResult(answer);
}
