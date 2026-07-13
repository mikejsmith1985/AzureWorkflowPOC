// SQLite-backed persistence for personal workflow gallery definitions (nodes, edges, settings, chat history).
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace DBAIAzure.Storage.Repositories;

/// <summary>
/// Persists <see cref="WorkflowDefinition"/> entities in the shared SQLite database via
/// <c>IDbContextFactory</c>, opening a short-lived context per operation so concurrent callers
/// do not contend on a single connection. Topology, settings, and chat history are round-tripped
/// as JSON TEXT blobs using the same pattern established by <c>SqliteConnectorConfigRepository</c>.
/// Owner-scoping is enforced at query level so no user can read or mutate another user's workflows.
/// </summary>
public sealed class SqliteWorkflowRepository : IWorkflowRepository
{
    /// <summary>
    /// Web-defaults JSON options used for all blob serialization/deserialization.
    /// Web defaults enable camelCase property names, matching the existing connector-config blob style.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<PipelineDbContext> _factory;

    /// <summary>
    /// Initialises the repository with the context factory that supplies short-lived
    /// <c>PipelineDbContext</c> instances. The factory is provided via DI and is thread-safe.
    /// </summary>
    /// <param name="factory">EF Core context factory for the shared SQLite pipeline database.</param>
    public SqliteWorkflowRepository(IDbContextFactory<PipelineDbContext> factory)
    {
        _factory = factory;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Performs an owner-scoped upsert: if a row with <paramref name="workflow"/>.Id exists it is
    /// updated in place; otherwise a new row is inserted. <see cref="WorkflowDefinition.LastModifiedAt"/>
    /// is always overwritten with the current UTC instant regardless of what the caller supplies,
    /// so the gallery "recently edited" sort is always accurate.
    /// A (OwnerId, Name) conflict with a <em>different</em> Id raises
    /// <see cref="WorkflowNameConflictException"/> before any write is attempted.
    /// </remarks>
    public async Task<Guid> SaveAsync(WorkflowDefinition workflow, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Guard: reject the save if a different workflow already occupies this (OwnerId, Name) slot.
        var isNameTaken = await db.Workflows
            .AsNoTracking()
            .AnyAsync(
                r => r.OwnerId == workflow.OwnerId
                  && r.Name    == workflow.Name
                  && r.Id      != workflow.Id,
                ct);

        if (isNameTaken)
            throw new WorkflowNameConflictException(workflow.OwnerId, workflow.Name);

        var existing = await db.Workflows
            .FirstOrDefaultAsync(r => r.Id == workflow.Id, ct);

        var utcNow = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            var newRecord = MapToRecord(workflow);
            // Honour the caller's CreatedAt (set once by WorkflowDefinition.CreateNew) but
            // always stamp LastModifiedAt fresh so the first save is indistinguishable from
            // subsequent saves from the gallery's sort perspective.
            newRecord.LastModifiedAt = utcNow;
            db.Workflows.Add(newRecord);
        }
        else
        {
            existing.Name            = workflow.Name;
            existing.NodesJson       = JsonSerializer.Serialize(workflow.Nodes, JsonOptions);
            existing.EdgesJson       = JsonSerializer.Serialize(workflow.Edges, JsonOptions);
            existing.SettingsJson    = JsonSerializer.Serialize(workflow.Settings, JsonOptions);
            existing.ChatHistoryJson = JsonSerializer.Serialize(workflow.ChatHistory, JsonOptions);
            existing.GeneratedCode   = workflow.GeneratedCode;
            existing.ThumbnailSvg    = workflow.ThumbnailSvg;
            existing.LastModifiedAt  = utcNow;
            // CreatedAt is intentionally not updated — it was set once at insert time.
        }

        await db.SaveChangesAsync(ct);
        return workflow.Id;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <see langword="null"/> rather than throwing when the row is absent or belongs to a
    /// different owner, so callers can distinguish "not found" from "access denied" without leaking
    /// the existence of other owners' workflows.
    /// </remarks>
    public async Task<WorkflowDefinition?> GetAsync(Guid id, string ownerId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var record = await db.Workflows
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == ownerId, ct);

        return record is null ? null : MapToDefinition(record);
    }

    /// <inheritdoc/>
    public async Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var record = await db.Workflows
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        return record is null ? null : MapToDefinition(record);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Results are ordered by name so the gallery card list is deterministic regardless of
    /// insertion order. Scoping to <paramref name="ownerId"/> prevents cross-tenant data leakage
    /// in the shared SQLite store.
    /// </remarks>
    public async Task<IReadOnlyList<WorkflowDefinition>> ListByOwnerAsync(
        string ownerId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var records = await db.Workflows
            .AsNoTracking()
            .Where(r => r.OwnerId == ownerId)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        return records.Select(MapToDefinition).ToList().AsReadOnly();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The method is designed to be idempotent-friendly: deleting a non-existent or
    /// foreign-owner row returns <see langword="false"/> rather than throwing, so a
    /// retry after a transient failure does not surface a spurious error.
    /// </remarks>
    public async Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var record = await db.Workflows
            .FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == ownerId, ct);

        if (record is null)
            return false;

        db.Workflows.Remove(record);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Intentionally lightweight — reads a single boolean without loading any JSON blobs.
    /// Used as a pre-flight check so callers can surface user-friendly validation errors
    /// before attempting a full save round-trip.
    /// </remarks>
    public async Task<bool> ExistsAsync(string name, string ownerId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        return await db.Workflows
            .AsNoTracking()
            .AnyAsync(r => r.OwnerId == ownerId && r.Name == name, ct);
    }

    // ── Private helpers ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deserializes all JSON blob columns on <paramref name="record"/> and assembles a fully
    /// populated <see cref="WorkflowDefinition"/>. Falls back to empty collections when a blob
    /// is absent, so older rows with missing columns do not surface deserialization exceptions.
    /// </summary>
    private static WorkflowDefinition MapToDefinition(WorkflowDefinitionRecord record)
    {
        var nodes = (IReadOnlyList<WorkflowNode>)(
            string.IsNullOrWhiteSpace(record.NodesJson)
                ? new List<WorkflowNode>()
                : JsonSerializer.Deserialize<List<WorkflowNode>>(record.NodesJson, JsonOptions)
                  ?? new List<WorkflowNode>());

        var edges = (IReadOnlyList<WorkflowEdge>)(
            string.IsNullOrWhiteSpace(record.EdgesJson)
                ? new List<WorkflowEdge>()
                : JsonSerializer.Deserialize<List<WorkflowEdge>>(record.EdgesJson, JsonOptions)
                  ?? new List<WorkflowEdge>());

        var settings = string.IsNullOrWhiteSpace(record.SettingsJson)
            ? new WorkflowSettings()
            : JsonSerializer.Deserialize<WorkflowSettings>(record.SettingsJson, JsonOptions)
              ?? new WorkflowSettings();

        var chatHistory = (IReadOnlyList<WorkflowChatMessage>)(
            string.IsNullOrWhiteSpace(record.ChatHistoryJson)
                ? new List<WorkflowChatMessage>()
                : JsonSerializer.Deserialize<List<WorkflowChatMessage>>(record.ChatHistoryJson, JsonOptions)
                  ?? new List<WorkflowChatMessage>());

        return new WorkflowDefinition
        {
            Id             = record.Id,
            Name           = record.Name,
            OwnerId        = record.OwnerId,
            Nodes          = nodes,
            Edges          = edges,
            Settings       = settings,
            ChatHistory    = chatHistory,
            GeneratedCode  = record.GeneratedCode,
            ThumbnailSvg   = record.ThumbnailSvg,
            CreatedAt      = record.CreatedAt,
            LastModifiedAt = record.LastModifiedAt,
        };
    }

    /// <summary>
    /// Serializes all collection and object properties on <paramref name="workflow"/> into their
    /// corresponding JSON blob columns. The resulting record is ready for EF Core tracking —
    /// the caller must still set <c>LastModifiedAt</c> to the current UTC instant before saving.
    /// </summary>
    private static WorkflowDefinitionRecord MapToRecord(WorkflowDefinition workflow) =>
        new()
        {
            Id             = workflow.Id,
            Name           = workflow.Name,
            OwnerId        = workflow.OwnerId,
            NodesJson      = JsonSerializer.Serialize(workflow.Nodes, JsonOptions),
            EdgesJson      = JsonSerializer.Serialize(workflow.Edges, JsonOptions),
            SettingsJson   = JsonSerializer.Serialize(workflow.Settings, JsonOptions),
            ChatHistoryJson = JsonSerializer.Serialize(workflow.ChatHistory, JsonOptions),
            GeneratedCode  = workflow.GeneratedCode,
            ThumbnailSvg   = workflow.ThumbnailSvg,
            CreatedAt      = workflow.CreatedAt,
            LastModifiedAt = workflow.LastModifiedAt,
        };
}
