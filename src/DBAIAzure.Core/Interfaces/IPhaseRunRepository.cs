// Persistence seam for phase-handler runs — durable audit + the idempotency anchor for upserts.
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Durable per-run audit store for the phase handler. Backs auditability (FR-011) and the
/// idempotent-upsert lookup (FR-013): the stored created-work-item reference keyed by
/// (FeatureKey, Phase) tells the step whether to create or upsert. Implemented by
/// SqlitePhaseRunRepository; a no-op null implementation is provided for tests/console use.
/// </summary>
public interface IPhaseRunRepository
{
    /// <summary>Inserts or updates the run record for the given state (keyed by RunId).</summary>
    Task UpsertRunAsync(PhaseHandlerState state, CancellationToken cancellationToken = default);

    /// <summary>Returns the persisted run for a RunId, or null if none exists.</summary>
    Task<PhaseRunRecordView?> GetByRunIdAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the persisted run for a (FeatureKey, Phase) pair, or null. Used to find an
    /// already-created work item so a repeat signal upserts instead of duplicating (FR-013).
    /// </summary>
    Task<PhaseRunRecordView?> GetByFeaturePhaseAsync(
        string featureKey, SpecKitPhase phase, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-model projection of a persisted phase run. Decouples the Core/Processes layers from the
/// EF Core entity that lives in the Storage project.
/// </summary>
public record PhaseRunRecordView
{
    /// <summary>The run identifier (primary key).</summary>
    public required string RunId { get; init; }

    /// <summary>Feature slug — idempotency key part 1.</summary>
    public required string FeatureKey { get; init; }

    /// <summary>Completed phase — idempotency key part 2.</summary>
    public required SpecKitPhase Phase { get; init; }

    /// <summary>Lifecycle status at the time of read.</summary>
    public required PhaseRunStatus Status { get; init; }

    /// <summary>Work items previously created/upserted for this (feature, phase); used for upsert lookup.</summary>
    public IReadOnlyList<CreatedWorkItemRef> CreatedWorkItems { get; init; } = [];
}

/// <summary>
/// No-op implementation used when persistence is not configured (e.g. unit tests). Keeps the
/// orchestrator runnable without a database, mirroring the ticket pipeline's NullRunRepository.
/// </summary>
public sealed class NullPhaseRunRepository : IPhaseRunRepository
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NullPhaseRunRepository Instance = new();

    public Task UpsertRunAsync(PhaseHandlerState state, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<PhaseRunRecordView?> GetByRunIdAsync(string runId, CancellationToken cancellationToken = default)
        => Task.FromResult<PhaseRunRecordView?>(null);

    public Task<PhaseRunRecordView?> GetByFeaturePhaseAsync(
        string featureKey, SpecKitPhase phase, CancellationToken cancellationToken = default)
        => Task.FromResult<PhaseRunRecordView?>(null);
}
