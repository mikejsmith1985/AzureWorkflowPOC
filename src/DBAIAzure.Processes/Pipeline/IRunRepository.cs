using DBAIAzure.Core.Models;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// Persistence contract for pipeline runs and step snapshots.
/// Implemented by SqliteRunRepository in DBAIAzure.Storage;
/// a no-op NullRunRepository is used by the console runner.
/// </summary>
public interface IRunRepository
{
    Task UpsertRunAsync(string runId, TicketState ticket, PipelineRunStatus status, CancellationToken ct = default);
    Task AddSnapshotAsync(string runId, string stepName, TicketState before, TicketState after, CancellationToken ct = default);
    Task<IReadOnlyList<PersistedRunSummary>> ListRunsAsync(string? search = null, PipelineRunStatus? status = null, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<PersistedRunDetail?> GetRunAsync(string runId, CancellationToken ct = default);
}

// ── Read-model DTOs ────────────────────────────────────────────────────────────

public record PersistedRunSummary(
    string RunId,
    string TicketId,
    string Title,
    string Source,
    string? SnowNumber,
    string? SnowPriority,
    PipelineRunStatus Status,
    int? StoryPoints,
    string? JiraIssueUrl,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

public record PersistedRunDetail(
    PersistedRunSummary Summary,
    string TicketStateJson,
    IReadOnlyList<PersistedSnapshot> Snapshots);

public record PersistedSnapshot(
    string StepName,
    string InputJson,
    string OutputJson,
    DateTimeOffset Timestamp);

/// <summary>No-op implementation used when persistence is not configured (e.g. console runner).</summary>
public sealed class NullRunRepository : IRunRepository
{
    public static readonly NullRunRepository Instance = new();
    public Task UpsertRunAsync(string runId, TicketState ticket, PipelineRunStatus status, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddSnapshotAsync(string runId, string stepName, TicketState before, TicketState after, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<PersistedRunSummary>> ListRunsAsync(string? search = null, PipelineRunStatus? status = null, int skip = 0, int take = 50, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PersistedRunSummary>>([]);
    public Task<PersistedRunDetail?> GetRunAsync(string runId, CancellationToken ct = default) => Task.FromResult<PersistedRunDetail?>(null);
}
