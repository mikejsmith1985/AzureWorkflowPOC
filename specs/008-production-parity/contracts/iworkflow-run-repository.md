# Contract: IWorkflowRunRepository

`DBAIAzure.Core/Interfaces/IWorkflowRunRepository.cs`

Persists and retrieves `WorkflowRunRecord` snapshots for all workflow builder runs.
Implementations: `EfWorkflowRunRepository` (production), in-memory fake (unit tests).

---

```csharp
public interface IWorkflowRunRepository
{
    /// <summary>Writes a new run record. Throws if RunId already exists.</summary>
    Task CreateAsync(WorkflowRunRecord run, CancellationToken ct = default);

    /// <summary>Replaces the stored record for an existing RunId. Throws if not found.</summary>
    Task UpdateAsync(WorkflowRunRecord run, CancellationToken ct = default);

    /// <summary>Returns null if the RunId is not found.</summary>
    Task<WorkflowRunRecord?> GetAsync(string runId, CancellationToken ct = default);

    /// <summary>Returns all runs with the given status, ordered by StartedAt descending.</summary>
    Task<IReadOnlyList<WorkflowRunRecord>> ListByStatusAsync(
        WorkflowRunStatus status, CancellationToken ct = default);

    /// <summary>
    /// Returns all runs for a workflow, ordered by StartedAt descending.
    /// Pass null workflowId to list all runs across all workflows.
    /// </summary>
    Task<IReadOnlyList<WorkflowRunRecord>> ListAsync(
        string? workflowId = null,
        int page = 0,
        int pageSize = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes all terminal runs (Completed, Failed, TimedOut, Cancelled) whose
    /// CompletedAt is older than the given cutoff. Never deletes Paused or Running runs.
    /// Returns the count of deleted records.
    /// </summary>
    Task<int> PurgeTerminalRunsOlderThanAsync(
        DateTimeOffset cutoff, CancellationToken ct = default);
}
```

---

**Registration**: `services.AddScoped<IWorkflowRunRepository, EfWorkflowRunRepository>()`

**Usage sites**:
- `WorkflowExecutionOrchestrator` — create on start, update on every status transition, list Paused on startup for rehydration.
- `ReviewQueue.razor` — `ListByStatusAsync(Paused)`.
- `RunHistory.razor` — `ListAsync(workflowId, page, pageSize)`.
- Retention `IHostedService` — `PurgeTerminalRunsOlderThanAsync(DateTimeOffset.UtcNow - ttl)`.
