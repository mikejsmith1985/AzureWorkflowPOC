using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DBAIAzure.Storage.Repositories;

/// <summary>
/// SQLite-backed implementation of IRunRepository.
/// Uses a DbContext factory so each operation gets its own short-lived context
/// — safe to call from concurrent background tasks.
/// </summary>
public sealed class SqliteRunRepository : IRunRepository
{
    private readonly IDbContextFactory<PipelineDbContext> _factory;
    private static readonly JsonSerializerOptions SerializerOpts = new() { WriteIndented = true };

    public SqliteRunRepository(IDbContextFactory<PipelineDbContext> factory)
    {
        _factory = factory;
    }

    public async Task UpsertRunAsync(
        string runId,
        TicketState ticket,
        PipelineRunStatus status,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var existing = await db.Runs.FindAsync([runId], ct);
        var ticketJson = JsonSerializer.Serialize(ticket, SerializerOpts);

        if (existing is null)
        {
            db.Runs.Add(new RunRecord
            {
                RunId          = runId,
                TicketId       = ticket.TicketId,
                Title          = ticket.Title,
                Source         = ticket.Source,
                SnowNumber     = ticket.SnowNumber,
                SnowPriority   = ticket.SnowPriority,
                SnowCategory   = ticket.SnowCategory,
                Status         = status.ToString(),
                StoryPoints    = ticket.StoryPoints,
                JiraIssueUrl   = ticket.JiraIssueUrl,
                TicketStateJson = ticketJson,
                StartedAt      = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.Status          = status.ToString();
            existing.Title           = ticket.Title;
            existing.StoryPoints     = ticket.StoryPoints;
            existing.JiraIssueUrl    = ticket.JiraIssueUrl;
            existing.TicketStateJson = ticketJson;

            if (status is PipelineRunStatus.Complete or PipelineRunStatus.Blocked or PipelineRunStatus.Failed)
                existing.CompletedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task AddSnapshotAsync(
        string runId,
        string stepName,
        TicketState before,
        TicketState after,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        db.Snapshots.Add(new StepSnapshotRecord
        {
            RunId      = runId,
            StepName   = stepName,
            InputJson  = JsonSerializer.Serialize(before, SerializerOpts),
            OutputJson = JsonSerializer.Serialize(after, SerializerOpts),
            Timestamp  = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PersistedRunSummary>> ListRunsAsync(
        string? search = null,
        PipelineRunStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var query = db.Runs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var lower = search.ToLower();
            query = query.Where(r =>
                r.Title.ToLower().Contains(lower) ||
                r.TicketId.ToLower().Contains(lower) ||
                (r.SnowNumber != null && r.SnowNumber.ToLower().Contains(lower)));
        }

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value.ToString());

        // Sort client-side: SQLite EF Core 8 does not translate DateTimeOffset ordering.
        var allRows = await query.ToListAsync(ct);
        var rows = allRows
            .OrderByDescending(r => r.StartedAt)
            .Skip(skip)
            .Take(take)
            .ToList();

        return rows.Select(r => new PersistedRunSummary(
            r.RunId, r.TicketId, r.Title, r.Source,
            r.SnowNumber, r.SnowPriority,
            Enum.Parse<PipelineRunStatus>(r.Status),
            r.StoryPoints, r.JiraIssueUrl,
            r.StartedAt, r.CompletedAt)).ToList();
    }

    public async Task<PersistedRunDetail?> GetRunAsync(string runId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var record = await db.Runs
            .AsNoTracking()
            .Include(r => r.Snapshots)
            .FirstOrDefaultAsync(r => r.RunId == runId, ct);

        if (record is null) return null;

        var summary = new PersistedRunSummary(
            record.RunId, record.TicketId, record.Title, record.Source,
            record.SnowNumber, record.SnowPriority,
            Enum.Parse<PipelineRunStatus>(record.Status),
            record.StoryPoints, record.JiraIssueUrl,
            record.StartedAt, record.CompletedAt);

        var snapshots = record.Snapshots
            .OrderBy(s => s.Timestamp.UtcTicks)
            .Select(s => new PersistedSnapshot(s.StepName, s.InputJson, s.OutputJson, s.Timestamp))
            .ToList();

        return new PersistedRunDetail(summary, record.TicketStateJson, snapshots);
    }
}
