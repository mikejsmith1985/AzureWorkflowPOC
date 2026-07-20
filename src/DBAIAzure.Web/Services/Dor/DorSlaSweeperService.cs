// Background service that enforces DoR SLAs (spec-021 US3). It polls the durable instance store for
// awaiting-human runs whose SLA deadline has elapsed and drives the next tier: a primary breach escalates; an
// escalation breach ends the run with a manual handoff. Durable — the deadline lives on the persisted instance,
// so a breach is detected correctly even across restarts.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Processes.Pipeline;

namespace DBAIAzure.Web.Services.Dor;

/// <summary>
/// Sweeps for SLA breaches on a coarse interval (DoR SLAs are measured in hours). For each due instance it asks
/// the orchestrator to escalate (primary tier) or manually exit (escalation tier). In-process de-duplication
/// prevents driving the same breach twice before the run's state advances.
/// </summary>
public sealed class DorSlaSweeperService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(60);

    private readonly IDorWorkflowInstanceStore _instanceStore;
    private readonly DorWorkflowOrchestrator _orchestrator;
    private readonly ILogger<DorSlaSweeperService> _logger;

    private readonly HashSet<string> _escalated = new();
    private readonly HashSet<string> _manualExited = new();

    public DorSlaSweeperService(
        IDorWorkflowInstanceStore instanceStore, DorWorkflowOrchestrator orchestrator, ILogger<DorSlaSweeperService> logger)
    {
        _instanceStore = instanceStore;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "DoR SLA sweep cycle failed; retrying next interval."); }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>
    /// Runs one sweep: escalate primary-tier breaches and manually exit escalation-tier breaches. Returns the
    /// number of runs acted on. Public so the sweeper can be exercised in tests.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var due = await _instanceStore.ListDueSlaAsync(DateTimeOffset.UtcNow, cancellationToken);
        var acted = 0;

        foreach (var instance in due)
        {
            if (instance.SlaTier == SlaTier.Primary)
            {
                if (_escalated.Add(instance.RunId))
                {
                    _orchestrator.SubmitEscalation(instance.RunId);
                    acted++;
                }
            }
            else if (_manualExited.Add(instance.RunId))
            {
                _orchestrator.SubmitManualExit(instance.RunId, "Escalation SLA breached without resolution.");
                acted++;
            }
        }

        return acted;
    }
}
