// Drives a MAF Workflow to idle and captures the observable outcome a parity test asserts on: the
// ordered executor sequence, any yielded outputs, and a pending human request (spec-019 US1 harness).
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Tests.Parity;

/// <summary>
/// What a parity test observes from one workflow run: the ordered ids of the executors that ran
/// (from <see cref="ExecutorInvokedEvent"/>), the outputs the terminal executor yielded, and the
/// human-in-the-loop request the run suspended on (null when the run completed without pausing).
/// </summary>
/// <param name="ExecutorSequence">Executor ids in invocation order — the step-sequence parity signal.</param>
/// <param name="Outputs">Outputs surfaced via <see cref="WorkflowOutputEvent"/> (the final state).</param>
/// <param name="PendingRequest">The pending HITL request if the run suspended, else null.</param>
/// <param name="Run">The live run, so a test can resume it via <see cref="StreamingRun.SendResponseAsync"/>.</param>
public sealed record MafRunObservation(
    IReadOnlyList<string> ExecutorSequence,
    IReadOnlyList<object> Outputs,
    RequestInfoEvent? PendingRequest,
    StreamingRun Run);

/// <summary>
/// Runs a MAF <see cref="Workflow"/> in-process and folds its event stream into a
/// <see cref="MafRunObservation"/>. Used only by parity tests: it drives the real GA runtime (no mock
/// framework) so the assertion is genuinely against MAF behaviour, while the model layer is pinned by a
/// <see cref="RecordedChatClient"/> so the run is deterministic.
/// </summary>
public static class MafWorkflowRunner
{
    private const string ParitySessionId = "parity-test";

    // A parity run must reach a terminal or suspended state quickly; if it does not, fail loudly rather
    // than hang the whole suite. The recorded client makes runs deterministic, so this is a safety net.
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Runs <paramref name="workflow"/> with <paramref name="input"/> and collects the outcome.</summary>
    public static async Task<MafRunObservation> RunAsync<TInput>(
        Workflow workflow, TInput input, CancellationToken cancellationToken = default)
        where TInput : notnull
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(RunTimeout);
        try
        {
            var run = await InProcessExecution.RunStreamingAsync(workflow, input, ParitySessionId, timeoutSource.Token);
            return await ObserveAsync(run, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The parity workflow did not reach a terminal or suspended state within {RunTimeout.TotalSeconds}s.");
        }
    }

    /// <summary>
    /// Folds a run's event stream until it goes idle (completed or awaiting a human response), returning
    /// the executor sequence, outputs, and any pending request captured along the way.
    /// </summary>
    public static async Task<MafRunObservation> ObserveAsync(StreamingRun run, CancellationToken cancellationToken = default)
    {
        var executorSequence = new List<string>();
        var outputs = new List<object>();
        RequestInfoEvent? pendingRequest = null;

        await foreach (var workflowEvent in run.WatchStreamAsync(cancellationToken))
        {
            switch (workflowEvent)
            {
                case ExecutorInvokedEvent invoked:
                    executorSequence.Add(invoked.ExecutorId);
                    break;
                case RequestInfoEvent request:
                    pendingRequest = request; // the run has suspended for a human response
                    break;
                case WorkflowOutputEvent output when output.Data is not null:
                    outputs.Add(output.Data);
                    break;
            }
        }

        return new MafRunObservation(executorSequence, outputs, pendingRequest, run);
    }
}
