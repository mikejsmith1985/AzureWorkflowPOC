// The per-node lifecycle state used by realization and readiness reporting.

namespace DBAIAzure.Core.Models;

/// <summary>
/// The realization state of a single workflow node. Computed (not persisted) from the node's
/// configuration, its realization provenance, and the health of any connector it binds to.
/// </summary>
public enum NodeRealizationStatus
{
    /// <summary>Plain-language only — no realized, executable configuration yet.</summary>
    Draft,

    /// <summary>An LLM proposal exists but the user has not accepted it (transient, session-scoped).</summary>
    Proposed,

    /// <summary>Configuration present, schema-valid, accepted, and (if bound) connector healthy.</summary>
    Realized,

    /// <summary>The realized form needs a connector that is missing or unhealthy.</summary>
    Blocked,

    /// <summary>The step could not be realized confidently (goal too vague or contradictory).</summary>
    NeedsInput,

    /// <summary>The node's intent (label/goal/connections) changed after it was realized.</summary>
    OutOfDate,
}
