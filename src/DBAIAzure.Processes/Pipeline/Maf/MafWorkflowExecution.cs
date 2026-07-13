// Runs a MAF workflow for the orchestrators, driving it segment-by-segment across human-in-the-loop
// suspensions (spec-019 T022/US2). Progress events are emitted by the executors themselves (via their
// injected progress sink); this helper captures the terminal result and the pending human request.
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Pipeline.Maf;

/// <summary>
/// The result of driving a workflow up to its next stop: the terminal output (when the run completed) and
/// the pending human request (when it suspended at a <see cref="RequestPort"/>).
/// </summary>
/// <typeparam name="TOutput">The state type a terminal executor yields (e.g. the final ticket).</typeparam>
/// <param name="Output">The terminal output, or null when the run suspended or produced none.</param>
/// <param name="PendingRequest">The pending request when suspended (carries the paused state), else null.</param>
public sealed record MafSegmentOutcome<TOutput>(TOutput? Output, RequestInfoEvent? PendingRequest)
{
    /// <summary>True when the run paused awaiting a human response.</summary>
    public bool Suspended => PendingRequest is not null;
}

/// <summary>
/// A live MAF workflow run an orchestrator drives across human-in-the-loop suspensions: start it, drive to
/// the next suspension or completion, respond to the pending request, and drive again. The session keeps
/// the underlying <see cref="StreamingRun"/> alive between segments (in-process resume; durable
/// checkpoint/restore is layered on top in US2 T030–T032).
/// </summary>
public sealed class MafWorkflowSession<TOutput>
{
    // Active execution between stops must reach a terminal or suspended state within this bound; the human
    // wait happens between segments, not inside DriveAsync, so this caps only live execution.
    private static readonly TimeSpan SegmentTimeout = TimeSpan.FromMinutes(3);

    private readonly StreamingRun _run;

    private MafWorkflowSession(StreamingRun run) => _run = run;

    /// <summary>The most recent checkpoint written for this run, or null when checkpointing is disabled.</summary>
    public CheckpointInfo? LastCheckpoint => _run.IsCheckpointingEnabled ? _run.LastCheckpoint : null;

    /// <summary>
    /// Starts the workflow under the run's id (the session id, so resume/checkpoints key on it). When a
    /// <paramref name="checkpointManager"/> is supplied, the run is checkpointed at every super-step so it
    /// can be resumed after a restart (spec-019 T030–T032); without one it runs in-process only.
    /// </summary>
    public static async Task<MafWorkflowSession<TOutput>> StartAsync<TInput>(
        Workflow workflow, TInput input, string runId, CheckpointManager? checkpointManager, CancellationToken cancellationToken)
        where TInput : notnull
    {
        var run = checkpointManager is null
            ? await InProcessExecution.RunStreamingAsync(workflow, input, runId, cancellationToken)
            : await InProcessExecution.RunStreamingAsync(workflow, input, checkpointManager, runId, cancellationToken);
        return new MafWorkflowSession<TOutput>(run);
    }

    /// <summary>
    /// Resumes a checkpointed run from <paramref name="checkpoint"/> using a fresh <paramref name="workflow"/>
    /// instance — the restart-recovery entry point (spec-019 T032). The resumed run re-emits any outstanding
    /// human request, which the orchestrator then drives as usual.
    /// </summary>
    public static async Task<MafWorkflowSession<TOutput>> ResumeAsync(
        Workflow workflow, CheckpointInfo checkpoint, CheckpointManager checkpointManager, CancellationToken cancellationToken)
    {
        var run = await InProcessExecution.ResumeStreamingAsync(workflow, checkpoint, checkpointManager, cancellationToken);
        return new MafWorkflowSession<TOutput>(run);
    }

    /// <summary>
    /// Drives the run until it completes or suspends at a human-in-the-loop gate, returning the terminal
    /// output or the pending request. Breaks on <see cref="RequestInfoEvent"/> because
    /// <c>WatchStreamAsync</c> does not reliably complete at <see cref="RunStatus.PendingRequests"/> on a
    /// background thread (as the orchestrators run via <c>Task.Run</c>); the completed path ends on its own.
    /// </summary>
    public async Task<MafSegmentOutcome<TOutput>> DriveAsync(CancellationToken cancellationToken)
    {
        using var segmentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        segmentCts.CancelAfter(SegmentTimeout);
        var segmentToken = segmentCts.Token;

        TOutput? output = default;
        RequestInfoEvent? pendingRequest = null;

        await foreach (var workflowEvent in _run.WatchStreamAsync(segmentToken))
        {
            switch (workflowEvent)
            {
                case WorkflowOutputEvent outputEvent when outputEvent.Data is TOutput terminal:
                    output = terminal;
                    break;
                case RequestInfoEvent request:
                    pendingRequest = request; // the run suspended for a human response
                    break;
                case ExecutorFailedEvent failure:
                    // Surface an executor failure rather than masking it as a silent, output-less run.
                    throw new InvalidOperationException($"Executor '{failure.ExecutorId}' failed: {failure.Data}");
                case WorkflowErrorEvent error:
                    throw new InvalidOperationException($"Workflow error: {error.Data}");
            }

            if (pendingRequest is not null)
            {
                break;
            }
        }

        return new MafSegmentOutcome<TOutput>(output, pendingRequest);
    }

    /// <summary>Resolves a pending request with the host's response, unblocking the run for the next segment.</summary>
    public async Task RespondAsync(ExternalRequest request, object responseData, CancellationToken cancellationToken)
    {
        await _run.SendResponseAsync(request.CreateResponse(responseData));
    }
}

/// <summary>Convenience for a single drive (used where resume is not yet wired) — starts and drives once.</summary>
public static class MafWorkflowExecution
{
    /// <summary>Starts <paramref name="workflow"/> and drives it to its first completion or suspension.</summary>
    public static async Task<MafSegmentOutcome<TOutput>> RunAsync<TInput, TOutput>(
        Workflow workflow, TInput input, string runId, CancellationToken cancellationToken,
        CheckpointManager? checkpointManager = null)
        where TInput : notnull
    {
        var session = await MafWorkflowSession<TOutput>.StartAsync(workflow, input, runId, checkpointManager, cancellationToken);
        return await session.DriveAsync(cancellationToken);
    }
}
