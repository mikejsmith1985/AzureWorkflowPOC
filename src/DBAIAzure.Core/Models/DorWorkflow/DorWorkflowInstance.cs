// The queryable lifecycle + SLA record for one DoR workflow run (spec-021). Distinct from the MAF checkpoint
// (the opaque resumable snapshot) and from the in-flight DorRunState payload — this is the row the SLA sweeper,
// restart rehydration, and the UI read.
namespace DBAIAzure.Core.Models.DorWorkflow;

/// <summary>
/// One workflow instance for one ticket. Persisted after every state transition (FR-031). One active instance
/// per ticket — enforced by a unique index on <see cref="TicketKey"/> filtered to non-terminal states (FR-004
/// idempotency). The SLA sweeper queries <see cref="SlaDeadlineAt"/>; restart rehydration reloads instances not
/// in <see cref="DorState.Done"/> and resumes them from the latest checkpoint.
/// </summary>
public sealed record DorWorkflowInstance
{
    /// <summary>The run id — also the MAF session id / checkpoint key.</summary>
    public required string RunId { get; init; }

    /// <summary>The Jira issue key under review.</summary>
    public required string TicketKey { get; init; }

    /// <summary>Current state-machine state.</summary>
    public DorState State { get; init; } = DorState.Created;

    /// <summary>Outstanding unmet DoR criterion names (JSON-serialized in storage).</summary>
    public IReadOnlyList<string> OutstandingGaps { get; init; } = Array.Empty<string>();

    /// <summary>Primary-loop exchange count (includes timed-out iterations).</summary>
    public int PrimaryIterations { get; init; }

    /// <summary>Escalation-loop exchange count (reset on entering escalation).</summary>
    public int EscalationIterations { get; init; }

    /// <summary>When the active SLA clock started (first outreach).</summary>
    public DateTimeOffset? SlaClockStartedAt { get; init; }

    /// <summary>The computed SLA deadline the sweeper compares against (indexed).</summary>
    public DateTimeOffset? SlaDeadlineAt { get; init; }

    /// <summary>Which SLA tier is currently running.</summary>
    public SlaTier SlaTier { get; init; } = SlaTier.Primary;

    /// <summary>The channel currently in use (primary or escalation).</summary>
    public string ActiveChannelId { get; init; } = "";

    /// <summary>The chat thread this instance owns (the reply boundary).</summary>
    public string ThreadRef { get; init; } = "";

    /// <summary>Cursor for reply polling (dedups replies across cycles/restarts).</summary>
    public string? LastSeenReplyRef { get; init; }

    /// <summary>Snapshot of the dry-run flag at start.</summary>
    public bool IsDryRun { get; init; }

    /// <summary>The terminal outcome (set when <see cref="State"/> is <see cref="DorState.Done"/>).</summary>
    public DorOutcome? Outcome { get; init; }

    /// <summary>When the instance was created.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>When the instance was last updated (any transition).</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>When the instance reached <see cref="DorState.Done"/>.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Human-readable manual-exit / failure context.</summary>
    public string? FailureReason { get; init; }

    /// <summary>True once the instance reaches a terminal state.</summary>
    public bool IsTerminal => State == DorState.Done;
}
