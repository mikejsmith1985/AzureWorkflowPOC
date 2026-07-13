// Owns the lifecycle of every workflow execution run initiated from the visual workflow builder:
// start, background execution via the SK Process Framework, stop, and human-approval routing.
#pragma warning disable SKEXP0080

using System.Collections.Concurrent;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
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
    private readonly IWorkflowRunRepository? _runRepository;
    private readonly IWorkflowApprovalNotifier? _approvalNotifier;
    private readonly IReadOnlyList<IWorkflowObserver> _observers;

    // Async callback invoked on every status transition to broadcast run updates via SignalR
    // (or any other real-time transport) to non-Blazor clients. Wired in Program.cs via
    // IHubContext<WorkflowRunHub>; null means no external broadcast (dev/test default).
    private readonly Func<string, string, Task>? _broadcastUpdate;

    // spec-019 T022: the provider-neutral model client + flag that run the visual workflow on MAF Workflows
    // (default off until cutover), plus deps the node executors and durable checkpointing need.
    private readonly IChatClient? _chatClient;
    private readonly bool _useMafRuntime;
    private readonly IConnectorConfigRepository? _connectorRepository;
    private readonly CheckpointManager? _checkpointManager;

    // ── Constructor ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the orchestrator with a kernel factory and optional persistence/notification
    /// collaborators. All optional parameters default to no-op so existing usages continue to
    /// compile without modification during incremental wiring (FR-18, FR-19, FR-21).
    /// </summary>
    /// <param name="kernelFactory">Factory producing a configured <see cref="Kernel"/> per run.</param>
    /// <param name="runRepository">Persists run lifecycle records; null means no persistence (dev only).</param>
    /// <param name="approvalNotifier">Sends HITL notifications on run pause; null disables Teams notifications.</param>
    /// <param name="observers">Fan-out step-event observers (SQL, SignalR, Azure Monitor); empty list is safe.</param>
    /// <param name="broadcastUpdate">
    /// Optional async callback invoked on every status transition with (runId, statusString).
    /// Program.cs wires this to IHubContext&lt;WorkflowRunHub&gt; for SignalR broadcasting (T048).
    /// </param>
    public WorkflowExecutionOrchestrator(
        Func<Kernel> kernelFactory,
        IWorkflowRunRepository? runRepository = null,
        IWorkflowApprovalNotifier? approvalNotifier = null,
        IEnumerable<IWorkflowObserver>? observers = null,
        Func<string, string, Task>? broadcastUpdate = null,
        IChatClient? chatClient = null,
        bool useMafRuntime = false,
        IConnectorConfigRepository? connectorRepository = null,
        CheckpointManager? checkpointManager = null)
    {
        _kernelFactory    = kernelFactory;
        _runtimeBuilder   = new WorkflowRuntimeBuilder();
        _runRepository    = runRepository;
        _approvalNotifier = approvalNotifier;
        _broadcastUpdate  = broadcastUpdate;
        _observers        = observers is not null
            ? (IReadOnlyList<IWorkflowObserver>)observers.ToList().AsReadOnly()
            : Array.Empty<IWorkflowObserver>();
        _chatClient          = chatClient;
        _useMafRuntime       = useMafRuntime && chatClient is not null;
        _connectorRepository = connectorRepository;
        _checkpointManager   = checkpointManager;
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
    public async Task<string> StartRunAsync(
        WorkflowDefinition workflow,
        string inputDescription,
        CancellationToken ct = default)
    {
        var runId    = Guid.NewGuid().ToString("N")[..8];
        var startedAt = DateTimeOffset.UtcNow;

        var initialRun = new WorkflowExecutionRun(
            RunId:         runId,
            WorkflowId:    workflow.Id,
            Status:        WorkflowRunStatus.NotStarted,
            NodeStates:    BuildInitialNodeStates(workflow),
            FailureReason: null,
            StartedAt:     startedAt,
            CompletedAt:   null);

        var runState = new WorkflowRunState
        {
            Run          = initialRun,
            WorkflowName = workflow.Name,
            TriggeredBy  = workflow.OwnerId,
        };
        _runs[runId] = runState;

        // Persist the initial record before launching the background task so
        // the run is queryable from the history page even before the first status transition.
        if (_runRepository is not null)
        {
            await _runRepository.CreateAsync(new Core.Models.WorkflowRunRecord(
                RunId:         runId,
                WorkflowId:    workflow.Id,
                WorkflowName:  workflow.Name,
                Status:        WorkflowRunStatus.NotStarted,
                TriggeredBy:   workflow.OwnerId,
                StartedAt:     startedAt,
                SuspendedAt:   null,
                ResumedAt:     null,
                CompletedAt:   null,
                FailureReason: null), ct);
        }

        // Launch the background task and intentionally discard the Task reference —
        // the run is observable through _runs and the RunUpdated event, not through
        // the Task itself. Exceptions are caught inside ExecuteRunAsync and written
        // to the run's FailureReason, never allowed to escape as unobserved faults.
        _ = Task.Run(() => ExecuteRunAsync(runState, workflow, inputDescription), ct);

        return runId;
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

        PersistRunState(runState);
        BroadcastRunStatus(runId, WorkflowRunStatus.Cancelled);
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

    public void RehydratePausedRun(WorkflowRunRecord record)
    {
        // Reconstitute just enough state for SubmitApproval to resolve the TCS.
        // The execution task itself cannot be resumed in-process — rehydration only
        // enables operators to approve/reject without waiting for the next full run.
        var run = new WorkflowExecutionRun(
            RunId:        record.RunId,
            WorkflowId:   record.WorkflowId,
            Status:       WorkflowRunStatus.Paused,
            NodeStates:   Array.Empty<NodeExecutionState>(),
            FailureReason: null,
            StartedAt:    record.StartedAt,
            CompletedAt:  null);

        var approvalTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var rehydrated = new WorkflowRunState
        {
            Run          = run,
            WorkflowName = record.WorkflowName,
            TriggeredBy  = record.TriggeredBy,
            SuspendedAt  = record.SuspendedAt,
            ApprovalTcs  = approvalTcs,
        };

        _runs.TryAdd(record.RunId, rehydrated);

        // T041: If the run was already past its escalation timeout before rehydration
        // (e.g., a long server outage), auto-reject immediately so stale runs don't
        // accumulate in the Review Queue indefinitely. The timeout threshold is the
        // default workflow execution timeout; per-approval-node config is not available
        // here without loading the workflow definition (a future improvement).
        ScheduleEscalationTimeout(record.RunId, approvalTcs, record.SuspendedAt);
    }

    /// <summary>
    /// Rehydrates a visual run paused at a HumanApproval gate before an application restart, resuming its MAF
    /// workflow from <paramref name="checkpoint"/> so a reviewer's decision submitted after the restart truly
    /// drives the run to completion (spec-019 T032) — not just resolves an in-memory TCS. The paused run is
    /// seeded at <see cref="WorkflowRunStatus.Running"/> (not Paused) so the drive loop, which arms the
    /// approval gate before advertising Paused, is authoritative and a reviewer callback cannot race ahead of
    /// the live session. No-op unless the MAF runtime is on.
    /// </summary>
    public void RehydratePausedRun(WorkflowRunRecord record, WorkflowDefinition definition, CheckpointInfo checkpoint)
    {
        if (!_useMafRuntime || _checkpointManager is null)
        {
            RehydratePausedRun(record); // fall back to reconstitute-only (approve/reject resolves the TCS)
            return;
        }

        var run = new WorkflowExecutionRun(
            RunId:         record.RunId,
            WorkflowId:    record.WorkflowId,
            Status:        WorkflowRunStatus.Running,
            NodeStates:    Array.Empty<NodeExecutionState>(),
            FailureReason: null,
            StartedAt:     record.StartedAt,
            CompletedAt:   null);

        var runState = new WorkflowRunState
        {
            Run          = run,
            WorkflowName = record.WorkflowName,
            TriggeredBy  = record.TriggeredBy,
            SuspendedAt  = record.SuspendedAt,
            ApprovalTcs  = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        };

        _runs[record.RunId] = runState;
        _ = Task.Run(() => RehydrateAndDriveVisualAsync(runState, definition, checkpoint));
    }

    private async Task RehydrateAndDriveVisualAsync(
        WorkflowRunState runState, WorkflowDefinition definition, CheckpointInfo checkpoint)
    {
        var runId = runState.Run.RunId;
        DBAIAzure.Core.Diagnostics.LlmRunContext.CurrentRunId.Value = runId;
        try
        {
            var services = new MafExecutorServices();
            if (_connectorRepository is not null)
            {
                services.Add<IConnectorConfigRepository>(_connectorRepository);
            }
            var mafWorkflow = new MafWorkflowRuntimeFactory().Build(definition, _chatClient!, services);

            var session = await MafWorkflowSession<WorkflowStepData>.ResumeAsync(
                mafWorkflow, checkpoint, _checkpointManager!, CancellationToken.None);

            var segment = await session.DriveAsync(CancellationToken.None);
            if (segment.Suspended)
            {
                await AwaitApprovalAndResumeAsync(runState, session, segment.PendingRequest!);
            }
            else
            {
                MarkRunCompleted(runState);
            }
        }
        catch (Exception ex)
        {
            runState.Run = runState.Run with
            {
                Status = WorkflowRunStatus.Failed,
                FailureReason = ex.Message,
                CompletedAt = DateTimeOffset.UtcNow,
            };
            PersistRunState(runState);
            BroadcastRunStatus(runId, WorkflowRunStatus.Failed);
            FireRunUpdated(runState, ref runState.LastUpdateTimestamp);
        }
    }

    /// <summary>
    /// Starts a background timer for a rehydrated paused run. If no human decision arrives
    /// within <see cref="DefaultApprovalTimeoutMinutes"/> of the run's suspension instant,
    /// the run is auto-rejected so stale approvals do not block the queue indefinitely (T041).
    /// </summary>
    private static void ScheduleEscalationTimeout(
        string runId,
        TaskCompletionSource<bool> approvalTcs,
        DateTimeOffset? suspendedAt)
    {
        if (approvalTcs.Task.IsCompleted)
            return;

        var suspendedInstant = suspendedAt ?? DateTimeOffset.UtcNow;
        var elapsed          = DateTimeOffset.UtcNow - suspendedInstant;
        var delay            = DefaultApprovalTimeout - elapsed;

        // Run is already past the default timeout — reject synchronously.
        if (delay <= TimeSpan.Zero)
        {
            approvalTcs.TrySetResult(false);
            return;
        }

        // Schedule a fire-and-forget background timer that auto-rejects after the remaining delay.
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay).ConfigureAwait(false);
            approvalTcs.TrySetResult(false); // TrySetResult is a no-op if already resolved by a human.
        });
    }

    /// <summary>
    /// Default duration after which a rehydrated Paused run is automatically rejected if no
    /// human decision has been submitted. 24 hours keeps short-lived runs out of the queue while
    /// still giving operators a full business day to respond (matches typical SLA expectations).
    /// </summary>
    private static readonly TimeSpan DefaultApprovalTimeout = TimeSpan.FromHours(24);

    // ── Background execution ───────────────────────────────────────────────────────

    /// <summary>
    /// Background execution loop for a single workflow run. Translates the plain-language
    /// input, builds the SK process, runs it to completion, and writes the final status
    /// back to the run record. All exceptions are caught and converted to a Failed status
    /// so the background task never faults silently.
    /// </summary>
    /// <summary>
    /// Runs the visual workflow on MAF Workflows (spec-019 T022): builds the graph from the definition,
    /// drives it to completion, and maps the outcome to the run's status. A HumanApproval node suspends the
    /// run as <see cref="WorkflowRunStatus.Paused"/> for review (the approval-resume bridge is US2 for the
    /// visual surface). Executors run under the provider-neutral <see cref="IChatClient"/>.
    /// </summary>
    private async Task ExecuteViaMafAsync(
        WorkflowRunState runState, WorkflowDefinition workflow, string inputDescription)
    {
        var runId = runState.Run.RunId;

        try
        {
            runState.Run = runState.Run with { Status = WorkflowRunStatus.Running };
            PersistRunState(runState);
            FireRunUpdated(runState, ref runState.LastUpdateTimestamp);
            BroadcastRunStatus(runId, WorkflowRunStatus.Running);

            // Seed the entry node (Nodes[0], typically the Trigger) with the run's input description.
            var seed = new WorkflowStepData
            {
                RunId = runId,
                NodeId = workflow.Nodes.Count > 0 ? workflow.Nodes[0].Id : string.Empty,
                InputPayload = inputDescription,
            };

            var services = new MafExecutorServices();
            if (_connectorRepository is not null)
            {
                services.Add<IConnectorConfigRepository>(_connectorRepository);
            }
            var mafWorkflow = new MafWorkflowRuntimeFactory().Build(workflow, _chatClient!, services);

            var session = await MafWorkflowSession<WorkflowStepData>.StartAsync(
                mafWorkflow, seed, runId, _checkpointManager, CancellationToken.None);
            var segment = await session.DriveAsync(CancellationToken.None);

            if (segment.Suspended)
            {
                // A HumanApproval node paused the run for review; await the decision and resume the workflow.
                await AwaitApprovalAndResumeAsync(runState, session, segment.PendingRequest!);
                return;
            }

            MarkRunCompleted(runState);
        }
        catch (Exception ex)
        {
            runState.Run = runState.Run with
            {
                Status = WorkflowRunStatus.Failed,
                FailureReason = ex.Message,
                CompletedAt = DateTimeOffset.UtcNow,
            };
            PersistRunState(runState);
            BroadcastRunStatus(runId, WorkflowRunStatus.Failed);
            FireRunUpdated(runState, ref runState.LastUpdateTimestamp);
        }
    }

    /// <summary>
    /// Suspends the run at a HumanApproval gate, awaits the reviewer's decision (via
    /// <see cref="SubmitApproval"/> or the auto-reject watchdog), then resumes the MAF session by responding
    /// to the approval <see cref="RequestPort"/> with the decided payload and drives the workflow to
    /// completion (spec-019 T027, visual surface). Parity with the SK path: the workflow continues after the
    /// gate carrying <see cref="WorkflowStepData.IsApproved"/>, whatever the decision. A second downstream gate
    /// is handled by re-entering this method with a fresh approval gate.
    /// </summary>
    private async Task AwaitApprovalAndResumeAsync(
        WorkflowRunState runState, MafWorkflowSession<WorkflowStepData> session, RequestInfoEvent pendingRequest)
    {
        var runId = runState.Run.RunId;

        // Arm a fresh approval gate for this suspension so a downstream gate does not reuse a resolved one.
        var suspendedAt = DateTimeOffset.UtcNow;
        var approvalTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runState.ApprovalTcs = approvalTcs;
        runState.SuspendedAt = suspendedAt;
        runState.Run = runState.Run with { Status = WorkflowRunStatus.Paused };
        PersistRunState(runState);
        FireRunUpdated(runState, ref runState.LastUpdateTimestamp);
        BroadcastRunStatus(runId, WorkflowRunStatus.Paused);

        // Auto-reject if no decision arrives within the approval timeout (parity with the SK watchdog; T029).
        ScheduleEscalationTimeout(runId, approvalTcs, suspendedAt);

        var approved = await approvalTcs.Task.ConfigureAwait(false);

        // Resume: respond to the approval port with the decided payload; the workflow continues to completion.
        runState.ResumedAt = DateTimeOffset.UtcNow;
        runState.Run = runState.Run with { Status = WorkflowRunStatus.Running };
        PersistRunState(runState);
        FireRunUpdated(runState, ref runState.LastUpdateTimestamp);
        BroadcastRunStatus(runId, WorkflowRunStatus.Running);

        var pausedData = ExtractStepData(pendingRequest, runId);
        await session.RespondAsync(pendingRequest.Request, pausedData with { IsApproved = approved }, CancellationToken.None);

        var finalSegment = await session.DriveAsync(CancellationToken.None);
        if (finalSegment.Suspended)
        {
            // Another approval gate downstream — handle it with a fresh gate.
            await AwaitApprovalAndResumeAsync(runState, session, finalSegment.PendingRequest!);
            return;
        }

        MarkRunCompleted(runState);
    }

    /// <summary>Reads the paused <see cref="WorkflowStepData"/> from the request, or a minimal fallback.</summary>
    private static WorkflowStepData ExtractStepData(RequestInfoEvent pendingRequest, string runId) =>
        pendingRequest.Request.TryGetDataAs<WorkflowStepData>(out var data) && data is not null
            ? data
            : new WorkflowStepData { RunId = runId, NodeId = string.Empty };

    /// <summary>Marks every node Completed and the run Completed, then persists and notifies.</summary>
    private void MarkRunCompleted(WorkflowRunState runState)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var completedNodeStates = runState.Run.NodeStates
            .Select(nodeState => nodeState with
            {
                Status = NodeStatus.Completed,
                StartedAt = nodeState.StartedAt ?? runState.Run.StartedAt,
                CompletedAt = nodeState.CompletedAt ?? completedAt,
            })
            .ToList()
            .AsReadOnly();

        runState.Run = runState.Run with
        {
            Status = WorkflowRunStatus.Completed,
            NodeStates = completedNodeStates,
            CompletedAt = completedAt,
        };
        PersistRunState(runState);
        BroadcastRunStatus(runState.Run.RunId, WorkflowRunStatus.Completed);
        FireRunUpdated(runState, ref runState.LastUpdateTimestamp);
    }

    private async Task ExecuteRunAsync(
        WorkflowRunState runState,
        WorkflowDefinition workflow,
        string inputDescription)
    {
        var runId = runState.Run.RunId;

        // Make the run id ambient so the LLM connector's usage reporter tags this run's calls (FR-008).
        DBAIAzure.Core.Diagnostics.LlmRunContext.CurrentRunId.Value = runId;

        // spec-019 T022: run the visual workflow on MAF Workflows when enabled (default off until cutover).
        if (_useMafRuntime)
        {
            await ExecuteViaMafAsync(runState, workflow, inputDescription);
            return;
        }

        try
        {
            // ── Mark the run as active ────────────────────────────────────────────
            runState.Run = runState.Run with { Status = WorkflowRunStatus.Running };
            PersistRunState(runState);
            FireRunUpdated(runState, ref runState.LastUpdateTimestamp);
            BroadcastRunStatus(runId, WorkflowRunStatus.Running);

            // Emit StepStarted for each runnable node so the Run History page shows
            // the run beginning even before per-step SK callbacks are available (T058).
            foreach (var node in workflow.Nodes.Where(n => n.NodeType != WorkflowNodeType.Trigger))
            {
                DispatchEvent(new Core.Models.WorkflowExecutionEvent(
                    EventId:         Guid.NewGuid(),
                    RunId:           runId,
                    NodeId:          node.Id,
                    NodeLabel:       node.Label,
                    EventType:       Core.Models.WorkflowEventType.StepStarted,
                    OccurredAt:      runState.Run.StartedAt,
                    DurationMs:      null,
                    Outcome:         null,
                    LlmModelName:    null,
                    LlmInputTokens:  null,
                    LlmOutputTokens: null));
            }

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

                // Merge the Trigger's GoalPrompt and initial-data description into the input payload so
                // the first runnable step receives the workflow intent as context. The description is
                // read via the realized TriggerNodeConfig when present, falling back to the legacy blob.
                var initialDataDescription = ReadTriggerInitialData(triggerNode.FunctionConfig);
                var triggerContext = triggerNode.GoalPrompt ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(initialDataDescription))
                {
                    triggerContext = string.IsNullOrWhiteSpace(triggerContext)
                        ? $"Available data: {initialDataDescription}"
                        : $"{triggerContext}\n\nAvailable data: {initialDataDescription}";
                }

                if (!string.IsNullOrWhiteSpace(triggerContext))
                {
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
                var timedOutAt = DateTimeOffset.UtcNow;
                var timedOutNodeStates = runState.Run.NodeStates
                    .Select(nodeState => nodeState.Status is NodeStatus.NotStarted or NodeStatus.Active
                        ? nodeState with { Status = NodeStatus.Skipped, CompletedAt = timedOutAt }
                        : nodeState)
                    .ToList()
                    .AsReadOnly();

                runState.Run = runState.Run with
                {
                    Status      = WorkflowRunStatus.TimedOut,
                    NodeStates  = timedOutNodeStates,
                    CompletedAt = timedOutAt,
                };

                // Emit StepFailed for nodes that did not finish before timeout (T058).
                foreach (var node in workflow.Nodes.Where(n => n.NodeType != WorkflowNodeType.Trigger))
                {
                    DispatchEvent(new Core.Models.WorkflowExecutionEvent(
                        EventId:         Guid.NewGuid(),
                        RunId:           runId,
                        NodeId:          node.Id,
                        NodeLabel:       node.Label,
                        EventType:       Core.Models.WorkflowEventType.StepFailed,
                        OccurredAt:      timedOutAt,
                        DurationMs:      (long)(timedOutAt - runState.Run.StartedAt).TotalMilliseconds,
                        Outcome:         "Timed out before completing.",
                        LlmModelName:    null,
                        LlmInputTokens:  null,
                        LlmOutputTokens: null));
                }
            }
            else
            {
                var completedAt = DateTimeOffset.UtcNow;
                var completedNodeStates = runState.Run.NodeStates
                    .Select(nodeState => nodeState with
                    {
                        Status      = NodeStatus.Completed,
                        StartedAt   = nodeState.StartedAt   ?? runState.Run.StartedAt,
                        CompletedAt = nodeState.CompletedAt ?? completedAt,
                    })
                    .ToList()
                    .AsReadOnly();

                runState.Run = runState.Run with
                {
                    Status      = WorkflowRunStatus.Completed,
                    NodeStates  = completedNodeStates,
                    CompletedAt = completedAt,
                };

                // Emit StepCompleted for each runnable node on clean completion (T058).
                foreach (var node in workflow.Nodes.Where(n => n.NodeType != WorkflowNodeType.Trigger))
                {
                    DispatchEvent(new Core.Models.WorkflowExecutionEvent(
                        EventId:         Guid.NewGuid(),
                        RunId:           runId,
                        NodeId:          node.Id,
                        NodeLabel:       node.Label,
                        EventType:       Core.Models.WorkflowEventType.StepCompleted,
                        OccurredAt:      completedAt,
                        DurationMs:      (long)(completedAt - runState.Run.StartedAt).TotalMilliseconds,
                        Outcome:         "Completed",
                        LlmModelName:    null,
                        LlmInputTokens:  null,
                        LlmOutputTokens: null));
                }
            }

            PersistRunState(runState);
            BroadcastRunStatus(runId, runState.Run.Status);
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

            PersistRunState(runState);
            BroadcastRunStatus(runId, WorkflowRunStatus.Failed);
            DispatchEvent(new Core.Models.WorkflowExecutionEvent(
                EventId:         Guid.NewGuid(),
                RunId:           runId,
                NodeId:          null,
                NodeLabel:       null,
                EventType:       Core.Models.WorkflowEventType.StepFailed,
                OccurredAt:      DateTimeOffset.UtcNow,
                DurationMs:      null,
                Outcome:         ex.Message,
                LlmModelName:    null,
                LlmInputTokens:  null,
                LlmOutputTokens: null));
            RunUpdated?.Invoke(runId);
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the trigger's initial-data description, preferring the realized
    /// <see cref="TriggerNodeConfig"/> and falling back to the legacy <c>{initialDataDescription}</c>
    /// blob so workflows authored before realization continue to run unchanged (FR-15.6).
    /// </summary>
    private static string? ReadTriggerInitialData(string? functionConfig)
    {
        if (string.IsNullOrWhiteSpace(functionConfig))
            return null;

        var realized = NodeConfigSerializer.ReadConfig<TriggerNodeConfig>(functionConfig);
        if (realized is not null && !string.IsNullOrWhiteSpace(realized.InitialDataDescription))
            return realized.InitialDataDescription;

        // Legacy path: the pre-realization trigger stored the description as a flat property.
        try
        {
            using var configDoc = System.Text.Json.JsonDocument.Parse(functionConfig);
            if (configDoc.RootElement.TryGetProperty("initialDataDescription", out var descProp))
                return descProp.GetString();
        }
        catch (System.Text.Json.JsonException)
        {
            // Unparseable legacy blob — treat as no description rather than failing the run.
        }
        return null;
    }

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

    /// <summary>
    /// Persists the current in-memory run state to the repository (if configured).
    /// Fire-and-forget: errors are swallowed so a transient DB failure never disrupts execution.
    /// </summary>
    private void PersistRunState(WorkflowRunState runState)
    {
        if (_runRepository is null)
            return;

        var snapshot = new Core.Models.WorkflowRunRecord(
            RunId:         runState.Run.RunId,
            WorkflowId:    runState.Run.WorkflowId,
            WorkflowName:  runState.WorkflowName,
            Status:        runState.Run.Status,
            TriggeredBy:   runState.TriggeredBy,
            StartedAt:     runState.Run.StartedAt,
            SuspendedAt:   runState.SuspendedAt,
            ResumedAt:     runState.ResumedAt,
            CompletedAt:   runState.Run.CompletedAt,
            FailureReason: runState.Run.FailureReason);

        // Fire-and-forget — never await on the hot execution path.
        _ = _runRepository.UpdateAsync(snapshot);
    }

    /// <summary>
    /// Fan-out a workflow execution event to all registered observers. Never throws.
    /// </summary>
    private void DispatchEvent(Core.Models.WorkflowExecutionEvent evt)
    {
        foreach (var observer in _observers)
        {
            _ = observer.RecordEventAsync(evt);
        }
    }

    /// <summary>
    /// Invokes the optional SignalR broadcast callback (T048) so external clients (non-Blazor,
    /// cross-server) receive run status changes without polling. Fire-and-forget — never throws.
    /// </summary>
    private void BroadcastRunStatus(string runId, WorkflowRunStatus status)
    {
        if (_broadcastUpdate is null)
            return;

        _ = Task.Run(async () =>
        {
            try { await _broadcastUpdate(runId, status.ToString()).ConfigureAwait(false); }
            catch { /* Broadcast failures must never disrupt the run execution path. */ }
        });
    }

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

        /// <summary>Display name of the workflow — kept here so persistence calls don't need to load the definition.</summary>
        public string WorkflowName { get; set; } = string.Empty;

        /// <summary>Identity of the user or process that triggered the run (email or "system").</summary>
        public string TriggeredBy { get; set; } = string.Empty;

        /// <summary>UTC instant the run entered Paused status; null until it first pauses.</summary>
        public DateTimeOffset? SuspendedAt { get; set; }

        /// <summary>UTC instant the run last resumed from Paused; null until it has been resumed.</summary>
        public DateTimeOffset? ResumedAt { get; set; }

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
