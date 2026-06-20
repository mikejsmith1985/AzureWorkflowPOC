// Defines execution-state enumerations shared across the workflow pipeline and UI layers.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Represents the lifecycle state of an entire workflow run from creation through terminal resolution.
/// A run transitions forward through these states and never moves backward, ensuring audit integrity.
/// </summary>
public enum WorkflowRunStatus
{
    /// <summary>
    /// The run has been created but no step has been activated yet.
    /// This is the default state immediately after a run record is written.
    /// </summary>
    NotStarted,

    /// <summary>
    /// At least one step is actively executing; the run has not reached a terminal state.
    /// </summary>
    Running,

    /// <summary>
    /// Execution has been suspended awaiting a human-in-the-loop signal or an external event.
    /// The run will resume once the expected signal is delivered via the process channel.
    /// </summary>
    Paused,

    /// <summary>
    /// Every step in the run reached a terminal state successfully; no further work remains.
    /// </summary>
    Completed,

    /// <summary>
    /// One or more steps encountered an unrecoverable error, causing the run to halt.
    /// Inspect the associated <c>ErrorMessage</c> on the run record for the root cause.
    /// </summary>
    Failed,

    /// <summary>
    /// The run exceeded its configured maximum duration and was forcibly terminated by the watchdog.
    /// Steps that were in-flight at cancellation are left in their last-known state.
    /// </summary>
    TimedOut,

    /// <summary>
    /// An operator or the system explicitly requested early termination before the run finished.
    /// Distinguishes intentional stop-requests from failures and time-outs.
    /// </summary>
    Cancelled,
}

/// <summary>
/// Represents the execution state of an individual node (step or gate) within a workflow run.
/// Node states roll up to <see cref="WorkflowRunStatus"/> at the run level.
/// </summary>
public enum NodeStatus
{
    /// <summary>
    /// The node is part of the plan but has not yet been activated by the orchestrator.
    /// All nodes begin in this state when the run is created.
    /// </summary>
    NotStarted,

    /// <summary>
    /// The node is currently executing; its result is not yet available.
    /// </summary>
    Active,

    /// <summary>
    /// The node finished without error and produced its expected output or side-effect.
    /// </summary>
    Completed,

    /// <summary>
    /// The node encountered an error it could not recover from.
    /// The run-level failure reason should reference this node's identifier.
    /// </summary>
    Failed,

    /// <summary>
    /// The node was intentionally bypassed — either because its precondition was not met
    /// or because a conditional branch routed execution around it.
    /// A skipped node does not imply an error.
    /// </summary>
    Skipped,

    /// <summary>
    /// The node exceeded its per-node execution deadline and was terminated by the watchdog.
    /// The parent run will transition to <see cref="WorkflowRunStatus.TimedOut"/> unless
    /// the node is configured as non-critical.
    /// </summary>
    TimedOut,
}
