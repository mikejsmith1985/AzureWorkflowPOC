// EF-backed MAF checkpoint store (spec-019 T030) — persists workflow checkpoints so a run paused at a
// human-in-the-loop gate resumes in place after an application restart (FR-006). Patterned after the
// shipped FileSystemJsonCheckpointStore, but over the pipeline database (SQLite dev / SQL Server).
using System.Text.Json;
using DBAIAzure.Storage.Entities;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.EntityFrameworkCore;

namespace DBAIAzure.Storage.Checkpointing;

/// <summary>
/// A durable <see cref="JsonCheckpointStore"/> over the pipeline database. Each checkpoint is one
/// <see cref="WorkflowCheckpointEntity"/> row keyed by (session, checkpoint id), linked to its parent so
/// the framework can walk a run's checkpoint tree. Build the manager with
/// <c>CheckpointManager.CreateJson(store, options)</c> and pass it to
/// <c>InProcessExecution.RunStreamingAsync</c> / <c>ResumeStreamingAsync</c>.
/// </summary>
public sealed class EfCheckpointStore : JsonCheckpointStore
{
    private readonly IDbContextFactory<PipelineDbContext> _contextFactory;

    /// <summary>Creates the store over the pipeline DbContext factory (singleton-safe).</summary>
    public EfCheckpointStore(IDbContextFactory<PipelineDbContext> contextFactory)
        => _contextFactory = contextFactory;

    /// <inheritdoc />
    public override async ValueTask<CheckpointInfo> CreateCheckpointAsync(
        string sessionId, JsonElement value, CheckpointInfo? parent)
    {
        var checkpointId = Guid.NewGuid().ToString("N");

        await using var db = await _contextFactory.CreateDbContextAsync();
        db.WorkflowCheckpoints.Add(new WorkflowCheckpointEntity
        {
            SessionId = sessionId,
            CheckpointId = checkpointId,
            ParentCheckpointId = parent?.CheckpointId,
            Payload = value.GetRawText(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return new CheckpointInfo(sessionId, checkpointId);
    }

    /// <inheritdoc />
    public override async ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var row = await db.WorkflowCheckpoints
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.SessionId == sessionId && c.CheckpointId == key.CheckpointId)
            ?? throw new KeyNotFoundException(
                $"No checkpoint '{key.CheckpointId}' for session '{sessionId}'.");

        // Clone so the returned element outlives the JsonDocument that owns its backing buffer.
        using var document = JsonDocument.Parse(row.Payload);
        return document.RootElement.Clone();
    }

    /// <inheritdoc />
    public override async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(
        string sessionId, CheckpointInfo? withParent = null)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var query = db.WorkflowCheckpoints.AsNoTracking().Where(c => c.SessionId == sessionId);
        query = withParent is null
            ? query.Where(c => c.ParentCheckpointId == null)
            : query.Where(c => c.ParentCheckpointId == withParent.CheckpointId);

        // Order client-side: SQLite cannot ORDER BY a DateTimeOffset, and the row count per parent is small.
        var rows = await query.Select(c => new { c.CheckpointId, c.CreatedAt }).ToListAsync();

        return rows
            .OrderBy(row => row.CreatedAt)
            .Select(row => new CheckpointInfo(sessionId, row.CheckpointId))
            .ToList();
    }
}
