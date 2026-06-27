// Event type taxonomy for all observable points in a workflow execution timeline.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Identifies the category of an event recorded by <c>IWorkflowObserver</c>.
/// Every event produced during a workflow run carries one of these labels so the
/// Execution History UI can display and filter the timeline at a glance.
/// </summary>
public enum WorkflowEventType
{
    /// <summary>A workflow node began execution.</summary>
    StepStarted,

    /// <summary>A workflow node finished without error.</summary>
    StepCompleted,

    /// <summary>A workflow node encountered an unrecoverable error.</summary>
    StepFailed,

    /// <summary>A workflow node was intentionally bypassed (conditional branch or cancel).</summary>
    StepSkipped,

    /// <summary>
    /// An LLM function invocation completed. The associated event carries model name,
    /// input/output token counts, and latency for cost-visibility reporting.
    /// </summary>
    LlmCallCompleted,

    /// <summary>The run suspended at a human-approval gate awaiting a decision.</summary>
    RunPaused,

    /// <summary>A human-approval decision was delivered and the run resumed execution.</summary>
    RunResumed,
}
