// Defines the classification of every node that can appear in a visual workflow graph.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Classifies each node in a workflow graph so the runtime knows which execution
/// strategy to apply — AI reasoning, a pure function, or a human gate.
/// </summary>
public enum WorkflowNodeType
{
    /// <summary>
    /// An AI-powered reasoning step: the kernel invokes an LLM to analyse context,
    /// make a decision, or produce structured output that drives downstream routing.
    /// </summary>
    AgenticReason,

    /// <summary>
    /// A deterministic branching step: evaluates conditions against the process state
    /// and selects which outgoing edge to follow, with no LLM involvement.
    /// </summary>
    FunctionRoute,

    /// <summary>
    /// A deterministic data-shaping step: maps, filters, or reshapes the process state
    /// from one schema to another before passing it to the next node.
    /// </summary>
    FunctionTransform,

    /// <summary>
    /// A side-effect step that dispatches a notification (email, Teams message, webhook, etc.)
    /// without altering the main process state.
    /// </summary>
    FunctionNotify,

    /// <summary>
    /// A data-access step that reads from or writes to an external store
    /// (database, blob storage, API) and surfaces the result in the process state.
    /// </summary>
    FunctionData,

    /// <summary>
    /// A human-in-the-loop gate: suspends the process and waits for an external approval
    /// signal via <c>IExternalKernelProcessMessageChannel</c> before resuming.
    /// </summary>
    HumanApproval,
}
