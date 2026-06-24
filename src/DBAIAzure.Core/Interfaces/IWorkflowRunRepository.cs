// Persistence contract for workflow builder run records (FR-18).

using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Single write path for workflow builder run lifecycle records.
/// Implementations must be thread-safe; the orchestrator calls them from background threads.
/// </summary>
public interface IWorkflowRunRepository
{
    /// <summary>Persists a brand-new run record. Called once, immediately after run creation.</summary>
    Task CreateAsync(WorkflowRunRecord run, CancellationToken ct = default);

    /// <summary>
    /// Replaces the stored record for <paramref name="run"/>.RunId with the supplied snapshot.
    /// Called on every status transition to keep the DB in sync with the in-memory state.
    /// </summary>
    Task UpdateAsync(WorkflowRunRecord run, CancellationToken ct = default);

    /// <summary>
    /// Returns the stored record for the given run, or null if no such run exists.
    /// </summary>
    Task<WorkflowRunRecord?> GetAsync(string runId, CancellationToken ct = default);

    /// <summary>
    /// Returns all runs whose <c>Status</c> matches <paramref name="status"/>,
    /// ordered by <c>StartedAt</c> descending (most recent first).
    /// </summary>
    Task<IReadOnlyList<WorkflowRunRecord>> ListByStatusAsync(
        WorkflowRunStatus status,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all runs, ordered by <c>StartedAt</c> descending.
    /// Callers may apply their own filter on the result set.
    /// </summary>
    Task<IReadOnlyList<WorkflowRunRecord>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Deletes runs in terminal states (Completed, Failed, TimedOut, Cancelled)
    /// whose <c>CompletedAt</c> is older than <paramref name="cutoff"/>.
    /// Paused and Running runs are never deleted by this method.
    /// </summary>
    Task PurgeTerminalRunsOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
