using DBAIAzure.Core.Diagnostics;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Collections.Concurrent;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// Singleton service that owns all pipeline run lifecycles. Runs the ticket-intake pipeline on MAF
/// Workflows; the provider-neutral <see cref="IChatClient"/> gives every LLM executor its model access
/// without coupling this class to any specific chat completion provider.
/// </summary>
public sealed class PipelineOrchestrator
{
    private readonly IRunRepository _repository;
    private readonly IHitlNotifier? _hitlNotifier;
    private readonly string _portalBaseUrl;
    private readonly IConnectorHealthChecker? _healthChecker;

    // The provider-neutral model client every LLM executor uses.
    private readonly IChatClient _chatClient;

    // When set, MAF runs are checkpointed so a run paused at the clarification gate can be resumed after a
    // restart (spec-019 T032). Null → in-process resume only.
    private readonly CheckpointManager? _checkpointManager;

    private readonly ConcurrentDictionary<string, PipelineRun> _runs = new();

    /// <summary>Fired on a background thread whenever a run's state or events change.</summary>
    public event Action<string>? RunUpdated;

    public PipelineOrchestrator(
        IChatClient chatClient,
        IRunRepository? repository = null,
        IHitlNotifier? hitlNotifier = null,
        string portalBaseUrl = "http://localhost:5000",
        IConnectorHealthChecker? healthChecker = null,
        CheckpointManager? checkpointManager = null)
    {
        _chatClient        = chatClient;
        _repository        = repository ?? NullRunRepository.Instance;
        _hitlNotifier      = hitlNotifier;
        _portalBaseUrl     = portalBaseUrl.TrimEnd('/');
        _healthChecker     = healthChecker;
        _checkpointManager = checkpointManager;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Enqueue a new ticket and return its run ID immediately.</summary>
    public string StartRun(TicketState ticket)
    {
        var runId = Guid.NewGuid().ToString("N")[..8];
        var run   = new PipelineRun(runId, ticket);
        _runs[runId] = run;

        _ = _repository.UpsertRunAsync(runId, ticket, PipelineRunStatus.Running);
        _ = Task.Run(() => ExecuteRunAsync(run, ticket));
        return runId;
    }

    public PipelineRun? GetRun(string runId) => _runs.GetValueOrDefault(runId);

    public IReadOnlyList<PipelineRun> GetAllRuns() =>
        [.. _runs.Values.OrderByDescending(r => r.StartedAt)];

    /// <summary>
    /// Returns persisted run summaries — survives server restarts.
    /// Falls back to in-memory list if the repository is not configured.
    /// </summary>
    public async Task<IReadOnlyList<PersistedRunSummary>> ListRunsAsync(
        string? search = null,
        PipelineRunStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        var persisted = await _repository.ListRunsAsync(search, status, skip, take, ct);
        if (persisted.Count > 0) return persisted;

        // Fallback to in-memory when storage is not configured
        return _runs.Values
            .OrderByDescending(r => r.StartedAt)
            .Skip(skip).Take(take)
            .Select(r => new PersistedRunSummary(
                r.RunId,
                r.InitialTicket.TicketId,
                r.InitialTicket.Title,
                r.InitialTicket.Source,
                r.InitialTicket.SnowNumber,
                r.InitialTicket.SnowPriority,
                r.Status,
                r.CurrentTicket?.StoryPoints,
                r.CurrentTicket?.JiraIssueUrl,
                r.StartedAt,
                null))
            .ToList();
    }

    /// <summary>Submit the PO's answer and unblock the waiting background task.</summary>
    public void SubmitHitlAnswer(string runId, string answer)
    {
        if (_runs.TryGetValue(runId, out var run))
            run.ProvideHitlInput(answer);
    }

    /// <summary>
    /// Replay a run from a specific step snapshot — creates a new run starting
    /// from the captured TicketState, equivalent to LangGraph time-travel.
    /// </summary>
    public string ReplayFromSnapshot(string stepName, TicketState snapshotState)
    {
        var replayTicket = snapshotState with
        {
            TicketId = $"{snapshotState.TicketId}-replay-{DateTime.UtcNow:HHmmss}",
            Source   = "replay",
        };
        return StartRun(replayTicket);
    }

    // ── Background execution loop ──────────────────────────────────────────────

    private async Task ExecuteRunAsync(PipelineRun run, TicketState initialTicket)
    {
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
                    run.SetFailed(FormatPreflightDiagnostic(failure));
                    await _repository.UpsertRunAsync(run.RunId, run.InitialTicket, PipelineRunStatus.Failed);
                    RunUpdated?.Invoke(run.RunId);
                    return;
                }
            }

            // Run the ticket-intake pipeline on MAF Workflows, driving the clarification loop.
            await ExecuteViaMafAsync(run, initialTicket);
        }
        catch (Exception ex)
        {
            run.SetFailed(ex.Message);
            await _repository.UpsertRunAsync(run.RunId, run.CurrentTicket ?? run.InitialTicket, PipelineRunStatus.Failed);
            RunUpdated?.Invoke(run.RunId);
        }
    }

    /// <summary>
    /// Runs the intake pipeline on MAF Workflows (spec-019 T022/US2), driving the clarification loop: when
    /// a ticket fails the Definition of Ready the run suspends at the HITL <see cref="RequestPort"/>; the PO's
    /// answer is applied to the ticket and sent back so validation re-runs, until the ticket is ready or the
    /// max clarification round is reached. Executors report their own progress via the run-bound reporter.
    /// </summary>
    private async Task ExecuteViaMafAsync(PipelineRun run, TicketState initialTicket)
    {
        // Cost/telemetry capture keys on the ambient run id (read by CostCapturingChatClient).
        LlmRunContext.CurrentRunId.Value = run.RunId;

        var reporter = new BoundProgressReporter(run, () => RunUpdated?.Invoke(run.RunId), _repository);
        var services = new MafExecutorServices().Add<IProgressReporter>(reporter);
        var workflow = MafIntakeWorkflowFactory.Build(_chatClient!, services);

        var session = await MafWorkflowSession<TicketState>.StartAsync(
            workflow, initialTicket, run.RunId, _checkpointManager, CancellationToken.None);

        await DriveMafSessionAsync(run, session, initialTicket, reporter);
    }

    /// <summary>
    /// Rehydrates a run paused before an application restart (spec-019 T032): rebuilds the run in memory,
    /// resumes its MAF workflow from <paramref name="checkpoint"/>, and re-enters the clarification loop so a
    /// PO answer submitted after the restart drives it to completion. No-op unless the MAF runtime is on.
    /// </summary>
    public void RehydratePausedRun(TicketState pausedTicket, CheckpointInfo checkpoint)
    {
        if (_checkpointManager is null)
        {
            return;
        }

        var runId = checkpoint.SessionId;
        var run = new PipelineRun(runId, pausedTicket);
        run.SetRunning();
        _runs[runId] = run;

        // The drive loop sets AwaitingHuman and arms the HITL gate once the resumed run re-emits its request,
        // so a caller must observe AwaitingHuman (set there) before submitting an answer — the same timing as
        // a fresh run. Pre-setting it here would let an answer race in before the gate is armed and be lost.
        _ = Task.Run(() => RehydrateAndDriveAsync(run, pausedTicket, checkpoint));
    }

    private async Task RehydrateAndDriveAsync(PipelineRun run, TicketState pausedTicket, CheckpointInfo checkpoint)
    {
        try
        {
            LlmRunContext.CurrentRunId.Value = run.RunId;

            var reporter = new BoundProgressReporter(run, () => RunUpdated?.Invoke(run.RunId), _repository);
            var services = new MafExecutorServices().Add<IProgressReporter>(reporter);

            // A live paused run was checkpointed with the normal Build graph — resume with the same graph.
            var workflow = MafIntakeWorkflowFactory.Build(_chatClient!, services);
            var session = await MafWorkflowSession<TicketState>.ResumeAsync(
                workflow, checkpoint, _checkpointManager!, CancellationToken.None);

            await DriveMafSessionAsync(run, session, pausedTicket, reporter);
        }
        catch (Exception ex)
        {
            run.SetFailed(ex.Message);
            await _repository.UpsertRunAsync(run.RunId, run.CurrentTicket ?? pausedTicket, PipelineRunStatus.Failed);
            RunUpdated?.Invoke(run.RunId);
        }
    }

    /// <summary>
    /// Drives a started/resumed MAF session's clarification loop: on each suspension surface the paused
    /// ticket and await the PO's answer, apply it, and resume validation until the run completes.
    /// </summary>
    private async Task DriveMafSessionAsync(
        PipelineRun run, MafWorkflowSession<TicketState> session, TicketState fallbackTicket, BoundProgressReporter reporter)
    {
        void NotifyUpdated() => RunUpdated?.Invoke(run.RunId);

        while (true)
        {
            var segment = await session.DriveAsync(CancellationToken.None);

            if (!segment.Suspended)
            {
                var finalTicket = segment.Output ?? reporter.FinalTicket ?? fallbackTicket;
                run.SetComplete(finalTicket);
                await _repository.UpsertRunAsync(run.RunId, finalTicket, run.Status);
                NotifyUpdated();
                return;
            }

            // Suspended at the clarification gate: surface the paused ticket (with its questions), notify,
            // and await the PO's answer.
            var pausedTicket = ExtractPausedTicket(segment.PendingRequest!, reporter.FinalTicket ?? fallbackTicket);
            run.SetAwaitingHuman(pausedTicket);
            await _repository.UpsertRunAsync(run.RunId, pausedTicket, PipelineRunStatus.AwaitingHuman);
            NotifyUpdated();
            NotifyHitl(run, pausedTicket);

            var answer = await run.WaitForHitlInputAsync();

            // Apply the answer and resume: the answered ticket re-enters validation via the port response.
            // Clear the now-answered questions so validation's ready/not-ready routing (keyed on whether the
            // ticket carries clarifying questions) evaluates the re-validation cleanly.
            var answeredTicket = pausedTicket with
            {
                HumanAnswer = answer,
                ClarificationRound = pausedTicket.ClarificationRound + 1,
                ClarifyingQuestions = [],
            };
            run.SetRunning();
            run.AddEvent(new PipelineEvent(
                "HitlResume",
                $"PO answered (round {answeredTicket.ClarificationRound}) — re-validating",
                ReportLevel.Info,
                DateTimeOffset.UtcNow));
            await _repository.UpsertRunAsync(run.RunId, answeredTicket, PipelineRunStatus.Running);
            NotifyUpdated();

            await session.RespondAsync(segment.PendingRequest!.Request, answeredTicket, CancellationToken.None);
        }
    }

    /// <summary>Reads the paused ticket from the pending request, falling back to the last known ticket.</summary>
    private static TicketState ExtractPausedTicket(RequestInfoEvent request, TicketState fallback)
        => request.Request.TryGetDataAs<TicketState>(out var ticket) && ticket is not null ? ticket : fallback;

    /// <summary>Fires the Teams / external HITL notification for a paused run (non-blocking), if configured.</summary>
    private void NotifyHitl(PipelineRun run, TicketState pausedTicket)
    {
        if (_hitlNotifier is null)
        {
            return;
        }
        var portalUrl = $"{_portalBaseUrl}/run/{run.RunId}";
        _ = _hitlNotifier.NotifyAsync(
            run.RunId, pausedTicket.TicketId, pausedTicket.Title, pausedTicket.ClarifyingQuestions, portalUrl);
    }

    private static string FormatPreflightDiagnostic(PipelinePreflightFailure failure)
    {
        var reasons = failure.FailingConnectors
            .Select(r => $"{r.Type}: {r.Message}")
            .ToArray();
        return $"Pre-flight check failed — {string.Join("; ", reasons)}";
    }
}
