// SQLite-backed monitoring heartbeats + close-the-loop dedup (feature 013).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace DBAIAzure.Storage.Repositories;

/// <summary>
/// Persists per-app monitoring heartbeats and raised-issue dedup signatures in the shared SQLite
/// database. Uses a short-lived <c>PipelineDbContext</c> per operation via <c>IDbContextFactory</c>
/// (thread-safe, concurrent-write safe) — the same pattern as the other repositories.
/// </summary>
public sealed class SqliteAppHeartbeatStore : IAppHeartbeatStore
{
    private readonly IDbContextFactory<PipelineDbContext> _factory;

    /// <summary>Creates the store over the shared DbContext factory.</summary>
    public SqliteAppHeartbeatStore(IDbContextFactory<PipelineDbContext> factory) => _factory = factory;

    /// <inheritdoc/>
    public async Task RecordCycleAsync(string appId, bool ok, string? error, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.AppMonitoringHeartbeats.FirstOrDefaultAsync(r => r.AppId == appId, ct);

        if (record is null)
        {
            db.AppMonitoringHeartbeats.Add(new AppMonitoringHeartbeatRecord
            {
                AppId = appId,
                LastCycleAt = DateTimeOffset.UtcNow,
                LastCycleOk = ok,
                LastError = error,
                CycleCount = 1
            });
        }
        else
        {
            record.LastCycleAt = DateTimeOffset.UtcNow;
            record.LastCycleOk = ok;
            record.LastError = error;
            record.CycleCount += 1;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<AppMonitoringHeartbeat?> GetAsync(string appId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.AppMonitoringHeartbeats
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.AppId == appId, ct);

        return record is null
            ? null
            : new AppMonitoringHeartbeat(record.AppId, record.LastCycleAt, record.LastCycleOk, record.LastError, record.CycleCount);
    }

    /// <inheritdoc/>
    public async Task<bool> IsRaisedAsync(string signature, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AppRaisedIssues.AsNoTracking().AnyAsync(r => r.Signature == signature, ct);
    }

    /// <inheritdoc/>
    public async Task RecordRaisedAsync(AppRaisedIssue issue, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Idempotent on the signature: a recurring issue must never create a second row.
        if (await db.AppRaisedIssues.AnyAsync(r => r.Signature == issue.Signature, ct))
            return;

        db.AppRaisedIssues.Add(new AppRaisedIssueRecord
        {
            Signature = issue.Signature,
            AppId = issue.AppId,
            WorkflowRunId = issue.WorkflowRunId,
            CreatedAt = issue.CreatedAt
        });
        await db.SaveChangesAsync(ct);
    }
}
