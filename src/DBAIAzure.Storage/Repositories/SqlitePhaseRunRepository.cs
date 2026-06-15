// SQLite-backed phase-run persistence; one short-lived DbContext per call so it is singleton-safe.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DBAIAzure.Storage.Repositories;

/// <summary>
/// SQLite-backed implementation of <see cref="IPhaseRunRepository"/>. Uses a DbContext factory so
/// each operation gets its own short-lived context — safe to call from concurrent background tasks
/// and from a singleton registration, exactly like <c>SqliteRunRepository</c>.
/// </summary>
public sealed class SqlitePhaseRunRepository : IPhaseRunRepository
{
    private readonly IDbContextFactory<PipelineDbContext> _factory;
    private static readonly JsonSerializerOptions SerializerOpts = new() { WriteIndented = false };

    /// <summary>Statuses considered terminal — when reached, the completion timestamp is stamped.</summary>
    private static readonly PhaseRunStatus[] TerminalStatuses =
    [
        PhaseRunStatus.Completed,
        PhaseRunStatus.Rejected,
        PhaseRunStatus.Unsupported,
        PhaseRunStatus.Failed,
    ];

    public SqlitePhaseRunRepository(IDbContextFactory<PipelineDbContext> factory)
    {
        _factory = factory;
    }

    public async Task UpsertRunAsync(PhaseHandlerState state, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);

        // Idempotency is keyed by (FeatureKey, Phase): a repeat signal arrives under a NEW RunId but
        // must update the single existing record for that feature/phase, not insert a second row that
        // would violate the unique (FeatureKey, Phase) index (FR-013). We therefore reconcile by the
        // idempotency key first, falling back to the RunId for the first write of a run.
        var phaseName = state.Phase.ToString();
        var existing =
            await db.PhaseRuns.FirstOrDefaultAsync(
                r => r.FeatureKey == state.FeatureKey && r.Phase == phaseName, cancellationToken)
            ?? await db.PhaseRuns.FindAsync([state.RunId], cancellationToken);

        // When found by FeatureKey+Phase with a stale RunId (repeat signal), replace the row so the
        // new RunId is the durable anchor — GetByRunIdAsync(newRunId) must resolve for the portal URL.
        // Capture the prior work-item ids first; they survive into the new row while this repeat run
        // is still in progress and has not yet emitted its own items (FR-013 idempotency anchor).
        string? inheritedWorkItemsJson = null;
        if (existing is not null && existing.RunId != state.RunId)
        {
            inheritedWorkItemsJson = existing.WorkItemIdsJson;
            db.PhaseRuns.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            existing = null;
        }

        var gapsJson      = JsonSerializer.Serialize(state.Validation?.Gaps ?? [], SerializerOpts);
        var decisionJson  = state.Decision is null ? null : JsonSerializer.Serialize(state.Decision, SerializerOpts);
        var workItemsJson = JsonSerializer.Serialize(state.CreatedWorkItems, SerializerOpts);
        var hasWorkItems  = state.CreatedWorkItems.Count > 0;
        var isTerminal    = TerminalStatuses.Contains(state.Status);

        // Effective work-item ids: the new run's items when present; otherwise any ids carried over
        // from the prior row (the repeat signal's in-progress writes will update this later).
        var effectiveWorkItemsJson = hasWorkItems ? workItemsJson : (inheritedWorkItemsJson ?? workItemsJson);

        if (existing is null)
        {
            db.PhaseRuns.Add(new PhaseRunRecord
            {
                RunId           = state.RunId,
                FeatureKey      = state.FeatureKey,
                Phase           = phaseName,
                Status          = state.Status.ToString(),
                Summary         = state.Validation?.Summary,
                GapsJson        = gapsJson,
                DecisionJson    = decisionJson,
                WorkItemIdsJson = effectiveWorkItemsJson,
                FailureReason   = state.FailureReason,
                StartedAt       = DateTimeOffset.UtcNow,
                CompletedAt     = isTerminal ? DateTimeOffset.UtcNow : null,
            });
        }
        else
        {
            // Keep the single (FeatureKey, Phase) row. RunId is the primary key and must not change;
            // the original run's id remains the durable anchor, while the latest re-validation's
            // fields (status, summary, decision, work item ids) overwrite the prior values.
            existing.FeatureKey      = state.FeatureKey;
            existing.Phase           = phaseName;
            existing.Status          = state.Status.ToString();
            existing.Summary         = state.Validation?.Summary;
            existing.GapsJson        = gapsJson;
            existing.DecisionJson    = decisionJson;
            // Use the same effective-ids logic as the insert path: new items when present, otherwise
            // preserve the prior ids so the idempotency anchor survives until the create step (FR-013).
            existing.WorkItemIdsJson = effectiveWorkItemsJson;
            existing.FailureReason   = state.FailureReason;
            if (isTerminal) existing.CompletedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PhaseRunRecordView?> GetByRunIdAsync(string runId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var record = await db.PhaseRuns.AsNoTracking().FirstOrDefaultAsync(r => r.RunId == runId, cancellationToken);
        return record is null ? null : ToView(record);
    }

    public async Task<PhaseRunRecordView?> GetByFeaturePhaseAsync(
        string featureKey, SpecKitPhase phase, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var phaseName = phase.ToString();
        var record = await db.PhaseRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.FeatureKey == featureKey && r.Phase == phaseName, cancellationToken);
        return record is null ? null : ToView(record);
    }

    /// <summary>Projects the EF entity into the Core read-model, deserialising the stored work items.</summary>
    private static PhaseRunRecordView ToView(PhaseRunRecord record)
    {
        var createdWorkItems = string.IsNullOrWhiteSpace(record.WorkItemIdsJson)
            ? []
            : JsonSerializer.Deserialize<List<CreatedWorkItemRef>>(record.WorkItemIdsJson, SerializerOpts) ?? [];

        return new PhaseRunRecordView
        {
            RunId            = record.RunId,
            FeatureKey       = record.FeatureKey,
            Phase            = Enum.Parse<SpecKitPhase>(record.Phase),
            Status           = Enum.Parse<PhaseRunStatus>(record.Status),
            CreatedWorkItems = createdWorkItems,
        };
    }
}
