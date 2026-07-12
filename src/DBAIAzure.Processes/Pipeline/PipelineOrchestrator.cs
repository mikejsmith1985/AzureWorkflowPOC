using DBAIAzure.Core.Diagnostics;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using System.Collections.Concurrent;

// Suppress SKEXP0080 — SK Process Framework is experimental in 1.77.0
#pragma warning disable SKEXP0080

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// Singleton service that owns all pipeline run lifecycles.
/// Accepts a kernel factory so the web and runner layers can provide their own
/// kernel configuration (Anthropic key, IProgressReporter registration) without
/// coupling this class to any specific chat completion provider.
/// </summary>
public sealed class PipelineOrchestrator
{
    private const int MaxClarificationRounds = 3;

    private readonly Func<IProgressReporter, Kernel> _kernelFactory;
    private readonly IRunRepository _repository;
    private readonly IHitlNotifier? _hitlNotifier;
    private readonly string _portalBaseUrl;
    private readonly IConnectorHealthChecker? _healthChecker;

    // spec-019 T022: the provider-neutral model client and the flag that runs the pipeline on MAF
    // Workflows instead of the SK Process Framework. Additive — the flag is off until the atomic cutover,
    // so production behaviour is unchanged; HITL resume on the MAF path lands in US2.
    private readonly IChatClient? _chatClient;
    private readonly bool _useMafRuntime;

    private readonly ConcurrentDictionary<string, PipelineRun> _runs = new();

    /// <summary>Fired on a background thread whenever a run's state or events change.</summary>
    public event Action<string>? RunUpdated;

    public PipelineOrchestrator(
        Func<IProgressReporter, Kernel> kernelFactory,
        IRunRepository? repository = null,
        IHitlNotifier? hitlNotifier = null,
        string portalBaseUrl = "http://localhost:5000",
        IConnectorHealthChecker? healthChecker = null,
        IChatClient? chatClient = null,
        bool useMafRuntime = false)
    {
        _kernelFactory  = kernelFactory;
        _repository     = repository ?? NullRunRepository.Instance;
        _hitlNotifier   = hitlNotifier;
        _portalBaseUrl  = portalBaseUrl.TrimEnd('/');
        _healthChecker  = healthChecker;
        _chatClient     = chatClient;
        _useMafRuntime  = useMafRuntime && chatClient is not null;
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

            // spec-019 T022: when the MAF runtime is enabled, run the pipeline on MAF Workflows instead of
            // the SK Process Framework. The SK loop below stays as the production default until cutover.
            if (_useMafRuntime)
            {
                await ExecuteViaMafAsync(run, initialTicket);
                return;
            }

            var currentTicket = initialTicket;

            for (int clarificationRound = 0; clarificationRound <= MaxClarificationRounds; clarificationRound++)
            {
                void NotifyUpdated() => RunUpdated?.Invoke(run.RunId);

                var reporter    = new BoundProgressReporter(run, NotifyUpdated, _repository);
                var kernel      = _kernelFactory(reporter);
                var hitlChannel = new HitlExternalChannel();
                var process     = IntakePipelineBuilder.Build();

                var startEvent = clarificationRound == 0
                    ? new KernelProcessEvent { Id = Events.TicketReceived,  Data = currentTicket }
                    : new KernelProcessEvent { Id = Events.HumanResponded, Data = currentTicket };

                await LocalKernelProcessFactory.RunToEndAsync(
                    process, kernel, startEvent, TimeSpan.FromSeconds(180), hitlChannel);

                if (!hitlChannel.WasPaused)
                {
                    var finalTicket = reporter.FinalTicket ?? currentTicket;
                    run.SetComplete(finalTicket);
                    await _repository.UpsertRunAsync(run.RunId, finalTicket, run.Status);
                    RunUpdated?.Invoke(run.RunId);
                    break;
                }

                var pausedTicket = ExtractTicket(hitlChannel.PausedMessage!, currentTicket);
                run.SetAwaitingHuman(pausedTicket);
                await _repository.UpsertRunAsync(run.RunId, pausedTicket, PipelineRunStatus.AwaitingHuman);
                RunUpdated?.Invoke(run.RunId);

                // Fire Teams / external notification — non-blocking
                if (_hitlNotifier is not null)
                {
                    var portalUrl = $"{_portalBaseUrl}/run/{run.RunId}";
                    _ = _hitlNotifier.NotifyAsync(
                        run.RunId,
                        pausedTicket.TicketId,
                        pausedTicket.Title,
                        pausedTicket.ClarifyingQuestions,
                        portalUrl);
                }

                var answer = await run.WaitForHitlInputAsync();

                currentTicket = pausedTicket with
                {
                    HumanAnswer       = answer,
                    ClarificationRound = pausedTicket.ClarificationRound + 1,
                };

                run.SetRunning();
                run.AddEvent(new PipelineEvent(
                    "HitlResume",
                    $"PO answered (round {currentTicket.ClarificationRound}) — re-validating",
                    ReportLevel.Info,
                    DateTimeOffset.UtcNow));
                await _repository.UpsertRunAsync(run.RunId, currentTicket, PipelineRunStatus.Running);
                RunUpdated?.Invoke(run.RunId);
            }
        }
        catch (Exception ex)
        {
            run.SetFailed(ex.Message);
            await _repository.UpsertRunAsync(run.RunId, run.CurrentTicket ?? run.InitialTicket, PipelineRunStatus.Failed);
            RunUpdated?.Invoke(run.RunId);
        }
    }

    /// <summary>
    /// Runs the intake pipeline on MAF Workflows (spec-019 T022). The executors report their own progress
    /// through the run-bound reporter; this method drives the run's lifecycle from the terminal output.
    /// A ticket that fails the Definition of Ready suspends at the clarification gate — full resume is US2.
    /// </summary>
    private async Task ExecuteViaMafAsync(PipelineRun run, TicketState initialTicket)
    {
        void NotifyUpdated() => RunUpdated?.Invoke(run.RunId);

        // Cost/telemetry capture keys on the ambient run id (read by CostCapturingChatClient).
        LlmRunContext.CurrentRunId.Value = run.RunId;

        var reporter = new BoundProgressReporter(run, NotifyUpdated, _repository);
        var services = new MafExecutorServices().Add<IProgressReporter>(reporter);
        var workflow = MafIntakeWorkflowFactory.Build(_chatClient!, services);

        var outcome = await MafWorkflowExecution.RunAsync<TicketState, TicketState>(
            workflow, initialTicket, run.RunId, CancellationToken.None);

        if (outcome.Suspended)
        {
            // Not-ready path: parked at the clarification gate. Submitting the answer and resuming the
            // run is US2 (RequestInfoEvent → SendResponseAsync); here the run is surfaced as awaiting input.
            var pausedTicket = reporter.FinalTicket ?? initialTicket;
            run.SetAwaitingHuman(pausedTicket);
            await _repository.UpsertRunAsync(run.RunId, pausedTicket, PipelineRunStatus.AwaitingHuman);
            NotifyUpdated();
            return;
        }

        var finalTicket = outcome.Output ?? reporter.FinalTicket ?? initialTicket;
        run.SetComplete(finalTicket);
        await _repository.UpsertRunAsync(run.RunId, finalTicket, run.Status);
        NotifyUpdated();
    }

    private static string FormatPreflightDiagnostic(PipelinePreflightFailure failure)
    {
        var reasons = failure.FailingConnectors
            .Select(r => $"{r.Type}: {r.Message}")
            .ToArray();
        return $"Pre-flight check failed — {string.Join("; ", reasons)}";
    }

    private static TicketState ExtractTicket(KernelProcessProxyMessage message, TicketState fallback)
    {
        if (message.EventData?.ToObject() is TicketState ticket) return ticket;
        return fallback;
    }
}
