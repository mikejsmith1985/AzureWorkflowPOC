// Startup service that resumes paused DoR conversations after an application restart (spec-021 SC-003/FR-010).
// Mirrors PausedRunRehydrationService: for each non-terminal instance awaiting a human, it resumes the MAF run
// from its latest checkpoint so the (possibly hours-later) reply is still processed.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage.Checkpointing;

namespace DBAIAzure.Web.Services.Dor;

/// <summary>
/// Runs once at startup: finds DoR instances left <see cref="DorState.AwaitingResponse"/> or
/// <see cref="DorState.Escalated"/> and, for each with a MAF checkpoint, asks the orchestrator to resume it from
/// that checkpoint. A run without a checkpoint is skipped (its conversation cannot be safely resumed).
/// </summary>
public sealed class DorRunRehydrationService : BackgroundService
{
    private readonly IDorWorkflowInstanceStore _instanceStore;
    private readonly DorWorkflowOrchestrator _orchestrator;
    private readonly EfCheckpointStore _checkpointStore;
    private readonly ILogger<DorRunRehydrationService> _logger;

    public DorRunRehydrationService(
        IDorWorkflowInstanceStore instanceStore,
        DorWorkflowOrchestrator orchestrator,
        EfCheckpointStore checkpointStore,
        ILogger<DorRunRehydrationService> logger)
    {
        _instanceStore = instanceStore;
        _orchestrator = orchestrator;
        _checkpointStore = checkpointStore;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var count = await RehydrateAllAsync(stoppingToken);
            if (count > 0)
                _logger.LogInformation("Rehydrated {Count} paused DoR conversation(s) from MAF checkpoints.", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rehydrate paused DoR conversations — they can resume on the next restart.");
        }
    }

    /// <summary>
    /// Resumes every awaiting-human DoR instance that has a checkpoint; returns how many were resumed. Public so
    /// the wiring can be exercised in tests without hosting the whole app.
    /// </summary>
    public async Task<int> RehydrateAllAsync(CancellationToken cancellationToken = default)
    {
        var active = await _instanceStore.ListActiveAsync(cancellationToken);
        var count = 0;

        foreach (var instance in active)
        {
            if (instance.State is not (DorState.AwaitingResponse or DorState.Escalated))
                continue;

            var checkpoint = await _checkpointStore.GetLatestCheckpointAsync(instance.RunId, cancellationToken);
            if (checkpoint is null)
            {
                _logger.LogWarning("Paused DoR run {RunId} has no checkpoint — cannot resume its conversation.", instance.RunId);
                continue;
            }

            await _orchestrator.RehydrateAsync(instance, checkpoint, cancellationToken);
            count++;
        }

        return count;
    }
}
