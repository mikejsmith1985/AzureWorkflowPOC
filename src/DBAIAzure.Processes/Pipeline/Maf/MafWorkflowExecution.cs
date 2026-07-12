// Runs a MAF workflow to completion (or first human-in-the-loop suspension) and reports the outcome the
// orchestrators need to drive a run's lifecycle (spec-019 T022). Progress events are emitted by the
// executors themselves (via their injected progress sink); this helper only captures the final result.
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Pipeline.Maf;

/// <summary>
/// The observable result of one MAF workflow run for an orchestrator: the terminal output (when the run
/// completed) and whether it suspended awaiting a human response.
/// </summary>
/// <typeparam name="TOutput">The state type a terminal executor yields (e.g. the final ticket).</typeparam>
/// <param name="Output">The terminal output, or null when the run suspended or produced none.</param>
/// <param name="Suspended">True when the run paused on a human-in-the-loop <see cref="RequestInfoEvent"/>.</param>
/// <param name="PendingRequest">The pending request when suspended (carries the paused state) — used by US2 resume.</param>
public sealed record MafExecutionOutcome<TOutput>(TOutput? Output, bool Suspended, RequestInfoEvent? PendingRequest);

/// <summary>Runs MAF workflows for the orchestrators and folds the event stream into a lifecycle outcome.</summary>
public static class MafWorkflowExecution
{
    /// <summary>
    /// Runs <paramref name="workflow"/> with <paramref name="input"/> under the run's id (used as the
    /// session id so US2 checkpoint/resume can key on it), returning the terminal output or the pending
    /// human request. The workflow's executors report their own progress as they run.
    /// </summary>
    public static async Task<MafExecutionOutcome<TOutput>> RunAsync<TInput, TOutput>(
        Workflow workflow, TInput input, string runId, CancellationToken cancellationToken)
        where TInput : notnull
    {
        var run = await InProcessExecution.RunStreamingAsync(workflow, input, runId, cancellationToken);

        TOutput? output = default;
        RequestInfoEvent? pendingRequest = null;

        await foreach (var workflowEvent in run.WatchStreamAsync(cancellationToken))
        {
            switch (workflowEvent)
            {
                case WorkflowOutputEvent outputEvent when outputEvent.Data is TOutput terminal:
                    output = terminal;
                    break;
                case RequestInfoEvent request:
                    pendingRequest = request; // the run suspended for a human response
                    break;
            }
        }

        return new MafExecutionOutcome<TOutput>(output, pendingRequest is not null, pendingRequest);
    }
}
