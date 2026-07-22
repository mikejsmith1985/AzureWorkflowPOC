// The DoR configuration slice a single workflow node owns, plus the role that says which slice it is.
// Stored on the node itself so an operator configures each step by opening that step in the visual builder.

namespace DBAIAzure.Core.Models.DorWorkflow;

/// <summary>
/// Identifies which part of the DoR workflow a node represents, so the builder knows which settings to show on
/// that node and the runtime assembler knows which configuration slice to read from it. The role is seeded when
/// the starter workflow is built and travels with the node, so renaming a node on the canvas never breaks the
/// wiring (a label is a display concern; the role is the contract).
/// </summary>
public enum DorNodeRole
{
    /// <summary>Not a DoR-workflow node — no DoR settings are shown or read.</summary>
    None = 0,

    /// <summary>The entry point: which Jira tickets are watched, and the global dry-run switch.</summary>
    Trigger = 1,

    /// <summary>The AI review step: the DoR document (a reference) plus the review model settings.</summary>
    Review = 2,

    /// <summary>The ready path: which transition and status a passing ticket moves to.</summary>
    ReadyTransition = 3,

    /// <summary>The human conversation: channel, reply window, iteration budget, and primary SLA.</summary>
    Resolve = 4,

    /// <summary>Applying a resolution: which ticket fields the agent may write.</summary>
    Update = 5,

    /// <summary>Escalation and manual handoff: escalation channel, SLA, and the manual label.</summary>
    Escalate = 6,

    /// <summary>Audit and close: what gets recorded and commented back to the ticket.</summary>
    Audit = 7,
}

/// <summary>
/// The DoR settings held on one node. Every field is optional: a node only carries the handful that belong to its
/// <see cref="Role"/>, and anything left unset falls back to the connector configuration. Keeping one record for
/// all roles (rather than seven near-identical records) means one serializer, one storage shape, and one place to
/// add a field — the builder and the assembler each read only the subset their role cares about.
/// </summary>
public sealed record DorNodeSettings
{
    /// <summary>Which part of the DoR workflow this node is; drives both the editor and the assembler.</summary>
    public DorNodeRole Role { get; init; } = DorNodeRole.None;

    // ── Trigger ──────────────────────────────────────────────────────────────

    /// <summary>Jira project keys whose tickets start this workflow.</summary>
    public IReadOnlyList<string>? ProjectKeys { get; init; }

    /// <summary>Issue types that trigger validation; empty or unset means all types.</summary>
    public IReadOnlyList<string>? IssueTypes { get; init; }

    /// <summary>Ticket fields extracted and sent to the AI for review.</summary>
    public IReadOnlyList<string>? WatchFields { get; init; }

    /// <summary>When true the workflow logs intended writes and messages without performing them (FR-032).</summary>
    public bool? DryRun { get; init; }

    // ── AI review and prompts ────────────────────────────────────────────────

    /// <summary>Sampling temperature for the review model; lower is more deterministic.</summary>
    public double? Temperature { get; init; }

    /// <summary>Maximum tokens the review model may produce.</summary>
    public int? MaxTokens { get; init; }

    /// <summary>Prompt template used for the DoR review.</summary>
    public string? ReviewPromptTemplate { get; init; }

    /// <summary>Prompt template used for each turn of the human conversation.</summary>
    public string? ConversationPromptTemplate { get; init; }

    /// <summary>Prompt template used when composing ticket updates from a resolution.</summary>
    public string? UpdatePromptTemplate { get; init; }

    // ── Ready transition ─────────────────────────────────────────────────────

    /// <summary>Jira transition id applied when a ticket passes the DoR review.</summary>
    public string? ReadyTransitionId { get; init; }

    /// <summary>The status name a passing ticket lands in, used for audit and messaging.</summary>
    public string? ReadyStatus { get; init; }

    // ── Channels and SLA (primary on Resolve, escalation on Escalate) ─────────

    /// <summary>Channel this step posts to.</summary>
    public string? ChannelId { get; init; }

    /// <summary>How long to wait for a human reply before the step gives up on this iteration.</summary>
    public int? ReplyTimeoutMinutes { get; init; }

    /// <summary>How many back-and-forth attempts this step makes before escalating or exiting.</summary>
    public int? MaxIterations { get; init; }

    /// <summary>SLA budget for this step, in hours, measured on the configured clock.</summary>
    public double? SlaHours { get; init; }

    // ── Update ───────────────────────────────────────────────────────────────

    /// <summary>The strict whitelist of ticket fields the agent may write (enforced in code — FR-021).</summary>
    public IReadOnlyList<string>? AiEditableFields { get; init; }

    // ── Escalate ─────────────────────────────────────────────────────────────

    /// <summary>Label applied to a ticket that exits to manual handling.</summary>
    public string? ManualLabel { get; init; }

    // ── Audit ────────────────────────────────────────────────────────────────

    /// <summary>Whether full AI responses are written to the audit trail.</summary>
    public bool? LogAiResponses { get; init; }

    /// <summary>Whether a comment is posted to the ticket when the review passes.</summary>
    public bool? JiraCommentOnPass { get; init; }

    /// <summary>Whether a comment is posted to the ticket when the review fails.</summary>
    public bool? JiraCommentOnFail { get; init; }

    /// <summary>Whether a comment is posted to the ticket when the run escalates.</summary>
    public bool? JiraCommentOnEscalation { get; init; }
}
