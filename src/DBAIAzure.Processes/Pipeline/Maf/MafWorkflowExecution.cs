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
    // Active execution must reach a terminal or suspended state within this bound; the human wait at a
    // suspension happens AFTER the stream completes at PendingRequests, so this caps only live execution.
    private static readonly TimeSpan ActiveExecutionTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Runs <paramref name="workflow"/> with <paramref name="input"/> under the run's id (used as the
    /// session id so US2 checkpoint/resume can key on it), returning the terminal output or the pending
    /// human request. The workflow's executors report their own progress as they run.
    /// </summary>
    public static async Task<MafExecutionOutcome<TOutput>> RunAsync<TInput, TOutput>(
        Workflow workflow, TInput input, string runId, CancellationToken cancellationToken)
        where TInput : notnull
    {
        // Watch the stream under a real, linked token bounded by the active-execution timeout. The stream
        // completes on its own at PendingRequests/Idle/Ended; the bound is a safety net against a stuck run.
        using var executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        executionCts.CancelAfter(ActiveExecutionTimeout);
        var executionToken = executionCts.Token;

        var run = await InProcessExecution.RunStreamingAsync(workflow, input, runId, executionToken);

        TOutput? output = default;
        RequestInfoEvent? pendingRequest = null;

        await foreach (var workflowEvent in run.WatchStreamAsync(executionToken))
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
                    throw new InvalidOperationException(
                        $"Executor '{failure.ExecutorId}' failed: {failure.Data}");
                case WorkflowErrorEvent error:
                    throw new InvalidOperationException($"Workflow error: {error.Data}");
            }

            // Stop as soon as the run suspends: WatchStreamAsync does not reliably complete at
            // RunStatus.PendingRequests on a background thread, so breaking here avoids a hang (the
            // completed path ends the stream on its own). The pending request is captured for US2 resume.
            if (pendingRequest is not null)
            {
                break;
            }
        }

        return new MafExecutionOutcome<TOutput>(output, pendingRequest is not null, pendingRequest);
    }
}
