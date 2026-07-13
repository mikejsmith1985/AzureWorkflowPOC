# Phase 1 Data Model: MAF Modernization

**Feature**: `specs/019-maf-modernization` · **Date**: 2026-07-12

This migration is **presentation-preserving**: the persisted run/cost data shapes are largely unchanged.
The model below records what stays, what is added (checkpoints, provider config), and the state
transitions that must behave identically after the framework swap.

## Entities

### Pipeline (Workflow)
A runnable orchestration. **Migrates** from an SK `KernelProcess` graph to a MAF `Workflow` built by
`WorkflowBuilder`. Three instances: `intake`, `phase-handler`, `visual-workflow`.
- Fields (conceptual): `id`, `kind`, node/executor set, edges (incl. switch/port routing), request ports.
- The visual pipeline is still **built at runtime** from a persisted `WorkflowDefinition` (unchanged
  storage) → `WorkflowBuilder` (was `WorkflowRuntimeBuilder`).

### Executor (Node / Step)
A unit of work. **Migrates** `KernelProcessStep` / `KernelProcessStep<TState>` → MAF `Executor` /
`Executor<TInput>`. Stateful executors persist via `OnCheckpointingAsync` / `OnCheckpointRestoredAsync`.
- Node kinds preserved: agentic, route (switch), transform, notify, data, human-approval, terminal-create.
- **Port label** (routing): preserved as a typed **enum** carried on the executor's output record; routing
  uses `AddSwitch(...AddCase(msg => msg.Port == X, target)...WithDefault(...))` (was event-id strings).

### Run
A single execution instance. **Storage shape preserved.**
- Fields (unchanged): `runId`, `status`, history/step records, snapshots, token/cost records, `pausedState`.
- **Added**: association to a **Checkpoint** (below) so a paused run resumes in place on MAF.
- States: `Running → Paused(awaiting request) → Running → Completed | Failed | Rejected/Escalated`.
  Transitions must match today, including timeout auto-resolution.

### Checkpoint (new)
A serialized MAF workflow snapshot at a superstep boundary. Persisted by the new `ICheckpointStore`.
- Captures: executor state, pending messages, **pending `RequestPort` requests**, shared state.
- Backed by EF Core (SQLite/SQL Server); keyed by `runId`.
- **Migration record (one-time)**: SK-paused runs are converted into a Checkpoint with the outstanding
  request reconstructed (D4/FR-006a).

### Approval / Request item
A pending human decision that suspends a run. **Migrates** proxy-step + external channel → MAF
`RequestPort` request surfaced as `RequestInfoEvent`; resolved via `SendResponseAsync`.
- Surfaces preserved: intake console prompt, phase-handler approval card, visual-builder **Review Queue**.
- App-level Review Queue, SignalR run updates, and `TaskCompletionSource` gating are retained as the host
  layer; only the underlying SK plumbing changes.

### Cost / Telemetry record
Token usage + computed cost bound to a run and work item (spec-016/017). **Data preserved; capture seam
moves** from SK filters to a `DelegatingChatClient` reading `ChatResponse.Usage` (D8).
- **Added field**: `provider` + `model` tags on each usage record (FR-009e), so accounting is correct
  across BYO-AI providers.
- Ledger, binding key, and ingest are reused unchanged.

### AI Provider Configuration (new — BYO-AI)
Selects the active model provider/model per deployment instance.
- Fields: `activeProviderId` (default `anthropic`), `model`, and per-provider secret **references**
  (resolved from config/vault, never stored inline — FR-009c).
- One active provider per instance (Clarifications Q4). Unknown/misconfigured provider → fail-loud,
  naming the provider; no silent fallback (FR-009d).
- Registry: `providerId → IChatClientProvider` factory; adding a provider = a factory + config, no
  pipeline/step change (FR-009b).

## Relationships

```
Pipeline 1───* Executor 1───* (edges/switch) ──> Executor
Run *───1 Pipeline
Run 1───0..1 Checkpoint            (paused/in-flight runs)
Run 1───* Cost/Telemetry record ──> tagged (provider, model)
Run 0..1 Request item              (when awaiting human)
AI Provider Configuration 1 (active) ──> IChatClient  (consumed by every LLM-using executor/service)
```

## Validation rules (from requirements)

- A paused Run MUST be resumable after restart (FR-006) and after cutover (FR-006a) with no lost approval.
- Route/switch selection MUST pick the same target as today for equivalent inputs (FR-002/FR-004).
- Structured outputs (RouteDecision, node realization) MUST deserialize to the same typed records (FR-011).
- Every LLM-using code path MUST depend only on `IChatClient`, never a provider-specific client (FR-008).
- Token/cost records MUST equal the pre-migration build for equivalent runs (SC-004) and carry provider/model.
