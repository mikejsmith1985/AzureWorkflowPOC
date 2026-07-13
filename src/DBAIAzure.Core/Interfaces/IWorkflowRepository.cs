// Defines the persistence contract for workflow definitions, scoped to an owner identity.

using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Persistence contract for <see cref="WorkflowDefinition"/> entities. All operations are
/// owner-scoped so that no caller can read or mutate another owner's workflows.
/// </summary>
public interface IWorkflowRepository
{
    /// <summary>
    /// Persists a new or updated <see cref="WorkflowDefinition"/> and returns its assigned
    /// identifier. The repository enforces uniqueness of the <c>(OwnerId, Name)</c> pair;
    /// if a <em>different</em> workflow with the same owner and name already exists,
    /// a <see cref="WorkflowNameConflictException"/> is thrown so callers receive a clear,
    /// actionable error rather than a silent overwrite.
    /// </summary>
    /// <param name="workflow">The workflow definition to persist.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>The <see cref="Guid"/> that uniquely identifies the saved workflow.</returns>
    /// <exception cref="WorkflowNameConflictException">
    /// Thrown when a different workflow with the same <c>OwnerId</c> and <c>Name</c> already exists.
    /// </exception>
    Task<Guid> SaveAsync(WorkflowDefinition workflow, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single <see cref="WorkflowDefinition"/> by its identifier, verifying that it
    /// belongs to the specified owner before returning it. Returning <see langword="null"/> rather
    /// than throwing ensures callers can distinguish "not found" from "access denied" without
    /// leaking the existence of other owners' workflows.
    /// </summary>
    /// <param name="id">The unique identifier of the workflow to retrieve.</param>
    /// <param name="ownerId">The owner whose workflows are in scope.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>
    /// The matching <see cref="WorkflowDefinition"/>, or <see langword="null"/> if no workflow
    /// with that <paramref name="id"/> exists for the given <paramref name="ownerId"/>.
    /// </returns>
    Task<WorkflowDefinition?> GetAsync(Guid id, string ownerId, CancellationToken ct = default);

    /// <summary>
    /// Returns the workflow definition with the given id regardless of owner — a system-context lookup used
    /// by startup rehydration, which reloads a paused run's definition from its persisted run record (which
    /// carries no owner). Returns <see langword="null"/> when no workflow with that id exists (spec-019 T032).
    /// </summary>
    Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Returns all workflow definitions that belong to the specified owner, ordered by name.
    /// Scoping the list to an owner prevents cross-tenant data leakage even when the underlying
    /// store is shared.
    /// </summary>
    /// <param name="ownerId">The owner whose workflows should be returned.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>
    /// A read-only list of <see cref="WorkflowDefinition"/> instances belonging to
    /// <paramref name="ownerId"/>. Returns an empty list when none exist.
    /// </returns>
    Task<IReadOnlyList<WorkflowDefinition>> ListByOwnerAsync(string ownerId, CancellationToken ct = default);

    /// <summary>
    /// Removes the workflow identified by <paramref name="id"/> if it belongs to
    /// <paramref name="ownerId"/>. Returning a boolean rather than throwing on "not found"
    /// keeps delete idempotent-friendly for callers that may retry.
    /// </summary>
    /// <param name="id">The unique identifier of the workflow to delete.</param>
    /// <param name="ownerId">The owner whose workflows are in scope.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> if the workflow was found and deleted;
    /// <see langword="false"/> if no matching workflow existed for that owner.
    /// </returns>
    Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct = default);

    /// <summary>
    /// Checks whether a workflow with the given <paramref name="name"/> already exists for
    /// <paramref name="ownerId"/>. Used as a lightweight pre-flight check before save operations
    /// so callers can surface user-friendly validation errors without attempting a full write.
    /// </summary>
    /// <param name="name">The workflow name to check.</param>
    /// <param name="ownerId">The owner whose namespace is being checked.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> if a workflow with that name exists for the owner;
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> ExistsAsync(string name, string ownerId, CancellationToken ct = default);
}
