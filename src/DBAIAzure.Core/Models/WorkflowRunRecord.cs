// Persistent projection of a workflow builder run — one record per execution instance.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Lightweight projection of a workflow execution run as stored in the database.
/// Updated on each status transition; never grows stale between application restarts
/// because <c>IWorkflowRunRepository</c> is the single write path.
/// </summary>
public sealed record WorkflowRunRecord(
    /// <summary>Stable identifier assigned at run creation.</summary>
    string RunId,

    /// <summary>Identity of the workflow definition that produced this run.</summary>
    Guid WorkflowId,

    /// <summary>Display name of the workflow at the time the run was started.</summary>
    string WorkflowName,

    /// <summary>Current lifecycle status.</summary>
    WorkflowRunStatus Status,

    /// <summary>Identity of the user or system that triggered the run (e.g. email or "system").</summary>
    string TriggeredBy,

    /// <summary>UTC instant the run was created.</summary>
    DateTimeOffset StartedAt,

    /// <summary>UTC instant the run entered Paused state, or null if never paused.</summary>
    DateTimeOffset? SuspendedAt,

    /// <summary>UTC instant the run resumed from Paused, or null if never resumed.</summary>
    DateTimeOffset? ResumedAt,

    /// <summary>UTC instant the run reached a terminal state, or null while still running.</summary>
    DateTimeOffset? CompletedAt,

    /// <summary>Human-readable reason the run failed; null when the run has not failed.</summary>
    string? FailureReason
);
