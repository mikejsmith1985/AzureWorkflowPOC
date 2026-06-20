// Defines the exception thrown when a duplicate workflow name is detected during persistence.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Thrown by <see cref="IWorkflowRepository.SaveAsync"/> when a (OwnerId, Name) pair
/// already exists in the store, preventing silent overwrites of another owner's workflow.
/// </summary>
public sealed class WorkflowNameConflictException : Exception
{
    /// <summary>Gets the owner identifier whose namespace contains the conflicting name.</summary>
    public string OwnerId { get; init; }

    /// <summary>Gets the workflow name that collided with an existing entry.</summary>
    public string Name { get; init; }

    /// <summary>
    /// Initialises a new instance with the owner and name that caused the conflict.
    /// The message is derived deterministically so callers never need to format it themselves.
    /// </summary>
    /// <param name="ownerId">The owner identifier whose namespace already contains <paramref name="name"/>.</param>
    /// <param name="name">The workflow name that is already taken.</param>
    public WorkflowNameConflictException(string ownerId, string name)
        : base($"A workflow named '{name}' already exists for owner '{ownerId}'.")
    {
        OwnerId = ownerId;
        Name = name;
    }
}
