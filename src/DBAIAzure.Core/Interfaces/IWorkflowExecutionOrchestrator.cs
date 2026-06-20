// Defines the contract for orchestrating workflow execution runs and observing their state.

using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Manages the lifecycle of workflow execution runs: starting them, tracking their state,
/// and routing external signals (stop, approval) back into the running process.
/// </summary>
public interface IWorkflowExecutionOrchestrator
{
    /// <summary>
    /// Fires with the affected runId whenever a run's state changes.
    /// Always raised on a background thread — subscribers must use InvokeAsync in Blazor.
    /// </summary>
    event Action<string>? RunUpdated;

    /// <summary>
    /// Enqueues a new execution of the given workflow against the supplied natural-language
    /// input. Returns a stable runId immediately; execution proceeds on a background thread.
    /// </summary>
    /// <param name="workflow">The workflow definition to execute.</param>
    /// <param name="inputDescription">Natural-language description of the work to perform.</param>
    /// <param name="ct">Token used to cancel the start-up handshake (not the full run).</param>
    /// <returns>A stable, unique identifier for the newly created run.</returns>
    Task<string> StartRunAsync(WorkflowDefinition workflow, string inputDescription, CancellationToken ct = default);

    /// <summary>
    /// Returns an immutable snapshot of the run identified by <paramref name="runId"/>,
    /// or <see langword="null"/> if no such run exists.
    /// </summary>
    WorkflowExecutionRun? GetRun(string runId);

    /// <summary>
    /// Signals the run to stop at its next safe checkpoint. The run transitions to a
    /// terminal status asynchronously; callers should observe <see cref="RunUpdated"/>.
    /// </summary>
    void RequestStop(string runId);

    /// <summary>
    /// Delivers a human-in-the-loop approval decision to a run that is suspended at an
    /// approval gate. Passing <see langword="false"/> causes the run to abort.
    /// </summary>
    void SubmitApproval(string runId, bool approved);
}

/// <summary>
/// Immutable snapshot of an in-progress or completed workflow run.
/// The orchestrator replaces the record atomically on each state change.
/// </summary>
public sealed record WorkflowExecutionRun(
    /// <summary>Stable, unique identifier assigned when the run was created.</summary>
    string RunId,

    /// <summary>Identity of the workflow definition that produced this run.</summary>
    Guid WorkflowId,

    /// <summary>Current lifecycle status of the run.</summary>
    WorkflowRunStatus Status,

    /// <summary>Ordered snapshot of each node's execution state at the time this record was captured.</summary>
    IReadOnlyList<NodeExecutionState> NodeStates,

    /// <summary>Human-readable reason the run failed, or <see langword="null"/> when the run has not failed.</summary>
    string? FailureReason,

    /// <summary>UTC instant at which execution began.</summary>
    DateTimeOffset StartedAt,

    /// <summary>UTC instant at which execution reached a terminal state, or <see langword="null"/> while still running.</summary>
    DateTimeOffset? CompletedAt
);
