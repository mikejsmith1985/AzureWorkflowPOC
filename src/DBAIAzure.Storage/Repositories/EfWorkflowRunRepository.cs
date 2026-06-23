// EF Core-backed implementation of IWorkflowRunRepository (FR-18, US1).

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace DBAIAzure.Storage.Repositories;

/// <summary>
/// Persists workflow builder run records using a scoped <see cref="PipelineDbContext"/>
/// obtained from <see cref="IDbContextFactory{TContext}"/> for each operation.
/// This ensures the repository is safe to use from singleton orchestrators without
/// capturing a scoped DbContext (captive-dependency protection).
/// </summary>
public sealed class EfWorkflowRunRepository : IWorkflowRunRepository
{
    private readonly IDbContextFactory<PipelineDbContext> _factory;

    public EfWorkflowRunRepository(IDbContextFactory<PipelineDbContext> factory)
        => _factory = factory;

    /// <inheritdoc />
    public async Task CreateAsync(WorkflowRunRecord run, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.WorkflowBuilderRuns.Add(ToEntity(run));
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(WorkflowRunRecord run, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.WorkflowBuilderRuns.FindAsync(new object[] { run.RunId }, ct);
        if (entity is null)
            return;

        Apply(run, entity);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<WorkflowRunRecord?> GetAsync(string runId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.WorkflowBuilderRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RunId == runId, ct);
        return entity is null ? null : ToRecord(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowRunRecord>> ListByStatusAsync(
        WorkflowRunStatus status,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var statusInt = (int)status;
        // Ordering on DateTimeOffset is done in-process so that the query is portable across
        // both Azure SQL (production) and SQLite (integration tests).
        var entities = await db.WorkflowBuilderRuns
            .AsNoTracking()
            .Where(r => r.Status == statusInt)
            .ToListAsync(ct);
        return entities
            .OrderByDescending(r => r.StartedAt)
            .Select(ToRecord)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowRunRecord>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entities = await db.WorkflowBuilderRuns
            .AsNoTracking()
            .ToListAsync(ct);
        return entities
            .OrderByDescending(r => r.StartedAt)
            .Select(ToRecord)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public async Task PurgeTerminalRunsOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var terminalStatuses = new[]
        {
            (int)WorkflowRunStatus.Completed,
            (int)WorkflowRunStatus.Failed,
            (int)WorkflowRunStatus.TimedOut,
            (int)WorkflowRunStatus.Cancelled,
        };

        // DateTimeOffset comparison is evaluated in-process (portable across Azure SQL and SQLite).
        var terminal = await db.WorkflowBuilderRuns
            .Where(r => terminalStatuses.Contains(r.Status))
            .ToListAsync(ct);

        var stale = terminal
            .Where(r => r.CompletedAt.HasValue && r.CompletedAt.Value < cutoff)
            .ToList();

        if (stale.Count == 0)
            return;

        db.WorkflowBuilderRuns.RemoveRange(stale);
        await db.SaveChangesAsync(ct);
    }

    // ── Mapping helpers ─────────────────────────────────────────────────────────

    private static WorkflowRunEntity ToEntity(WorkflowRunRecord r) => new()
    {
        RunId        = r.RunId,
        WorkflowId   = r.WorkflowId,
        WorkflowName = r.WorkflowName,
        Status       = (int)r.Status,
        TriggeredBy  = r.TriggeredBy,
        StartedAt    = r.StartedAt,
        SuspendedAt  = r.SuspendedAt,
        ResumedAt    = r.ResumedAt,
        CompletedAt  = r.CompletedAt,
        FailureReason = r.FailureReason,
    };

    private static void Apply(WorkflowRunRecord r, WorkflowRunEntity entity)
    {
        entity.Status        = (int)r.Status;
        entity.SuspendedAt   = r.SuspendedAt;
        entity.ResumedAt     = r.ResumedAt;
        entity.CompletedAt   = r.CompletedAt;
        entity.FailureReason = r.FailureReason;
    }

    private static WorkflowRunRecord ToRecord(WorkflowRunEntity e) => new(
        RunId:         e.RunId,
        WorkflowId:    e.WorkflowId,
        WorkflowName:  e.WorkflowName,
        Status:        (WorkflowRunStatus)e.Status,
        TriggeredBy:   e.TriggeredBy,
        StartedAt:     e.StartedAt,
        SuspendedAt:   e.SuspendedAt,
        ResumedAt:     e.ResumedAt,
        CompletedAt:   e.CompletedAt,
        FailureReason: e.FailureReason
    );
}
