// Rehydrates persisted Paused visual runs into the in-memory orchestrator on startup.
// With the MAF runtime on, each run is truly resumed from its durable checkpoint (so approving it after a
// restart drives the workflow to completion); without a checkpoint it falls back to reconstituting just the
// approval gate so operators can still approve/reject (spec-019 T032).

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage.Checkpointing;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Background service that runs once at startup: loads all Paused workflow runs and, for each, reloads its
/// definition and latest MAF checkpoint and asks the orchestrator to resume it from that checkpoint. A run
/// without a checkpoint (or when the MAF runtime is off) falls back to reconstituting the approval gate so a
/// reviewer can still approve or reject after a server restart.
/// </summary>
public sealed class WorkflowRunRehydrationService : BackgroundService
{
    private readonly IWorkflowRunRepository _repository;
    private readonly IWorkflowRepository _workflowRepository;
    private readonly WorkflowExecutionOrchestrator _orchestrator;
    private readonly EfCheckpointStore _checkpointStore;
    private readonly ILogger<WorkflowRunRehydrationService> _logger;

    public WorkflowRunRehydrationService(
        IWorkflowRunRepository repository,
        IWorkflowRepository workflowRepository,
        WorkflowExecutionOrchestrator orchestrator,
        EfCheckpointStore checkpointStore,
        ILogger<WorkflowRunRehydrationService> logger)
    {
        _repository         = repository;
        _workflowRepository = workflowRepository;
        _orchestrator       = orchestrator;
        _checkpointStore    = checkpointStore;
        _logger             = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var paused = await _repository.ListByStatusAsync(WorkflowRunStatus.Paused, stoppingToken);
            if (paused.Count == 0)
                return;

            _logger.LogInformation("Rehydrating {Count} paused run(s) from persistence store.", paused.Count);
            foreach (var record in paused)
            {
                await RehydrateOneAsync(record, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rehydrate paused runs — approvals may be unresolvable until next restart.");
        }
    }

    /// <summary>
    /// Resumes one paused run from its checkpoint when both the definition and a checkpoint are available;
    /// otherwise reconstitutes the approval gate only (so approve/reject still resolves the in-memory TCS).
    /// </summary>
    private async Task RehydrateOneAsync(WorkflowRunRecord record, CancellationToken cancellationToken)
    {
        var checkpoint = await _checkpointStore.GetLatestCheckpointAsync(record.RunId, cancellationToken);
        var definition = await _workflowRepository.GetByIdAsync(record.WorkflowId, cancellationToken);

        if (checkpoint is not null && definition is not null)
        {
            _orchestrator.RehydratePausedRun(record, definition, checkpoint);
            return;
        }

        _logger.LogInformation(
            "Paused run {RunId} has no MAF checkpoint or definition — reconstituting the approval gate only.", record.RunId);
        _orchestrator.RehydratePausedRun(record);
    }
}
