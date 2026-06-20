// Data payload model shared across all KernelProcessStep instances in a workflow run.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Data payload passed between KernelProcessStep instances in a workflow run.
/// Each step reads its goal/input and writes its output into a new record instance
/// (records are immutable).
/// </summary>
public sealed record WorkflowStepData
{
    /// <summary>
    /// Stable identifier for the entire workflow run, generated once at entry and
    /// threaded through every step so correlated log entries can be traced end-to-end.
    /// </summary>
    public required string RunId { get; init; }

    /// <summary>
    /// Identifies which canvas node this data belongs to, allowing the process router
    /// to dispatch outputs to the correct downstream step.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// Natural-language goal injected into agentic nodes so the LLM knows what outcome
    /// to pursue. Null for function nodes that execute deterministic logic instead.
    /// </summary>
    public string? GoalPrompt { get; init; }

    /// <summary>
    /// Text input arriving at this step, either translated from the original trigger
    /// by WorkflowInputTranslator or forwarded as the output of the previous step.
    /// Null when the step is the first in the chain and receives no predecessor output.
    /// </summary>
    public string? InputPayload { get; init; }

    /// <summary>
    /// Text produced by the current step once it completes. Null while the step is still
    /// in progress; set by the step implementation before raising its completion event.
    /// </summary>
    public string? OutputPayload { get; init; }

    /// <summary>
    /// Captures the human reviewer's decision for HumanApproval steps: true means the
    /// run is approved to continue, false means it is rejected and the run halts.
    /// Defaults to false so an unreviewed record cannot silently propagate.
    /// </summary>
    public bool IsApproved { get; init; }
}
