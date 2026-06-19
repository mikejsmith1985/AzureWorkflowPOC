# Contract: IWorkflowRepository

**Feature**: 003-visual-workflow-builder | **Layer**: `DBAIAzure.Core.Interfaces`
**Implementation**: `DBAIAzure.Storage.Repositories.SqliteWorkflowRepository`

---

## Responsibility

Provides durable storage and retrieval of `WorkflowDefinition` objects. Enforces personal
ownership — every read and write operation requires the `ownerId` of the requesting user,
and no operation may return or mutate a workflow belonging to a different owner.

---

## Interface Definition

```csharp
/// <summary>
/// Persists and retrieves workflow definitions scoped to their owning user.
/// All operations are owner-scoped — no cross-user access is possible through this interface.
/// </summary>
public interface IWorkflowRepository
{
    /// <summary>
    /// Upserts a workflow definition. Creates the record if <see cref="WorkflowDefinition.Id"/>
    /// does not yet exist; updates all fields if it does.
    /// Throws <see cref="WorkflowNameConflictException"/> if another workflow owned by the same
    /// user already uses <see cref="WorkflowDefinition.Name"/> and has a different Id.
    /// </summary>
    Task SaveAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the workflow with the given <paramref name="id"/> if it is owned by
    /// <paramref name="ownerId"/>; returns null if not found or owned by another user.
    /// </summary>
    Task<WorkflowDefinition?> GetAsync(Guid id, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all workflows owned by <paramref name="ownerId"/>, ordered by
    /// <see cref="WorkflowDefinition.LastModifiedAt"/> descending. Never returns workflows
    /// belonging to other users.
    /// </summary>
    Task<IReadOnlyList<WorkflowDefinition>> ListByOwnerAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes the workflow with <paramref name="id"/> if and only if it is owned
    /// by <paramref name="ownerId"/>. Returns true if the record was deleted; false if not found
    /// or owned by a different user. Never throws for a not-found condition.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a workflow with <paramref name="id"/> exists and is owned by
    /// <paramref name="ownerId"/>. Used for pre-delete confirmation checks.
    /// </summary>
    Task<bool> ExistsAsync(Guid id, string ownerId, CancellationToken cancellationToken = default);
}
```

---

## Contracts and Invariants

1. **Owner isolation**: `GetAsync`, `DeleteAsync`, and `ExistsAsync` all accept `ownerId`
   and must silently return null/false (never throw) for workflows not owned by that user.
2. **Name uniqueness**: `SaveAsync` must throw `WorkflowNameConflictException` (a typed
   domain exception in `DBAIAzure.Core`) if the `(ownerId, name)` combination already
   maps to a different workflow `Id`. Updating an existing workflow to the same name it
   already has is not a conflict.
3. **Upsert semantics**: `SaveAsync` must be idempotent — calling it twice with the same
   `WorkflowDefinition` leaves the database in the same state as calling it once.
4. **Timestamp management**: `CreatedAt` is set on first insert only; `LastModifiedAt` is
   set by the repository on every `SaveAsync` call (the caller must not set it).
5. **No partial saves**: All JSON columns are written atomically in the same SQL statement.
   A partial write must not be possible.

---

## Error Types

| Exception | Thrown when |
|-----------|-------------|
| `WorkflowNameConflictException` | `SaveAsync` — name already used by a different workflow for the same owner |

All other error conditions (database unavailable, serialization failure) propagate as the
underlying exception type — callers are expected to handle them at the orchestration layer.

---

## Test Obligations

- Unit tests use an in-memory SQLite database (`Microsoft.EntityFrameworkCore.InMemory`).
- Owner isolation is verified: a workflow saved under `ownerA` is not returned for `ownerB`.
- Name-uniqueness conflict throws `WorkflowNameConflictException`.
- Upsert round-trip: save, modify name, save again, assert new name is persisted.
- Delete returns false for a non-existent workflow without throwing.
