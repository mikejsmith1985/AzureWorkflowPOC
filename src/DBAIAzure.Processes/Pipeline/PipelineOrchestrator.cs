using DBAIAzure.Core.Models;
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
    private readonly ConcurrentDictionary<string, PipelineRun> _runs = new();

    /// <summary>Fired on a background thread whenever a run's state or events change.</summary>
    public event Action<string>? RunUpdated;

    public PipelineOrchestrator(Func<IProgressReporter, Kernel> kernelFactory)
    {
        _kernelFactory = kernelFactory;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Enqueue a new ticket and return its run ID immediately.</summary>
    public string StartRun(TicketState ticket)
    {
        var runId = Guid.NewGuid().ToString("N")[..8];
        var run = new PipelineRun(runId, ticket);
        _runs[runId] = run;
        _ = Task.Run(() => ExecuteRunAsync(run, ticket));
        return runId;
    }

    public PipelineRun? GetRun(string runId) =>
        _runs.GetValueOrDefault(runId);

    public IReadOnlyList<PipelineRun> GetAllRuns() =>
        [.. _runs.Values.OrderByDescending(r => r.StartedAt)];

    /// <summary>Submit the PO's answer and unblock the waiting background task.</summary>
    public void SubmitHitlAnswer(string runId, string answer)
    {
        if (_runs.TryGetValue(runId, out var run))
            run.ProvideHitlInput(answer);
    }

    // ── Background execution loop ──────────────────────────────────────────────

    private async Task ExecuteRunAsync(PipelineRun run, TicketState initialTicket)
    {
        try
        {
            var currentTicket = initialTicket;

            for (int clarificationRound = 0; clarificationRound <= MaxClarificationRounds; clarificationRound++)
            {
                var reporter = new BoundProgressReporter(run, () => RunUpdated?.Invoke(run.RunId));
                var kernel = _kernelFactory(reporter);
                var hitlChannel = new HitlExternalChannel();
                var process = IntakePipelineBuilder.Build();

                var startEvent = clarificationRound == 0
                    ? new KernelProcessEvent { Id = Events.TicketReceived, Data = currentTicket }
                    : new KernelProcessEvent { Id = Events.HumanResponded, Data = currentTicket };

                await LocalKernelProcessFactory.RunToEndAsync(
                    process, kernel, startEvent, TimeSpan.FromSeconds(120), hitlChannel);

                if (!hitlChannel.WasPaused)
                {
                    var finalTicket = reporter.FinalTicket ?? currentTicket;
                    run.SetComplete(finalTicket);
                    RunUpdated?.Invoke(run.RunId);
                    break;
                }

                var pausedTicket = ExtractTicketFromProxyMessage(hitlChannel.PausedMessage!, currentTicket);
                run.SetAwaitingHuman(pausedTicket);
                RunUpdated?.Invoke(run.RunId);

                var answer = await run.WaitForHitlInputAsync();

                currentTicket = pausedTicket with
                {
                    HumanAnswer = answer,
                    ClarificationRound = pausedTicket.ClarificationRound + 1,
                };

                run.SetRunning();
                run.AddEvent(new PipelineEvent(
                    "HitlResume",
                    $"PO answered (round {currentTicket.ClarificationRound}) — re-validating",
                    ReportLevel.Info,
                    DateTimeOffset.UtcNow));
                RunUpdated?.Invoke(run.RunId);
            }
        }
        catch (Exception ex)
        {
            run.SetFailed(ex.Message);
            RunUpdated?.Invoke(run.RunId);
        }
    }

    private static TicketState ExtractTicketFromProxyMessage(
        KernelProcessProxyMessage message, TicketState fallback)
    {
        if (message.EventData?.ToObject() is TicketState ticket)
            return ticket;
        return fallback;
    }
}
