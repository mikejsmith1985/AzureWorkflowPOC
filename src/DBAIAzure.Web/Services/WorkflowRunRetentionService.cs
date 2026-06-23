// Background service that purges old terminal workflow runs on a daily schedule (FR-18.4).

using DBAIAzure.Core.Interfaces;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Runs once per day and calls <see cref="IWorkflowRunRepository.PurgeTerminalRunsOlderThanAsync"/>
/// to remove Completed, Failed, TimedOut, and Cancelled runs that exceed the configured TTL.
/// Paused and Running runs are never purged regardless of age.
/// </summary>
public sealed class WorkflowRunRetentionService : BackgroundService
{
    private readonly IWorkflowRunRepository _repository;
    private readonly IConfiguration _config;
    private readonly ILogger<WorkflowRunRetentionService> _logger;

    public WorkflowRunRetentionService(
        IWorkflowRunRepository repository,
        IConfiguration config,
        ILogger<WorkflowRunRetentionService> logger)
    {
        _repository = repository;
        _config     = config;
        _logger     = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait 30 seconds on startup to let the application fully initialise before the first purge.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var retentionDays = _config.GetValue<int>("RetentionDays", 30);
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(retentionDays);

            try
            {
                await _repository.PurgeTerminalRunsOlderThanAsync(cutoff, stoppingToken);
                _logger.LogDebug("Workflow run retention: purged terminal runs older than {Cutoff}", cutoff);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Workflow run retention job failed — will retry in 24 hours");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
