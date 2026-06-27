# Contract: IWorkflowExecutionOrchestrator

**Feature**: 003-visual-workflow-builder | **Layer**: `DBAIAzure.Core.Interfaces`
**Implementation**: `DBAIAzure.Processes.Pipeline.WorkflowExecutionOrchestrator`

---

## Responsibility

Manages the full lifecycle of a workflow execution run: translates a `WorkflowDefinition`
into a live SK `KernelProcess`, starts execution, streams per-node status events to
subscribers, enforces the per-workflow timeout, and handles cancellation and failure.
One instance of this service is registered as a singleton in DI; it can manage multiple
concurrent workflow runs.

---

## Interface Definition

```csharp
/// <summary>
/// Manages the execution lifecycle for visual workflow runs. Fired on a background thread;
/// notifies subscribers via <see cref="RunUpdated"/> for real-time UI streaming.
/// </summary>
public interface IWorkflowExecutionOrchestrator
{
    /// <summary>
    /// Fired whenever a run's state or any node's execution state changes.
    /// The argument is the <c>runId</c> of the affected run.
    /// Always raised on a background thread — subscribers must use InvokeAsync in Blazor.
    /// </summary>
    event Action<string>? RunUpdated;

    /// <summary>
    /// Starts a new execution of <paramref name="workflow"/>. Translates the plain-language
    /// <paramref name="inputDescription"/> into structured SK process input via the LLM
    /// before starting the process. Returns a stable <c>runId</c> immediately — execution
    /// proceeds on a background thread.
    /// </summary>
    Task<string> StartRunAsync(
        WorkflowDefinition workflow,
        string inputDescription,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current execution state for the given run, or null if the run is unknown.
    /// </summary>
    WorkflowExecutionRun? GetRun(string runId);

    /// <summary>
    /// Requests a graceful stop of the running workflow. In-flight agentic steps complete
    /// their current unit of work before stopping; all pending nodes are marked Skipped.
    /// No-ops if the run has already completed or is not found.
    /// </summary>
    void RequestStop(string runId);

    /// <summary>
    /// Submits the human approval decision for a run that is paused at a <c>HumanApproval</c>
    /// node. <paramref name="approved"/> true resumes the process; false marks the node
    /// as failed and skips downstream nodes.
    /// </summary>
    void SubmitApproval(string runId, bool approved);
}
```

---

## WorkflowExecutionRun (read model)

```csharp
/// <summary>
/// Immutable snapshot of an in-progress or completed workflow run.
/// The orchestrator replaces the whole record atomically on each state change.
/// </summary>
public sealed record WorkflowExecutionRun(
    string RunId,
    Guid WorkflowId,
    WorkflowRunStatus Status,
    IReadOnlyList<NodeExecutionState> NodeStates,
    string? FailureReason,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt
);
```

---

## Contracts and Invariants

1. **Run ID stability**: The `runId` returned by `StartRunAsync` is stable for the lifetime
   of the run. Once a `runId` is issued, it never changes or is reused.
2. **Background execution**: `StartRunAsync` returns as soon as the run is queued — it never
   blocks until execution completes.
3. **RunUpdated frequency**: The event fires at minimum once per node state transition and
   once on run status change. It must not fire more than 10 times per second per run to
   avoid flooding the Blazor renderer.
4. **Timeout enforcement**: If execution has not completed within `workflow.Settings.ExecutionTimeoutMinutes`,
   the orchestrator stops the process, marks all running/pending nodes as `TimedOut`, sets
   the run status to `WorkflowRunStatus.TimedOut`, and fires `RunUpdated`.
5. **Graceful stop**: `RequestStop` must not forcibly terminate; it signals the process to
   stop after the current agentic step completes. The run reaches `Cancelled` status within
   the time required to complete the in-flight step (max: the configured timeout).
6. **Failure isolation**: A failed node must not throw an unhandled exception that terminates
   the orchestrator process. The failure is captured, the node is marked `Failed`, downstream
   nodes are marked `Skipped`, and the run transitions to `WorkflowRunStatus.Failed`.
7. **HumanApproval pausing**: A run reaching a `HumanApproval` node transitions to
   `WorkflowRunStatus.Paused`. `RunUpdated` fires. The run resumes only via `SubmitApproval`.

---

## Interaction with WorkflowRuntimeBuilder

The orchestrator delegates construction of the `KernelProcess` to `WorkflowRuntimeBuilder`:

```
StartRunAsync(workflow, inputDescription)
  │
  ├── WorkflowInputTranslator.TranslateAsync(inputDescription, workflow.Nodes)
  │     → structured ProcessInput record
  │
  ├── WorkflowRuntimeBuilder.Build(workflow)
  │     → KernelProcess (ProcessBuilder-constructed from node/edge list)
  │
  └── LocalKernelProcessFactory.RunToEndAsync(process, kernel, startEvent, timeout)
```

---

## Test Obligations

- Unit tests mock `WorkflowRuntimeBuilder` to return a minimal process.
- `RequestStop` test: confirm all pending nodes transition to `Skipped` within 1 second.
- `SubmitApproval(false)` test: confirm run reaches `Failed` and downstream nodes are `Skipped`.
- Timeout test: set a 1-second timeout workflow and confirm `TimedOut` status.
- `RunUpdated` event test: confirm it fires on each node state change.
