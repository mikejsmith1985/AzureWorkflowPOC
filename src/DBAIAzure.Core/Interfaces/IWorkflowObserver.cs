// Single write path for all workflow execution events (FR-21.3).

using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Receives step-level and run-level events during workflow execution.
/// Multiple implementations are registered simultaneously; the orchestrator fan-outs to all
/// registered observers. Implementations must never throw — log and swallow all errors
/// so that one failing observer cannot disrupt the run or other observers.
/// </summary>
public interface IWorkflowObserver
{
    /// <summary>
    /// Records a single workflow execution event.
    /// Called on step entry/exit, LLM invocation, run pause, and run resume.
    /// </summary>
    Task RecordEventAsync(WorkflowExecutionEvent evt, CancellationToken ct = default);
}
