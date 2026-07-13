// One-time, idempotent migration of SK-paused runs onto MAF checkpoints (spec-019 T033/FR-006a). For each
// run paused under the retired SK Process Framework, it reconstructs an equivalent MAF checkpoint at the
// human-in-the-loop suspension so the run resumes in place — no approval or clarification is lost at cutover.
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DBAIAzure.Storage.Checkpointing;

/// <summary>The outcome of migrating one paused run.</summary>
public enum SkPausedRunMigrationOutcome
{
    /// <summary>A MAF checkpoint was created at the run's suspension point.</summary>
    Migrated,

    /// <summary>The run already had a checkpoint — skipped (idempotent re-run).</summary>
    AlreadyMigrated,

    /// <summary>The resume workflow did not suspend, or the migration failed — the run was not converted.</summary>
    Failed,
}

/// <summary>The result of migrating one paused run: its outcome and, when migrated, the pause checkpoint.</summary>
/// <param name="Outcome">What happened to the run.</param>
/// <param name="Checkpoint">The checkpoint written at the suspension (resume from here), or null.</param>
public sealed record SkPausedRunMigrationResult(SkPausedRunMigrationOutcome Outcome, CheckpointInfo? Checkpoint);

/// <summary>
/// Migrates SK-paused runs onto durable MAF checkpoints. The caller supplies, per run, a <em>resume</em>
/// workflow (one that forwards the paused state straight to its HITL <see cref="RequestPort"/>) and the
/// paused state; this service runs it with checkpointing so a checkpoint is written at the suspension. It
/// is <b>idempotent</b> — a run that already has a checkpoint is skipped — so it is safe to run at every
/// deploy and only converts records once (FR-006a). Verify against representative paused records (SC-009).
/// </summary>
public sealed class SkPausedRunMigration
{
    private readonly IDbContextFactory<PipelineDbContext> _contextFactory;
    private readonly CheckpointManager _checkpointManager;
    private readonly ILogger<SkPausedRunMigration> _logger;

    /// <summary>Creates the migration over the checkpoint database and the JSON checkpoint manager.</summary>
    public SkPausedRunMigration(
        IDbContextFactory<PipelineDbContext> contextFactory,
        CheckpointManager checkpointManager,
        ILogger<SkPausedRunMigration> logger)
    {
        _contextFactory = contextFactory;
        _checkpointManager = checkpointManager;
        _logger = logger;
    }

    /// <summary>
    /// Migrates one paused run: skips it when a checkpoint already exists, otherwise runs
    /// <paramref name="resumeWorkflow"/> with <paramref name="seedState"/> under <paramref name="sessionId"/>
    /// (the run id) until it suspends, which writes the checkpoint. Never throws — a failed record is
    /// reported so the batch continues.
    /// </summary>
    public async Task<SkPausedRunMigrationResult> MigrateAsync<TState>(
        string sessionId, Workflow resumeWorkflow, TState seedState, CancellationToken cancellationToken = default)
        where TState : notnull
    {
        try
        {
            await using (var db = await _contextFactory.CreateDbContextAsync(cancellationToken))
            {
                var alreadyCheckpointed = await db.WorkflowCheckpoints
                    .AnyAsync(checkpoint => checkpoint.SessionId == sessionId, cancellationToken);
                if (alreadyCheckpointed)
                {
                    _logger.LogInformation("Paused run {SessionId} already has a MAF checkpoint — skipping.", sessionId);
                    return new SkPausedRunMigrationResult(SkPausedRunMigrationOutcome.AlreadyMigrated, Checkpoint: null);
                }
            }

            // Run the resume workflow to its human-in-the-loop suspension; the manager persists a checkpoint.
            var session = await MafWorkflowSession<TState>.StartAsync(
                resumeWorkflow, seedState, sessionId, _checkpointManager, cancellationToken);
            var segment = await session.DriveAsync(cancellationToken);

            if (!segment.Suspended)
            {
                _logger.LogWarning(
                    "Paused run {SessionId} did not suspend at a HITL gate during migration — not converted.", sessionId);
                return new SkPausedRunMigrationResult(SkPausedRunMigrationOutcome.Failed, Checkpoint: null);
            }

            _logger.LogInformation("Migrated paused run {SessionId} to a MAF checkpoint.", sessionId);
            return new SkPausedRunMigrationResult(SkPausedRunMigrationOutcome.Migrated, session.LastCheckpoint);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to migrate paused run {SessionId} — it was left unconverted.", sessionId);
            return new SkPausedRunMigrationResult(SkPausedRunMigrationOutcome.Failed, Checkpoint: null);
        }
    }
}
