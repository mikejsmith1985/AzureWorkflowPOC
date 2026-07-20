// EF Core entity for a DoR workflow instance (spec-021): the queryable lifecycle + SLA record for one ticket's
// run, updated on every state transition. Maps to the DorWorkflowInstances table. Distinct from the MAF
// checkpoint (WorkflowCheckpoints), which holds the opaque resumable snapshot.
namespace DBAIAzure.Storage.Entities;

/// <summary>
/// Persisted representation of a DoR workflow instance. One row per ticket run; the state enum and SLA fields
/// are stored so the background SLA sweeper and restart rehydration can query without touching MAF checkpoints.
/// Idempotency (FR-004) is enforced by a unique index on <see cref="TicketKey"/> filtered to non-terminal states.
/// </summary>
public sealed class DorWorkflowInstanceEntity
{
    /// <summary>Stable run id — assigned by the orchestrator, also the MAF session/checkpoint key.</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>The Jira issue key under review.</summary>
    public string TicketKey { get; set; } = string.Empty;

    /// <summary>State-machine state stored as its integer ordinal.</summary>
    public int State { get; set; }

    /// <summary>Outstanding unmet DoR criterion names, JSON-serialized.</summary>
    public string OutstandingGapsJson { get; set; } = "[]";

    public int PrimaryIterations { get; set; }
    public int EscalationIterations { get; set; }

    public DateTimeOffset? SlaClockStartedAt { get; set; }

    /// <summary>The computed SLA deadline the sweeper compares against (indexed).</summary>
    public DateTimeOffset? SlaDeadlineAt { get; set; }

    /// <summary>SLA tier stored as its integer ordinal.</summary>
    public int SlaTier { get; set; }

    public string ActiveChannelId { get; set; } = string.Empty;
    public string ThreadRef { get; set; } = string.Empty;
    public string? LastSeenReplyRef { get; set; }
    public bool IsDryRun { get; set; }

    /// <summary>Terminal outcome ordinal, or null until the instance is Done.</summary>
    public int? Outcome { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
}
