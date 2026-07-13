// Startup service that resumes MAF-paused runs after an application restart (spec-019 T032). It enumerates
// intake runs left awaiting-human and phase-handler runs left awaiting-approval, and for each with a durable
// checkpoint asks the owning orchestrator to rehydrate it — so a reviewer can still answer/approve, and the
// run complete, once the app comes back up.
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage.Checkpointing;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Runs once at startup: finds intake runs left <see cref="PipelineRunStatus.AwaitingHuman"/> and
/// phase-handler runs left <see cref="PhaseRunStatus.AwaitingApproval"/> and, for each that has a MAF
/// checkpoint, calls the owning orchestrator's <c>RehydratePausedRun</c> to resume it from that checkpoint.
/// Only active when the MAF runtime is enabled; a run without a checkpoint (e.g. an SK-era pause not yet
/// migrated) is skipped and left to the one-time migration (T033).
/// </summary>
public sealed class PausedRunRehydrationService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IRunRepository _repository;
    private readonly PipelineOrchestrator _orchestrator;
    private readonly IPhaseRunRepository _phaseRepository;
    private readonly PhaseHandlerOrchestrator _phaseOrchestrator;
    private readonly EfCheckpointStore _checkpointStore;
    private readonly bool _mafEnabled;
    private readonly ILogger<PausedRunRehydrationService> _logger;

    public PausedRunRehydrationService(
        IRunRepository repository,
        PipelineOrchestrator orchestrator,
        IPhaseRunRepository phaseRepository,
        PhaseHandlerOrchestrator phaseOrchestrator,
        EfCheckpointStore checkpointStore,
        IConfiguration configuration,
        ILogger<PausedRunRehydrationService> logger)
    {
        _repository = repository;
        _orchestrator = orchestrator;
        _phaseRepository = phaseRepository;
        _phaseOrchestrator = phaseOrchestrator;
        _checkpointStore = checkpointStore;
        _mafEnabled = configuration.GetValue<bool>("Maf:Enabled");
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_mafEnabled)
        {
            return; // production runs on SK until cutover — nothing to rehydrate onto MAF
        }

        try
        {
            var intake = await RehydrateAllPausedAsync(stoppingToken);
            if (intake > 0)
            {
                _logger.LogInformation("Rehydrated {Count} paused intake run(s) from MAF checkpoints.", intake);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rehydrate paused intake runs — they can still be resumed on the next restart.");
        }

        try
        {
            var phase = await RehydrateAllPausedPhaseRunsAsync(stoppingToken);
            if (phase > 0)
            {
                _logger.LogInformation("Rehydrated {Count} paused phase-handler run(s) from MAF checkpoints.", phase);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rehydrate paused phase-handler runs — they can still be resumed on the next restart.");
        }
    }

    /// <summary>
    /// Rehydrates every phase-handler run left <see cref="PhaseRunStatus.AwaitingApproval"/> that has a
    /// checkpoint, returning how many were resumed. The full paused state is recovered from the checkpoint, so
    /// only the run identity (from the persisted view) is needed to seed the placeholder. Public so the wiring
    /// can be exercised in tests without hosting the whole app.
    /// </summary>
    public async Task<int> RehydrateAllPausedPhaseRunsAsync(CancellationToken cancellationToken = default)
    {
        var paused = await _phaseRepository.ListByStatusAsync(PhaseRunStatus.AwaitingApproval, cancellationToken);
        if (paused.Count == 0)
        {
            return 0;
        }

        var count = 0;
        foreach (var view in paused)
        {
            var checkpoint = await _checkpointStore.GetLatestCheckpointAsync(view.RunId, cancellationToken);
            if (checkpoint is null)
            {
                _logger.LogInformation(
                    "Paused phase run {RunId} has no MAF checkpoint — leaving it for the SK-paused migration.", view.RunId);
                continue;
            }

            // Minimal placeholder — the real paused state (artifacts, validation, decision) rides the
            // checkpoint's re-emitted approval request. FeatureDirectory follows the specs/<key> convention.
            var placeholder = new PhaseHandlerState
            {
                RunId = view.RunId,
                FeatureKey = view.FeatureKey,
                FeatureDirectory = $"specs/{view.FeatureKey}",
                Phase = view.Phase,
                Status = PhaseRunStatus.AwaitingApproval,
            };

            _phaseOrchestrator.RehydratePausedRun(placeholder, checkpoint);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Rehydrates every awaiting-human intake run that has a checkpoint, returning how many were resumed.
    /// Public so the wiring can be exercised in tests without hosting the whole app.
    /// </summary>
    public async Task<int> RehydrateAllPausedAsync(CancellationToken cancellationToken = default)
    {
        var paused = await _repository.ListRunsAsync(
            status: PipelineRunStatus.AwaitingHuman, take: int.MaxValue, ct: cancellationToken);
        if (paused.Count == 0)
        {
            return 0;
        }

        var count = 0;
        foreach (var summary in paused)
        {
            var checkpoint = await _checkpointStore.GetLatestCheckpointAsync(summary.RunId, cancellationToken);
            if (checkpoint is null)
            {
                _logger.LogInformation(
                    "Paused run {RunId} has no MAF checkpoint — leaving it for the SK-paused migration.", summary.RunId);
                continue;
            }

            var detail = await _repository.GetRunAsync(summary.RunId, cancellationToken);
            if (detail is null)
            {
                continue;
            }

            var pausedTicket = JsonSerializer.Deserialize<TicketState>(detail.TicketStateJson, JsonOptions);
            if (pausedTicket is null)
            {
                _logger.LogWarning("Paused run {RunId} has no readable ticket state — skipping.", summary.RunId);
                continue;
            }

            _orchestrator.RehydratePausedRun(pausedTicket, checkpoint);
            count++;
        }

        return count;
    }
}
