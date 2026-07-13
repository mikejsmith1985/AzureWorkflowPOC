// Singleton that owns phase-handler run lifecycles: start, pause for approval, resume, persist.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using System.Collections.Concurrent;

// Suppress SKEXP0080 — SK Process Framework is experimental in 1.77.0
#pragma warning disable SKEXP0080

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// Owns every phase-handler run, mirroring the ticket pipeline's <c>PipelineOrchestrator</c>:
/// a background task per run, an SK process driven to its approval pause, an out-of-band approval
/// gate, and durable persistence. It enforces the core safety rule — no board write occurs before
/// an approved decision (FR-006) — by only resuming the create step once a decision arrives.
/// </summary>
public sealed class PhaseHandlerOrchestrator
{
    private const int ProcessTimeoutSeconds = 180;

    /// <summary>
    /// Hours a run may wait for human approval before it is automatically expired as Failed.
    /// Prevents the background task leaking indefinitely when a reviewer never responds (e.g. the
    /// decision card link was lost or the feature was abandoned). 72 hours ≈ 3 business days.
    /// </summary>
    private const int ApprovalTimeoutHours = 72;

    private readonly Func<IPhaseProgressSink, Kernel> _kernelFactory;
    private readonly IPhaseRunRepository _repository;
    private readonly IPhaseApprovalNotifier? _approvalNotifier;
    private readonly string _portalBaseUrl;
    private readonly IConnectorHealthChecker? _healthChecker;

    // spec-019 T022: the MAF model client + executor dependencies + the flag that runs the phase handler
    // on MAF Workflows. Additive — off until cutover, so production still runs on SK; approval resume on
    // the MAF path (create-on-decision) lands in US2.
    private readonly IChatClient? _chatClient;
    private readonly IArtifactReader? _artifactReader;
    private readonly IBindingKeyMinter? _bindingKeyMinter;
    private readonly bool _useMafRuntime;

    // spec-019 T032: when set, MAF runs are checkpointed so a run paused at the approval gate survives a restart.
    private readonly CheckpointManager? _checkpointManager;

    private readonly ConcurrentDictionary<string, PhaseHandlerRun> _runs = new();

    /// <summary>Fired on a background thread whenever a run's state changes (for live UI updates).</summary>
    public event Action<string>? RunUpdated;

    public PhaseHandlerOrchestrator(
        Func<IPhaseProgressSink, Kernel> kernelFactory,
        IPhaseRunRepository? repository = null,
        IPhaseApprovalNotifier? approvalNotifier = null,
        string portalBaseUrl = "http://localhost:5000",
        IConnectorHealthChecker? healthChecker = null,
        IChatClient? chatClient = null,
        IArtifactReader? artifactReader = null,
        IBindingKeyMinter? bindingKeyMinter = null,
        bool useMafRuntime = false,
        CheckpointManager? checkpointManager = null)
    {
        _kernelFactory     = kernelFactory;
        _repository        = repository ?? NullPhaseRunRepository.Instance;
        _approvalNotifier  = approvalNotifier;
        _portalBaseUrl     = portalBaseUrl.TrimEnd('/');
        _healthChecker     = healthChecker;
        _chatClient        = chatClient;
        _artifactReader    = artifactReader;
        _bindingKeyMinter  = bindingKeyMinter;
        // The MAF path needs both the model client and an artifact reader to run read→validate→approval.
        _useMafRuntime     = useMafRuntime && chatClient is not null && artifactReader is not null;
        _checkpointManager = checkpointManager;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a phase-handler run and returns its run id immediately. An unsupported phase is
    /// recorded and short-circuited with no work item (FR-014); supported phases run the pipeline.
    /// </summary>
    public string StartRun(PhaseHandlerState initialState)
    {
        var run = new PhaseHandlerRun(initialState);
        _runs[initialState.RunId] = run;

        if (initialState.Phase == SpecKitPhase.Unsupported)
        {
            var unsupported = initialState with { Status = PhaseRunStatus.Unsupported };
            run.UpdateState(unsupported);
            _ = _repository.UpsertRunAsync(unsupported);
            RunUpdated?.Invoke(run.RunId);
            return run.RunId;
        }

        _ = _repository.UpsertRunAsync(initialState);
        _ = Task.Run(() => ExecuteRunAsync(run));
        return run.RunId;
    }

    /// <summary>Returns the run for a run id, or null.</summary>
    public PhaseHandlerRun? GetRun(string runId) => _runs.GetValueOrDefault(runId);

    /// <summary>All runs, newest first.</summary>
    public IReadOnlyList<PhaseHandlerRun> GetAllRuns() =>
        [.. _runs.Values.OrderByDescending(r => r.StartedAt)];

    /// <summary>Result of applying a decision callback, so the controller can map the right HTTP status.</summary>
    public enum ApprovalResult
    {
        /// <summary>Decision accepted and applied to the waiting run.</summary>
        Applied,

        /// <summary>No run with that id, or the run is not awaiting a decision (→ 404).</summary>
        NotAwaiting,
    }

    /// <summary>
    /// Applies a reviewer decision to a paused run, unblocking its background loop. Returns
    /// <see cref="ApprovalResult.NotAwaiting"/> when the run is unknown or already decided.
    /// </summary>
    public ApprovalResult SubmitApproval(string runId, ApprovalDecision decision)
    {
        if (!_runs.TryGetValue(runId, out var run)) return ApprovalResult.NotAwaiting;
        return run.ProvideApproval(decision) ? ApprovalResult.Applied : ApprovalResult.NotAwaiting;
    }

    // ── Background execution loop ──────────────────────────────────────────────

    private async Task ExecuteRunAsync(PhaseHandlerRun run)
    {
        // Tag this phase run's LLM calls (validation + any connector test) so their usage is recorded
        // against this run — set before the kernel runs so the AsyncLocal flows to the validation call.
        DBAIAzure.Core.Diagnostics.LlmRunContext.CurrentRunId.Value = run.RunId;

        try
        {
            // Pre-flight: all four connectors must pass a live functional test before the run starts (FR-018).
            // A 30-second wall-clock cap (SC-008) ensures the pipeline is never stalled by a hung connector.
            if (_healthChecker is not null)
            {
                using var preflightCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var preflightResults = await _healthChecker.CheckAllAsync(preflightCts.Token);
                var failures = preflightResults.Where(r => !r.IsSuccess).ToList();
                if (failures.Count > 0)
                {
                    var failure = new PipelinePreflightFailure(failures.AsReadOnly());
                    var reasons = failure.FailingConnectors
                        .Select(r => $"{r.Type}: {r.Message}")
                        .ToArray();
                    var diagnostic = $"Pre-flight check failed — {string.Join("; ", reasons)}";
                    var failed = run.State with { Status = PhaseRunStatus.Failed, FailureReason = diagnostic };
                    run.UpdateState(failed);
                    await _repository.UpsertRunAsync(failed);
                    RunUpdated?.Invoke(run.RunId);
                    return;
                }
            }

            // spec-019 T022: when the MAF runtime is enabled, run read→validate→approval on MAF Workflows.
            // The SK loop below stays as the production default until cutover.
            if (_useMafRuntime)
            {
                await ExecuteViaMafAsync(run);
                return;
            }

            var sink = new RecordingSink(run, () => RunUpdated?.Invoke(run.RunId), _repository);
            var kernel = _kernelFactory(sink);

            // Arm the approval gate before running, so a fast callback cannot race the pause.
            run.BeginAwaitingApproval();

            var channel = new ApprovalExternalChannel();
            var process = PhaseHandlerPipelineBuilder.Build();
            var startEvent = new KernelProcessEvent
            {
                Id = PhaseHandlerEvents.PhaseSignalReceived,
                Data = run.State,
            };

            await LocalKernelProcessFactory.RunToEndAsync(
                process, kernel, startEvent, TimeSpan.FromSeconds(ProcessTimeoutSeconds), channel);

            // If the run failed before reaching the pause (e.g. missing artifacts), it is terminal.
            if (!channel.WasPaused)
            {
                await PersistAndNotifyAsync(run, sink.LatestState ?? run.State);
                return;
            }

            // Paused for approval: persist AwaitingApproval and push the decision card.
            var pausedState = ExtractState(channel.PausedMessage!, sink.LatestState ?? run.State);
            run.UpdateState(pausedState);
            run.MarkPaused();
            await _repository.UpsertRunAsync(pausedState);
            RunUpdated?.Invoke(run.RunId);
            PushApprovalCard(run, pausedState);

            // Wait for the reviewer's decision (delivered via SubmitApproval), bounded by the
            // configured timeout so the background task does not leak if approval never arrives.
            ApprovalDecision decision;
            using var approvalTimeoutCts = new CancellationTokenSource(
                TimeSpan.FromHours(ApprovalTimeoutHours));
            try
            {
                decision = await run.WaitForApprovalAsync().WaitAsync(approvalTimeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                var expired = pausedState with
                {
                    Status = PhaseRunStatus.Failed,
                    FailureReason = $"No approval received within {ApprovalTimeoutHours} hours — run expired.",
                };
                await PersistAndNotifyAsync(run, expired);
                return;
            }

            await ResumeWithDecisionAsync(run, pausedState, decision);
        }
        catch (Exception ex)
        {
            var failed = run.State with { Status = PhaseRunStatus.Failed, FailureReason = ex.Message };
            run.UpdateState(failed);
            await _repository.UpsertRunAsync(failed);
            RunUpdated?.Invoke(run.RunId);
        }
    }

    /// <summary>
    /// Runs the phase handler on MAF Workflows (spec-019 T022): read → validate → the approval gate, where
    /// the run suspends. Terminal failure paths (missing artifacts, validation/DoR failure) complete here;
    /// applying the reviewer's decision and creating the work item on resume is US2.
    /// </summary>
    private async Task ExecuteViaMafAsync(PhaseHandlerRun run)
    {
        var sink = new RecordingSink(run, () => RunUpdated?.Invoke(run.RunId), _repository);

        var services = new MafExecutorServices()
            .Add<IArtifactReader>(_artifactReader!)
            .Add<IPhaseProgressSink>(sink);
        if (_bindingKeyMinter is not null)
        {
            services.Add<IBindingKeyMinter>(_bindingKeyMinter);
        }

        // Arm the approval gate before running, so a fast suspension cannot race it (matches the SK path)
        // and the gate is ready for the US2 resume bridge.
        run.BeginAwaitingApproval();

        var workflow = MafPhaseHandlerWorkflowFactory.Build(_chatClient!, services);
        var outcome = await MafWorkflowExecution.RunAsync<PhaseHandlerState, PhaseHandlerState>(
            workflow, run.State, run.RunId, CancellationToken.None, _checkpointManager);

        if (outcome.Suspended)
        {
            // Parked at the approval gate: persist AwaitingApproval and push the decision card. Applying the
            // decision and resuming to the create step is US2 (RequestInfoEvent → SendResponseAsync).
            var pausedState = (sink.LatestState ?? run.State) with { Status = PhaseRunStatus.AwaitingApproval };
            run.UpdateState(pausedState);
            run.MarkPaused();
            await _repository.UpsertRunAsync(pausedState);
            RunUpdated?.Invoke(run.RunId);
            PushApprovalCard(run, pausedState);
            return;
        }

        // No suspension → a terminal failure path (missing artifacts, validation/DoR failure).
        await PersistAndNotifyAsync(run, outcome.Output ?? sink.LatestState ?? run.State);
    }

    /// <summary>Restarts the process from the decision, routing straight to the create step.</summary>
    private async Task ResumeWithDecisionAsync(
        PhaseHandlerRun run, PhaseHandlerState pausedState, ApprovalDecision decision)
    {
        var sink = new RecordingSink(run, () => RunUpdated?.Invoke(run.RunId), _repository);
        var kernel = _kernelFactory(sink);

        var decidedState = pausedState with { Decision = decision };
        var channel = new ApprovalExternalChannel();
        var process = PhaseHandlerPipelineBuilder.Build();
        var resumeEvent = new KernelProcessEvent
        {
            Id = PhaseHandlerEvents.ApprovalDecided,
            Data = decidedState,
        };

        await LocalKernelProcessFactory.RunToEndAsync(
            process, kernel, resumeEvent, TimeSpan.FromSeconds(ProcessTimeoutSeconds), channel);

        await PersistAndNotifyAsync(run, sink.LatestState ?? decidedState);
    }

    /// <summary>Persists the final state and fires the update event.</summary>
    private async Task PersistAndNotifyAsync(PhaseHandlerRun run, PhaseHandlerState finalState)
    {
        run.UpdateState(finalState);
        await _repository.UpsertRunAsync(finalState);
        RunUpdated?.Invoke(run.RunId);
    }

    /// <summary>Pushes the validation summary + gaps + portal link to the decision card (non-blocking).</summary>
    private void PushApprovalCard(PhaseHandlerRun run, PhaseHandlerState pausedState)
    {
        if (_approvalNotifier is null || pausedState.Validation is null) return;

        var portalUrl = $"{_portalBaseUrl}/run/{run.RunId}";
        _ = _approvalNotifier.NotifyAsync(
            run.RunId,
            pausedState.FeatureKey,
            pausedState.Phase,
            pausedState.Validation.Summary,
            pausedState.Validation.Gaps,
            portalUrl);
    }

    /// <summary>Recovers the state object from the proxy message; falls back to the last sink snapshot.</summary>
    private static PhaseHandlerState ExtractState(KernelProcessProxyMessage message, PhaseHandlerState fallback)
    {
        if (message.EventData?.ToObject() is PhaseHandlerState state) return state;
        return fallback;
    }

    /// <summary>
    /// Bridges <see cref="IPhaseProgressSink"/> to the live run + persistence, capturing the latest
    /// state each step produces so the orchestrator can read the final outcome after the process ends.
    /// </summary>
    private sealed class RecordingSink : IPhaseProgressSink
    {
        private readonly PhaseHandlerRun _run;
        private readonly Action _notifyUpdated;
        private readonly IPhaseRunRepository _repository;

        public PhaseHandlerState? LatestState { get; private set; }

        public RecordingSink(PhaseHandlerRun run, Action notifyUpdated, IPhaseRunRepository repository)
        {
            _run = run;
            _notifyUpdated = notifyUpdated;
            _repository = repository;
        }

        public void Report(PhaseHandlerState state)
        {
            LatestState = state;
            _run.UpdateState(state);
            _ = _repository.UpsertRunAsync(state);
            _notifyUpdated();
        }
    }
}
