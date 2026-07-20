// EF Core-backed implementation of IDorWorkflowInstanceStore (spec-021). Uses a DbContext-per-operation from
// IDbContextFactory so it is safe to use from singleton orchestrators/background services.
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace DBAIAzure.Storage.Repositories;

/// <summary>
/// Persists <see cref="DorWorkflowInstance"/> rows. Creation is idempotent (FR-004): a second active instance for
/// the same ticket violates the filtered unique index and is reported as "already active" rather than throwing.
/// </summary>
public sealed class EfDorWorkflowInstanceStore : IDorWorkflowInstanceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<PipelineDbContext> _factory;

    public EfDorWorkflowInstanceStore(IDbContextFactory<PipelineDbContext> factory) => _factory = factory;

    /// <inheritdoc />
    public async Task<bool> TryCreateAsync(DorWorkflowInstance instance, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.DorWorkflowInstances.Add(ToEntity(instance));
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Filtered unique index on TicketKey (active states) rejected the insert — an active instance
            // already exists for this ticket, so the trigger is a duplicate and is discarded (FR-004).
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<DorWorkflowInstance?> GetAsync(string runId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.DorWorkflowInstances.AsNoTracking()
            .FirstOrDefaultAsync(e => e.RunId == runId, ct);
        return entity is null ? null : ToRecord(entity);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(DorWorkflowInstance instance, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.DorWorkflowInstances.FindAsync(new object[] { instance.RunId }, ct);
        if (entity is null)
            return;
        Apply(instance, entity);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DorWorkflowInstance>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var terminal = (int)DorState.Done;
        var entities = await db.DorWorkflowInstances.AsNoTracking()
            .Where(e => e.State != terminal)
            .ToListAsync(ct);
        return entities.Select(ToRecord).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DorWorkflowInstance>> ListDueSlaAsync(
        DateTimeOffset asOf, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var awaiting = (int)DorState.AwaitingResponse;
        var escalated = (int)DorState.Escalated;
        var candidates = await db.DorWorkflowInstances.AsNoTracking()
            .Where(e => (e.State == awaiting || e.State == escalated) && e.SlaDeadlineAt != null)
            .ToListAsync(ct);
        // DateTimeOffset comparison in-process for portability across SQLite (dev) and SQL Server (prod).
        return candidates
            .Where(e => e.SlaDeadlineAt!.Value <= asOf)
            .Select(ToRecord)
            .ToList()
            .AsReadOnly();
    }

    // ── Mapping helpers ─────────────────────────────────────────────────────────

    private static DorWorkflowInstanceEntity ToEntity(DorWorkflowInstance r) => new()
    {
        RunId = r.RunId,
        TicketKey = r.TicketKey,
        State = (int)r.State,
        OutstandingGapsJson = JsonSerializer.Serialize(r.OutstandingGaps, JsonOptions),
        PrimaryIterations = r.PrimaryIterations,
        EscalationIterations = r.EscalationIterations,
        SlaClockStartedAt = r.SlaClockStartedAt,
        SlaDeadlineAt = r.SlaDeadlineAt,
        SlaTier = (int)r.SlaTier,
        ActiveChannelId = r.ActiveChannelId,
        ThreadRef = r.ThreadRef,
        LastSeenReplyRef = r.LastSeenReplyRef,
        IsDryRun = r.IsDryRun,
        Outcome = r.Outcome is { } o ? (int)o : null,
        StartedAt = r.StartedAt,
        UpdatedAt = r.UpdatedAt,
        CompletedAt = r.CompletedAt,
        FailureReason = r.FailureReason,
    };

    private static void Apply(DorWorkflowInstance r, DorWorkflowInstanceEntity e)
    {
        e.State = (int)r.State;
        e.OutstandingGapsJson = JsonSerializer.Serialize(r.OutstandingGaps, JsonOptions);
        e.PrimaryIterations = r.PrimaryIterations;
        e.EscalationIterations = r.EscalationIterations;
        e.SlaClockStartedAt = r.SlaClockStartedAt;
        e.SlaDeadlineAt = r.SlaDeadlineAt;
        e.SlaTier = (int)r.SlaTier;
        e.ActiveChannelId = r.ActiveChannelId;
        e.ThreadRef = r.ThreadRef;
        e.LastSeenReplyRef = r.LastSeenReplyRef;
        e.IsDryRun = r.IsDryRun;
        e.Outcome = r.Outcome is { } o ? (int)o : null;
        e.UpdatedAt = r.UpdatedAt;
        e.CompletedAt = r.CompletedAt;
        e.FailureReason = r.FailureReason;
    }

    private static DorWorkflowInstance ToRecord(DorWorkflowInstanceEntity e) => new()
    {
        RunId = e.RunId,
        TicketKey = e.TicketKey,
        State = (DorState)e.State,
        OutstandingGaps = JsonSerializer.Deserialize<List<string>>(e.OutstandingGapsJson, JsonOptions)
                          ?? new List<string>(),
        PrimaryIterations = e.PrimaryIterations,
        EscalationIterations = e.EscalationIterations,
        SlaClockStartedAt = e.SlaClockStartedAt,
        SlaDeadlineAt = e.SlaDeadlineAt,
        SlaTier = (SlaTier)e.SlaTier,
        ActiveChannelId = e.ActiveChannelId,
        ThreadRef = e.ThreadRef,
        LastSeenReplyRef = e.LastSeenReplyRef,
        IsDryRun = e.IsDryRun,
        Outcome = e.Outcome is { } o ? (DorOutcome)o : null,
        StartedAt = e.StartedAt,
        UpdatedAt = e.UpdatedAt,
        CompletedAt = e.CompletedAt,
        FailureReason = e.FailureReason,
    };
}
