// Owns the lifecycle of every workflow execution run initiated from the visual workflow builder:
// start, background execution via the SK Process Framework, stop, and human-approval routing.
#pragma warning disable SKEXP0080

using System.Collections.Concurrent;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// Singleton service that owns all visual-workflow execution run lifecycles.
/// It accepts a kernel factory so the web layer can supply its own kernel configuration
/// (LLM connector, DI services) without coupling this class to any specific provider.
/// The SK Process Framework handles all orchestration state — this class is intentionally
/// thin, acting only as the entry/exit boundary between the UI layer and the process runtime.
/// </summary>
public sealed class WorkflowExecutionOrchestrator : IWorkflowExecutionOrchestrator
{
    // ── Constants ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maximum number of RunUpdated events that may be fired per second per run.
    /// Coalescing prevents the Blazor UI from being overwhelmed with state-diff renders
    /// when a long-running process emits rapid completions.
    /// </summary>
    private const int MaxRunUpdatesPerSecond = 10;

    /// <summary>Reciprocal of <see cref="MaxRunUpdatesPerSecond"/> expressed in milliseconds.</summary>
    private const int MinMillisecondsBetweenUpdates = 1000 / MaxRunUpdatesPerSecond;

    // ── Fields ─────────────────────────────────────────────────────────────────────

    private readonly Func<Kernel> _kernelFactory;
    private readonly WorkflowRuntimeBuilder _runtimeBuilder;
    private readonly ConcurrentDictionary<string, WorkflowRunState> _runs = new();

    // ── Constructor ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the orchestrator with a kernel factory that is called once per run to
    /// obtain an <see cref="IChatCompletionService"/> for the input-translation step.
    /// <see cref="WorkflowRuntimeBuilder"/> and <see cref="WorkflowInputTranslator"/> are
    /// stateless helpers constructed internally — callers need not supply them.
    /// </summary>
    /// <param name="kernelFactory">
    /// Zero-argument factory that produces a configured <see cref="Kernel"/>.
    /// Called on the background thread immediately before each run's first LLM request.
    /// </param>
    public WorkflowExecutionOrchestrator(Func<Kernel> kernelFactory)
    {
        _kernelFactory  = kernelFactory;
        _runtimeBuilder = new WorkflowRuntimeBuilder();
    }

    // ── IWorkflowExecutionOrchestrator ─────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Always raised on a background thread. Blazor subscribers must dispatch back to
    /// the UI thread via <c>InvokeAsync</c> before touching component state.
    /// </remarks>
    public event Action<string>? RunUpdated;

    /// <inheritdoc />
    /// <remarks>
    /// Returns a stable runId immediately; actual execution begins on a <see cref="Task.Run"/>
    /// background thread so the caller is never blocked waiting for the first LLM response.
    /// </remarks>
    public Task<string> StartRunAsync(
        WorkflowDefinition workflow,
        string inputDescription,
        CancellationToken ct = default)
    {
        var runId = Guid.NewGuid().ToString("N")[..8];

        var initialRun = new WorkflowExecutionRun(
            RunId:         runId,
            WorkflowId:    workflow.Id,
            Status:        WorkflowRunStatus.NotStarted,
            NodeStates:    BuildInitialNodeStates(workflow),
            FailureReason: null,
            StartedAt:     DateTimeOffset.UtcNow,
            CompletedAt:   null);

        var runState = new WorkflowRunState { Run = initialRun };
        _runs[runId] = runState;

        // Launch the background task and intentionally discard the Task reference —
        // the run is observable through _runs and the RunUpdated event, not through
        // the Task itself. Exceptions are caught inside ExecuteRunAsync and written
        // to the run's FailureReason, never allowed to escape as unobserved faults.
        _ = Task.Run(() => ExecuteRunAsync(runState, workflow, inputDescription), ct);

        return Task.FromResult(runId);
    }

    /// <inheritdoc />
    public WorkflowExecutionRun? GetRun(string runId) =>
        _runs.GetValueOrDefault(runId)?.Run;

    /// <inheritdoc />
    /// <remarks>
    /// Cancellation is cooperative — the run transitions to <see cref="WorkflowRunStatus.Cancelled"/>
    /// asynchronously once the SK process framework honours the token. Callers should
    /// observe <see cref="RunUpdated"/> to detect the terminal transition rather than polling.
    /// </remarks>
    public void RequestStop(string runId)
    {
        if (!_runs.TryGetValue(runId, out var runState))
            return;

        // Signal the background task to stop at its next safe checkpoint.
        runState.Cts.Cancel();

        // Immediately transition all non-terminal node states to Skipped so the UI
        // reflects the intent before the background thread has caught up to the cancel.
        var updatedNodeStates = runState.Run.NodeStates
            .Select(nodeState => nodeState.Status is NodeStatus.NotStarted or NodeStatus.Active
                ? nodeState with { Status = NodeStatus.Skipped, CompletedAt = DateTimeOffset.UtcNow }
                : nodeState)
            .ToList()
            .AsReadOnly();

        runState.Run = runState.Run with
        {
            Status      = WorkflowRunStatus.Cancelled,
            NodeStates  = updatedNodeStates,
            CompletedAt = DateTimeOffset.UtcNow,
        };

        RunUpdated?.Invoke(runId);
    }

    /// <inheritdoc />
    /// <remarks>
    /// If no run exists for <paramref name="runId"/>, or the run is not currently
    /// suspended at an approval gate, this call is silently ignored to avoid race conditions
    /// with runs that complete or fail between the UI rendering the approval button and the
    /// user clicking it.
    /// </remarks>
    public void SubmitApproval(string runId, bool approved)
    {
        if (!_runs.TryGetValue(runId, out var runState))
            return;

        // SetResult is a no-op if the TCS has already been resolved, so this is safe to
        // call even if the run has already moved past the approval gate.
        runState.ApprovalTcs?.TrySetResult(approved);
    }

    // ── Background execution ───────────────────────────────────────────────────────

    /// <summary>
    /// Background execution loop for a single workflow run. Translates the plain-language
    /// input, builds the SK process, runs it to completion, and writes the final status
    /// back to the run record. All exceptions are caught and converted to a Failed status
    /// so the background task never faults silently.
    /// </summary>
    private async Task ExecuteRunAsync(
        WorkflowRunState runState,
        WorkflowDefinition workflow,
        string inputDescription)
    {
        var runId = runState.Run.RunId;

        try
        {
            // ── Mark the run as active ────────────────────────────────────────────
            runState.Run = runState.Run with { Status = WorkflowRunStatus.Running };
            FireRunUpdated(runState, ref runState.LastUpdateTimestamp);

            // ── Translate plain-language input to a structured workflow payload ────
            // WorkflowInputTranslator is stateless so it is safe to create per-run;
            // it relies on the kernel for the chat-completion backend only.
            var kernel = _kernelFactory();
            var chatService = kernel.GetRequiredService<IChatCompletionService>();
            var inputTranslator = new WorkflowInputTranslator(chatService);

            var (translatedInput, _) = await inputTranslator
                .TranslateAsync(inputDescription, workflow.Nodes, runState.Cts.Token)
                .ConfigureAwait(false);
            // Mutable local so the Trigger context merge below can prepend to it.
            var structuredInput = translatedInput;

            // ── Resolve the graph entry point ─────────────────────────────────────
            // The Trigger node marks the entry point but has no runnable logic of its own.
            // If a Trigger is present, we skip it and start from the first downstream node
            // (the target of the Trigger's "Begin" output edge). If no Trigger is present
            // (e.g., legacy workflows) we fall back to the first node in the list.
            var triggerNode = workflow.Nodes.FirstOrDefault(n => n.NodeType == WorkflowNodeType.Trigger);
            string firstRunnableNodeId;

            if (triggerNode is not null)
            {
                // Find the downstream node connected to the Trigger's output port.
                var triggerOutEdge = workflow.Edges.FirstOrDefault(e => e.SourceNodeId == triggerNode.Id);
                firstRunnableNodeId = triggerOutEdge?.TargetNodeId
                    ?? workflow.Nodes.FirstOrDefault(n => n.NodeType != WorkflowNodeType.Trigger)?.Id
                    ?? string.Empty;

                // Merge the Trigger's GoalPrompt and initialDataDescription into the input
                // payload so the first runnable step receives the workflow intent as context.
                if (!string.IsNullOrWhiteSpace(triggerNode.GoalPrompt))
                {
                    var triggerContext = triggerNode.GoalPrompt;
                    if (!string.IsNullOrWhiteSpace(triggerNode.FunctionConfig))
                    {
                        try
                        {
                            using var configDoc = System.Text.Json.JsonDocument.Parse(triggerNode.FunctionConfig);
                            if (configDoc.RootElement.TryGetProperty("initialDataDescription", out var descProp)
                                && !string.IsNullOrWhiteSpace(descProp.GetString()))
                            {
                                triggerContext += $"\n\nAvailable data: {descProp.GetString()}";
                            }
                        }
                        catch { }
                    }

                    structuredInput = string.IsNullOrWhiteSpace(structuredInput)
                        ? triggerContext
                        : $"{triggerContext}\n\n{structuredInput}";
                }
            }
            else
            {
                firstRunnableNodeId = workflow.Nodes.Count > 0 ? workflow.Nodes[0].Id : string.Empty;
            }

            // ── Build the KernelProcess from the workflow definition ───────────────
            var process = _runtimeBuilder.Build(workflow);

            var startEvent = new KernelProcessEvent
            {
                Id   = WorkflowNodeEvents.NodeStart,
                Data = new WorkflowStepData
                {
                    RunId        = runId,
                    NodeId       = firstRunnableNodeId,
                    InputPayload = structuredInput,
                },
            };

            // ── Run the process under the configured timeout ──────────────────────
            var executionTimeout = TimeSpan.FromMinutes(workflow.Settings.ExecutionTimeoutMinutes);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(runState.Cts.Token);
            timeoutCts.CancelAfter(executionTimeout);

            bool didTimeOut = false;
            try
            {
                await LocalKernelProcessFactory
                    .RunToEndAsync(process, kernel, startEvent, executionTimeout)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!runState.Cts.IsCancellationRequested)
            {
                // The linked token fired due to timeout, not an explicit stop request.
                didTimeOut = true;
            }

            // ── Write the terminal status ─────────────────────────────────────────
            // For this POC we do not have per-step callbacks from the SK process, so all
            // nodes are marked Succeeded on clean completion. Future work (specs/003) will
            // wire per-step progress events to update individual node states during the run.
            if (runState.Cts.IsCancellationRequested && !didTimeOut)
            {
                // RequestStop was called; the status was already written there — nothing more to do.
                return;
            }

            if (didTimeOut)
            {
                var timedOutNodeStates = runState.Run.NodeStates
                    .Select(nodeState => nodeState.Status is NodeStatus.NotStarted or NodeStatus.Active
                        ? nodeState with { Status = NodeStatus.Skipped, CompletedAt = DateTimeOffset.UtcNow }
                        : nodeState)
                    .ToList()
                    .AsReadOnly();

                runState.Run = runState.Run with
                {
                    Status      = WorkflowRunStatus.TimedOut,
                    NodeStates  = timedOutNodeStates,
                    CompletedAt = DateTimeOffset.UtcNow,
                };
            }
            else
            {
                var completedNodeStates = runState.Run.NodeStates
                    .Select(nodeState => nodeState with
                    {
                        Status      = NodeStatus.Completed,
                        StartedAt   = nodeState.StartedAt   ?? runState.Run.StartedAt,
                        CompletedAt = nodeState.CompletedAt ?? DateTimeOffset.UtcNow,
                    })
                    .ToList()
                    .AsReadOnly();

                runState.Run = runState.Run with
                {
                    Status      = WorkflowRunStatus.Completed,
                    NodeStates  = completedNodeStates,
                    CompletedAt = DateTimeOffset.UtcNow,
                };
            }

            FireRunUpdated(runState, ref runState.LastUpdateTimestamp);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            runState.Run = runState.Run with
            {
                Status        = WorkflowRunStatus.Failed,
                FailureReason = ex.Message,
                CompletedAt   = DateTimeOffset.UtcNow,
            };

            RunUpdated?.Invoke(runId);
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Fires <see cref="RunUpdated"/> for the given run, but only if at least
    /// <see cref="MinMillisecondsBetweenUpdates"/> milliseconds have elapsed since the
    /// last fire. This coalesces rapid state changes at <see cref="MaxRunUpdatesPerSecond"/>
    /// to prevent flooding the Blazor render queue when many nodes complete in quick succession.
    /// </summary>
    /// <param name="runState">The run state record whose runId is broadcast.</param>
    /// <param name="lastUpdateTimestamp">
    /// Reference to the run-scoped last-fire timestamp; updated in place when the event is fired.
    /// </param>
    private void FireRunUpdated(WorkflowRunState runState, ref long lastUpdateTimestamp)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (nowMs - lastUpdateTimestamp < MinMillisecondsBetweenUpdates)
            return;

        lastUpdateTimestamp = nowMs;
        RunUpdated?.Invoke(runState.Run.RunId);
    }

    /// <summary>
    /// Produces the initial <see cref="NodeExecutionState"/> list for a workflow, with every
    /// node in the <see cref="NodeStatus.NotStarted"/> state. The list mirrors the canvas node
    /// order so the UI can render them in the same sequence without additional sorting.
    /// </summary>
    /// <summary>
    /// Produces the initial <see cref="NodeExecutionState"/> list for a workflow.
    /// Trigger nodes start as <see cref="NodeStatus.Skipped"/> because they are the entry
    /// point marker and have no runnable logic — the runtime skips them and begins execution
    /// at the first downstream node. All other nodes start as <see cref="NodeStatus.NotStarted"/>.
    /// </summary>
    private static IReadOnlyList<NodeExecutionState> BuildInitialNodeStates(WorkflowDefinition workflow) =>
        workflow.Nodes
            .Select(node => new NodeExecutionState
            {
                NodeId = node.Id,
                Status = node.NodeType == WorkflowNodeType.Trigger
                    ? NodeStatus.Skipped
                    : NodeStatus.NotStarted,
            })
            .ToList()
            .AsReadOnly();

    // ── Private inner class ────────────────────────────────────────────────────────

    /// <summary>
    /// Mutable container for all mutable per-run state held by the orchestrator.
    /// The <see cref="Run"/> record itself is replaced atomically on each transition;
    /// the <see cref="Cts"/> and <see cref="ApprovalTcs"/> are long-lived over the run lifetime.
    /// </summary>
    private sealed class WorkflowRunState
    {
        /// <summary>
        /// Immutable snapshot of the run; replaced with a <c>with</c> expression on every
        /// state transition so observers always receive a consistent, non-partial view.
        /// </summary>
        public WorkflowExecutionRun Run { get; set; } = null!;

        /// <summary>
        /// Source used to request cooperative cancellation of the background execution task.
        /// Cancelled by <see cref="RequestStop"/> and by the per-run timeout watchdog.
        /// </summary>
        public CancellationTokenSource Cts { get; set; } = new();

        /// <summary>
        /// Completion source parked while the run is suspended at a human-approval gate.
        /// <see cref="SubmitApproval"/> resolves it with the reviewer's decision; the
        /// background task awaits it before continuing past the gate.
        /// Null when the run is not currently suspended.
        /// </summary>
        public TaskCompletionSource<bool>? ApprovalTcs { get; set; }

        /// <summary>
        /// Unix-millisecond timestamp of the last <see cref="RunUpdated"/> fire for this run.
        /// Used by the update coalescer to enforce the <see cref="MaxRunUpdatesPerSecond"/> cap.
        /// Declared as a field so it can be passed by reference to <see cref="FireRunUpdated"/>.
        /// </summary>
        public long LastUpdateTimestamp;
    }
}
