// EF entity for MAF workflow checkpoints (spec-019 T030). One row per checkpoint in a run's checkpoint
// tree; the store persists these so a run paused at a HITL gate can resume in place after a restart.
namespace DBAIAzure.Storage.Entities;

/// <summary>
/// A single MAF workflow checkpoint. Checkpoints form a tree per session (run): each has an optional
/// parent, and the payload is the framework's opaque JSON snapshot of the run at a super-step boundary.
/// Keyed by (<see cref="SessionId"/>, <see cref="CheckpointId"/>).
/// </summary>
public sealed class WorkflowCheckpointEntity
{
    /// <summary>The run's session id (the pipeline run id).</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Unique id of this checkpoint within the session.</summary>
    public string CheckpointId { get; set; } = string.Empty;

    /// <summary>The parent checkpoint's id, or null for a root checkpoint.</summary>
    public string? ParentCheckpointId { get; set; }

    /// <summary>The framework's opaque JSON snapshot (a serialized <c>JsonElement</c>).</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>When the checkpoint was written — orders the checkpoint index.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
