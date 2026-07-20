// The core enumerations for the Intelligent DoR Validation Workflow (spec-021): the persisted state-machine
// state, the terminal outcome tag, and which SLA tier is currently running.
namespace DBAIAzure.Core.Models.DorWorkflow;

/// <summary>
/// The explicit state-machine state for one DoR workflow instance. Persisted on the instance row after every
/// transition (FR-031) so a run can resume after interruption. This is the state ENUM — distinct from the MAF
/// payload record <see cref="DorRunState"/> that carries it, and from the persisted instance row.
/// </summary>
public enum DorState
{
    /// <summary>Trigger received, HMAC-valid, not a duplicate — nothing evaluated yet.</summary>
    Created,

    /// <summary>Ticket hydrated and the DoR document loaded; AI review (or reply re-evaluation) in progress.</summary>
    Reviewing,

    /// <summary>All DoR criteria satisfied — the ticket will be transitioned to the ready status.</summary>
    Passed,

    /// <summary>One or more DoR criteria unmet — the conversational resolution path begins.</summary>
    Failed,

    /// <summary>Primary outreach sent; the workflow is durably suspended waiting for a human reply.</summary>
    AwaitingResponse,

    /// <summary>The primary SLA elapsed without resolution — escalation is about to trigger.</summary>
    SlaBreach,

    /// <summary>Escalation outreach sent; a second-tier conversation with its own SLA and iteration budget.</summary>
    Escalated,

    /// <summary>A resolution was reached; the field-update payload is being applied to the ticket.</summary>
    Updating,

    /// <summary>Iteration or SLA limits exhausted at some tier — the ticket is handed off to a human.</summary>
    ManualExit,

    /// <summary>Terminal — the ticket was transitioned, or cleanly handed off. No further transitions.</summary>
    Done,
}

/// <summary>The classification written to the audit trail and the ticket tag when a workflow instance ends.</summary>
public enum DorOutcome
{
    /// <summary>The ticket met the DoR on first review and was auto-advanced with no human involvement.</summary>
    Passed,

    /// <summary>The ticket was not ready but was resolved through the human conversation, then advanced.</summary>
    ResolvedAuto,

    /// <summary>Automation could not resolve the ticket within its limits — a human must action it.</summary>
    ManualRequired,
}

/// <summary>Which SLA clock is currently running for an instance (the escalation tier has its own budget).</summary>
public enum SlaTier
{
    /// <summary>The first-tier SLA, measured from the first primary-channel outreach.</summary>
    Primary,

    /// <summary>The second-tier SLA, started fresh when the instance escalates.</summary>
    Escalation,
}
