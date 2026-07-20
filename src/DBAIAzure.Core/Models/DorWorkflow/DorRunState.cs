// The in-flight payload for a DoR workflow run (spec-021). This is the record that flows between MAF executors
// and rides the RequestPort request/response so the full paused state is recovered from the request on resume.
namespace DBAIAzure.Core.Models.DorWorkflow;

/// <summary>
/// The MAF payload record carried between DoR executors and on the human-in-the-loop <c>RequestPort</c>. It
/// CONTAINS a <see cref="DorState"/> (the state enum) plus everything an executor needs to make the next
/// decision — it is deliberately distinct from the state enum and from the persisted <c>DorWorkflowInstance</c>
/// row (which is the queryable lifecycle/SLA record). Executors produce new instances with <c>with</c>; the
/// record is data-only (no config-dependent computation — the orchestrator/executors own routing decisions).
/// </summary>
public sealed record DorRunState
{
    /// <summary>The run id — also the MAF session id and the persisted instance key.</summary>
    public required string RunId { get; init; }

    /// <summary>The Jira issue key under review (e.g. <c>SBRO-123</c>).</summary>
    public required string TicketKey { get; init; }

    /// <summary>The browse URL of the ticket, for message deep-links.</summary>
    public string TicketUrl { get; init; } = "";

    /// <summary>The current state-machine state (persisted every transition).</summary>
    public DorState State { get; init; } = DorState.Created;

    /// <summary>The normalized watched-field values that form the AI review payload.</summary>
    public IReadOnlyDictionary<string, string?> Fields { get; init; } =
        new Dictionary<string, string?>();

    /// <summary>The full DoR document text injected into the review prompt.</summary>
    public string DorDocumentText { get; init; } = "";

    /// <summary>The DoR document version/etag in effect at review time (recorded for audit traceability).</summary>
    public string? DorDocumentVersion { get; init; }

    /// <summary>The unmet DoR criterion names still outstanding, carried across conversation turns.</summary>
    public IReadOnlyList<string> OutstandingGaps { get; init; } = Array.Empty<string>();

    /// <summary>Exchange count in the primary loop (includes timed-out iterations — FR-014).</summary>
    public int PrimaryIterations { get; init; }

    /// <summary>Exchange count in the escalation loop (reset to zero on entering escalation — FR-017).</summary>
    public int EscalationIterations { get; init; }

    /// <summary>When the active SLA clock started (first outreach) — null before <see cref="DorState.AwaitingResponse"/>.</summary>
    public DateTimeOffset? SlaClockStartedAt { get; init; }

    /// <summary>The computed SLA deadline (business-hours aware) the sweeper compares against.</summary>
    public DateTimeOffset? SlaDeadlineAt { get; init; }

    /// <summary>Which SLA tier is currently running.</summary>
    public SlaTier SlaTier { get; init; } = SlaTier.Primary;

    /// <summary>The channel (primary or escalation) the conversation is currently in.</summary>
    public string ActiveChannelId { get; init; } = "";

    /// <summary>The chat thread this instance owns — the boundary within which human replies are captured.</summary>
    public string ThreadRef { get; init; } = "";

    /// <summary>Cursor for reply polling (the last processed reply id) — dedups across poll cycles and restarts.</summary>
    public string? LastSeenReplyRef { get; init; }

    /// <summary>Snapshot of the global dry-run flag at start — when true, every write is logged, not performed.</summary>
    public bool IsDryRun { get; init; }

    /// <summary>Set by the review/reply-eval step: true when the latest evaluation passed all criteria.</summary>
    public bool ReviewPassed { get; init; }

    /// <summary>Set by the reply-eval step when a human reply resolved every outstanding gap.</summary>
    public bool JustResolved { get; init; }

    /// <summary>The latest human reply text, carried on the RequestPort response into the reply-eval step.</summary>
    public string? HumanReply { get; init; }

    /// <summary>Set by reply-eval: continue the conversation (unresolved, within the iteration budget).</summary>
    public bool ContinueConversation { get; init; }

    /// <summary>A focused follow-up message the outreach step should post next (from a partial reply).</summary>
    public string? PendingOutreachMessage { get; init; }

    /// <summary>The whitelisted-candidate field updates a resolution implies (filtered again before any write).</summary>
    public IReadOnlyDictionary<string, string> ResolvedFieldUpdates { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Set on the HITL-gate response by the SLA sweeper to escalate a breached conversation.</summary>
    public bool EscalateRequested { get; init; }

    /// <summary>Set on the HITL-gate response by the SLA sweeper to force a manual exit (limits exhausted).</summary>
    public bool ManualExitRequested { get; init; }

    /// <summary>The terminal outcome, set when <see cref="State"/> reaches <see cref="DorState.Done"/>.</summary>
    public DorOutcome? Outcome { get; init; }

    /// <summary>Human-readable context for a manual exit or failure.</summary>
    public string? FailureReason { get; init; }
}
