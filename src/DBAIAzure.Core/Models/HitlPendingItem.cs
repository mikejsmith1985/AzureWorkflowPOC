// Projected view of a run that is currently suspended at a human-approval gate.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Read-model for the Review Queue UI. Holds all information an operator needs to
/// understand and act on a suspended run without loading the full run record or
/// inspecting the workflow definition.
/// </summary>
public sealed record HitlPendingItem(
    /// <summary>The suspended run.</summary>
    string RunId,

    /// <summary>Display name of the workflow.</summary>
    string WorkflowName,

    /// <summary>Label of the approval node the run is parked at.</summary>
    string NodeLabel,

    /// <summary>The plain-language question shown to the approver.</summary>
    string Question,

    /// <summary>Ordered list of approver UPNs/emails; first entry is currently notified.</summary>
    IReadOnlyList<string> ApproverChain,

    /// <summary>Index into <see cref="ApproverChain"/> of the currently notified approver.</summary>
    int CurrentApproverIndex,

    /// <summary>UTC instant the run entered Paused state.</summary>
    DateTimeOffset SuspendedAt,

    /// <summary>UTC deadline for the current approver; null when no timeout is configured.</summary>
    DateTimeOffset? TimeoutAt,

    /// <summary>
    /// What happens when the current approver's timeout elapses:
    /// "escalate" | "auto-approve" | "auto-reject".
    /// </summary>
    string EscalationPolicy
);
