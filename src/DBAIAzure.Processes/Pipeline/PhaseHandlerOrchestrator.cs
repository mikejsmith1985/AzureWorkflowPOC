// Singleton that owns phase-handler run lifecycles: start, pause for approval, resume, persist.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Collections.Concurrent;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// Owns every phase-handler run, mirroring the ticket pipeline's <c>PipelineOrchestrator</c>:
/// a background task per run, a MAF workflow driven to its approval pause, an out-of-band approval
/// gate, and durable persistence. It enforces the core safety rule — no board write occurs before
/// an approved decision (FR-006) — by only resuming the create executor once a decision arrives.
/// </summary>
public sealed class PhaseHandlerOrchestrator
{
    /// <summary>
    /// Hours a run may wait for human approval before it is automatically expired as Failed.
    /// Prevents the background task leaking indefinitely when a reviewer never responds (e.g. the
    /// decision card link was lost or the feature was abandoned). 72 hours ≈ 3 business days.
    /// </summary>
    private const int ApprovalTimeoutHours = 72;

    private readonly IChatClient _chatClient;
    private readonly IArtifactReader _artifactReader;
    private readonly PhaseWorkItemWriterDeps _writerDeps;
    private readonly IPhaseRunRepository _repository;
    private readonly IPhaseApprovalNotifier? _approvalNotifier;
    private readonly string _portalBaseUrl;
    private readonly IConnectorHealthChecker? _healthChecker;
    private readonly IBindingKeyMinter? _bindingKeyMinter;

    // When set, MAF runs are checkpointed so a run paused at the approval gate survives a restart (T032).
    private readonly CheckpointManager? _checkpointManager;

    private readonly ConcurrentDictionary<string, PhaseHandlerRun> _runs = new();

    /// <summary>Fired on a background thread whenever a run's state changes (for live UI updates).</summary>
    public event Action<string>? RunUpdated;

    public PhaseHandlerOrchestrator(
        IChatClient chatClient,
        IArtifactReader artifactReader,
        PhaseWorkItemWriterDeps writerDeps,
        IPhaseRunRepository? repository = null,
        IPhaseApprovalNotifier? approvalNotifier = null,
        string portalBaseUrl = "http://localhost:5000",
        IConnectorHealthChecker? healthChecker = null,
        IBindingKeyMinter? bindingKeyMinter = null,
        CheckpointManager? checkpointManager = null)
    {
        _chatClient        = chatClient;
        _artifactReader    = artifactReader;
        _writerDeps        = writerDeps;
        _repository        = repository ?? NullPhaseRunRepository.Instance;
        _approvalNotifier  = approvalNotifier;
        _portalBaseUrl     = portalBaseUrl.TrimEnd('/');
        _healthChecker     = healthChecker;
        _bindingKeyMinter  = bindingKeyMinter;
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

            // Run read → validate → approval → create-on-approval on MAF Workflows.
            await ExecuteViaMafAsync(run);
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
    /// Runs the phase handler on MAF Workflows (spec-019 T022/T027): read → validate → the approval gate,
    /// where the run suspends; on the reviewer's decision it resumes and the create executor writes the board
    /// (only on approval — FR-006). Terminal failure paths (missing artifacts, validation/DoR failure)
    /// complete without suspending. The board-write dependencies are sourced from the same DI container the
    /// SK path uses (the kernel), so both frameworks write the board identically.
    /// </summary>
    private async Task ExecuteViaMafAsync(PhaseHandlerRun run)
    {
        var sink = new RecordingSink(run, () => RunUpdated?.Invoke(run.RunId), _repository);
        var workflow = BuildMafWorkflow(sink);

        // Arm the approval gate before driving, so a fast suspension cannot race the reviewer's callback.
        run.BeginAwaitingApproval();

        var session = await MafWorkflowSession<PhaseHandlerState>.StartAsync(
            workflow, run.State, run.RunId, _checkpointManager, CancellationToken.None);

        await DriveApprovalSessionAsync(run, session, sink);
    }

    /// <summary>
    /// Rehydrates a phase-handler run paused at the approval gate before an application restart (spec-019
    /// T032): rebuilds the run in memory, resumes its MAF workflow from <paramref name="checkpoint"/> (which
    /// re-emits the outstanding approval request carrying the paused state), and drives it so a reviewer's
    /// decision submitted after the restart still writes the board. No-op unless the MAF runtime is on.
    /// </summary>
    /// <param name="placeholderState">A minimal state (RunId/FeatureKey/Phase) — the full paused state is
    /// recovered from the checkpoint's re-emitted request, so only the run identity is needed here.</param>
    public void RehydratePausedRun(PhaseHandlerState placeholderState, CheckpointInfo checkpoint)
    {
        if (_checkpointManager is null)
        {
            return;
        }

        // Seed the run at a non-awaiting status (it is mid-resume): AwaitingApproval must be set only by the
        // drive loop, AFTER it arms the gate (MarkPaused), so a reviewer callback cannot observe the status and
        // submit before the gate is live — which would drop the decision (ProvideApproval requires _hasPaused).
        var run = new PhaseHandlerRun(placeholderState with { Status = PhaseRunStatus.Validated });
        _runs[placeholderState.RunId] = run;
        _ = Task.Run(() => RehydrateAndDriveAsync(run, checkpoint));
    }

    private async Task RehydrateAndDriveAsync(PhaseHandlerRun run, CheckpointInfo checkpoint)
    {
        DBAIAzure.Core.Diagnostics.LlmRunContext.CurrentRunId.Value = run.RunId;
        try
        {
            var sink = new RecordingSink(run, () => RunUpdated?.Invoke(run.RunId), _repository);
            var workflow = BuildMafWorkflow(sink);

            run.BeginAwaitingApproval();

            var session = await MafWorkflowSession<PhaseHandlerState>.ResumeAsync(
                workflow, checkpoint, _checkpointManager!, CancellationToken.None);

            await DriveApprovalSessionAsync(run, session, sink);
        }
        catch (Exception ex)
        {
            var failed = run.State with { Status = PhaseRunStatus.Failed, FailureReason = ex.Message };
            await PersistAndNotifyAsync(run, failed);
        }
    }

    /// <summary>
    /// Builds the phase-handler MAF workflow for a run from the directly-injected dependencies: the model
    /// client, this run's progress sink, the artifact reader, the optional binding-key minter, and the
    /// board-write dependencies (<see cref="PhaseWorkItemWriterDeps"/>). No Semantic Kernel container is
    /// involved — the create executor writes the board through the same adapter/ledger the app wires in DI.
    /// </summary>
    private Microsoft.Agents.AI.Workflows.Workflow BuildMafWorkflow(RecordingSink sink) =>
        MafPhaseHandlerWorkflowFactory.Build(
            _chatClient, _artifactReader, sink, _bindingKeyMinter, _writerDeps);

    /// <summary>
    /// Drives a started or resumed MAF session: a terminal failure path completes immediately; a suspension at
    /// the approval gate parks the run, awaits the reviewer's decision (bounded by the timeout), then responds
    /// with the decided state and drives to completion. Shared by the fresh-start and restart-rehydration
    /// paths so both resume identically — the paused state is read from the request (populated from the
    /// checkpoint on resume), not from local memory.
    /// </summary>
    private async Task DriveApprovalSessionAsync(
        PhaseHandlerRun run, MafWorkflowSession<PhaseHandlerState> session, RecordingSink sink)
    {
        var segment = await session.DriveAsync(CancellationToken.None);

        if (!segment.Suspended)
        {
            // No suspension → a terminal failure path (missing artifacts, validation/DoR failure).
            await PersistAndNotifyAsync(run, segment.Output ?? sink.LatestState ?? run.State);
            return;
        }

        // Parked at the approval gate: the paused state rides the request (recovered from the checkpoint on a
        // rehydrated run, where nothing has run locally to populate the sink). Arm the pause BEFORE advertising
        // AwaitingApproval so a reviewer callback that observes the status cannot race ahead of the gate being
        // live (ProvideApproval requires _hasPaused) and be dropped.
        var pausedState = ExtractPausedState(segment.PendingRequest!, sink.LatestState ?? run.State)
            with { Status = PhaseRunStatus.AwaitingApproval };
        run.MarkPaused();
        run.UpdateState(pausedState);
        await _repository.UpsertRunAsync(pausedState);
        RunUpdated?.Invoke(run.RunId);
        PushApprovalCard(run, pausedState);

        // Wait for the reviewer's decision (delivered via SubmitApproval → ProvideApproval), bounded by the
        // configured timeout so the background task does not leak if approval never arrives.
        ApprovalDecision decision;
        using var approvalTimeoutCts = new CancellationTokenSource(TimeSpan.FromHours(ApprovalTimeoutHours));
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

        // Resume: respond to the approval port with the decided state; the create executor writes the board on
        // an approved decision (or yields Rejected otherwise), then the run completes.
        var decidedState = pausedState with
        {
            Decision = decision,
            Status = decision.IsApproved ? PhaseRunStatus.WritingBoard : PhaseRunStatus.Rejected,
        };
        run.UpdateState(decidedState);
        RunUpdated?.Invoke(run.RunId);

        await session.RespondAsync(segment.PendingRequest!.Request, decidedState, CancellationToken.None);

        var finalSegment = await session.DriveAsync(CancellationToken.None);
        await PersistAndNotifyAsync(run, finalSegment.Output ?? sink.LatestState ?? decidedState);
    }

    /// <summary>Reads the paused state from the approval request, falling back to the last known state.</summary>
    private static PhaseHandlerState ExtractPausedState(RequestInfoEvent request, PhaseHandlerState fallback)
        => request.Request.TryGetDataAs<PhaseHandlerState>(out var state) && state is not null ? state : fallback;

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
