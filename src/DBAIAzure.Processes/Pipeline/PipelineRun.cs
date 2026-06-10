using DBAIAzure.Core.Models;
using System.Collections.Concurrent;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// Mutable state container for a single pipeline execution.
/// Background task and UI thread access it concurrently — all mutations go through
/// the dedicated setter methods to ensure consistent state transitions.
/// </summary>
public sealed class PipelineRun
{
    private TaskCompletionSource<string>? _hitlInputSource;

    public string RunId { get; }
    public TicketState InitialTicket { get; }
    public TicketState? CurrentTicket { get; private set; }
    public PipelineRunStatus Status { get; private set; } = PipelineRunStatus.Running;
    public ConcurrentQueue<PipelineEvent> Events { get; } = new();
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public string? ErrorMessage { get; private set; }

    /// <summary>Questions from GapAnalysisStep shown to the PO when status is AwaitingHuman.</summary>
    public IReadOnlyList<string> HitlQuestions { get; private set; } = [];

    public PipelineRun(string runId, TicketState initialTicket)
    {
        RunId = runId;
        InitialTicket = initialTicket;
        CurrentTicket = initialTicket;
    }

    public void AddEvent(PipelineEvent pipelineEvent) => Events.Enqueue(pipelineEvent);

    public void SetRunning()
    {
        Status = PipelineRunStatus.Running;
    }

    /// <summary>
    /// Transitions to AwaitingHuman and exposes a Task the background loop awaits.
    /// The UI calls ProvideHitlInput to unblock it.
    /// </summary>
    public void SetAwaitingHuman(TicketState pausedTicket)
    {
        CurrentTicket = pausedTicket;
        HitlQuestions = pausedTicket.ClarifyingQuestions;
        Status = PipelineRunStatus.AwaitingHuman;
        _hitlInputSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Terminal state — Complete when story points assigned, Blocked when max rounds hit.
    /// </summary>
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
    // VSTHRD003: The TCS uses RunContinuationsAsynchronously, so continuations run on a
    // thread-pool thread — not inline — which is the safe pattern for cross-context awaiting.
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
