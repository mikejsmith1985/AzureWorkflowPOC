# Contract: Workflow / Executor Mapping (SK Process Framework → MAF Workflows)

**Owner**: `DBAIAzure.Processes`. **Basis**: research.md D1.

## Rules

- `ProcessBuilder` → `WorkflowBuilder(startExecutor)`; `.Build()` validates reachability/types.
- `KernelProcessStep[<TState>]` → `Executor[<TInput>]`; handler via `[MessageHandler]` or
  `HandleAsync(msg, IWorkflowContext, ct)`; emit with `context.SendMessageAsync` / `YieldOutputAsync`
  (replaces `EmitEventAsync`).
- `AddStepFromType<T>()` → executor **instances** passed to the builder (DI-constructed).
- **Edges**: `.OnEvent("id").SendEventTo(step)` → `AddEdge(src, tgt, condition:)`.
- **Port-label routing** (the app's `KnownPortLabels`): executor returns a typed record carrying a
  **route enum**; wire with `AddSwitch(src, sb => sb.AddCase(m => m.Port == X, tgtX)....WithDefault(tgtN))`.
  First matching case wins; a default is mandatory.
- **Runtime**: `LocalKernelProcessFactory.RunToEndAsync` → `InProcessExecution.RunStreamingAsync` (and
  `ResumeStreamingAsync` for restore). Orchestrators watch the event stream (`WatchStreamAsync`).
- **LLM inside an executor**: inject the active `IChatClient` (or a `ChatClientAgent`) and call it in the
  handler; structured output via `ChatResponseFormat.ForJsonSchema<T>()` (see cost-telemetry + provider contracts).

## Per-pipeline
- `IntakePipelineBuilder`, `PhaseHandlerPipelineBuilder` → static `WorkflowBuilder` graphs.
- `WorkflowRuntimeBuilder` → builds a `Workflow` at runtime from the persisted `WorkflowDefinition`
  (node→executor, edge→`AddEdge`/`AddSwitch`, human-approval node→`RequestPort`). `PortLabelsByNodeId`
  preserved as the switch cases.

## Acceptance
- Same steps execute, same routing target chosen, same work items created, same run history for
  equivalent inputs (FR-002/FR-004; parity tests per pipeline).
