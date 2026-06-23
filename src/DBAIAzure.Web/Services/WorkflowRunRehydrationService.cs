// Rehydrates persisted Paused runs into the in-memory orchestrator on startup.
// Without this, a server restart would leave approvals stuck with no TCS to resolve.

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Background service that runs once at startup: loads all Paused workflow runs from
/// the repository and calls <see cref="IWorkflowExecutionOrchestrator.RehydrateAsync"/>
/// so that operators can still approve or reject them after a server restart.
/// </summary>
public sealed class WorkflowRunRehydrationService : BackgroundService
{
    private readonly IWorkflowRunRepository _repository;
    private readonly IWorkflowExecutionOrchestrator _orchestrator;
    private readonly ILogger<WorkflowRunRehydrationService> _logger;

    public WorkflowRunRehydrationService(
        IWorkflowRunRepository repository,
        IWorkflowExecutionOrchestrator orchestrator,
        ILogger<WorkflowRunRehydrationService> logger)
    {
        _repository   = repository;
        _orchestrator = orchestrator;
        _logger       = logger;
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
                _orchestrator.RehydratePausedRun(record);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rehydrate paused runs — approvals may be unresolvable until next restart.");
        }
    }
}
