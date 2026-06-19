// Defines the classification of every node that can appear in a visual workflow graph.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Classifies each node in a workflow graph so the runtime knows which execution
/// strategy to apply — entry-point trigger, AI reasoning, a pure function, or a human gate.
/// The numeric value also controls palette category sort order: a lower value renders
/// higher in the palette, so <see cref="Trigger"/> at 0 always appears first.
/// </summary>
public enum WorkflowNodeType
{
    /// <summary>
    /// Marks the single entry point of a workflow. Has zero input ports and one "Begin"
    /// output port. Must appear exactly once per workflow definition. The runtime does not
    /// execute it as a processing step — it provides the initial context forwarded to the
    /// first downstream node.
    /// </summary>
    Trigger = 0,

    /// <summary>
    /// An AI-powered reasoning step: the kernel invokes an LLM to analyse context,
    /// make a decision, or produce structured output that drives downstream routing.
    /// </summary>
    AgenticReason = 1,

    /// <summary>
    /// A deterministic branching step: evaluates conditions against the process state
    /// and selects which outgoing edge to follow, with no LLM involvement.
    /// </summary>
    FunctionRoute = 2,

    /// <summary>
    /// A deterministic data-shaping step: maps, filters, or reshapes the process state
    /// from one schema to another before passing it to the next node.
    /// </summary>
    FunctionTransform = 3,

    /// <summary>
    /// A side-effect step that dispatches a notification (email, Teams message, webhook, etc.)
    /// without altering the main process state.
    /// </summary>
    FunctionNotify = 4,

    /// <summary>
    /// A data-access step that reads from or writes to an external store
    /// (database, blob storage, API) and surfaces the result in the process state.
    /// </summary>
    FunctionData = 5,

    /// <summary>
    /// A human-in-the-loop gate: suspends the process and waits for an external approval
    /// signal via <c>IExternalKernelProcessMessageChannel</c> before resuming.
    /// </summary>
    HumanApproval = 6,
}
