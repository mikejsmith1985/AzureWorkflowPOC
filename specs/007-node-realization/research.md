# Phase 0 Research: Node Realization

All spec-level clarifications were resolved in the 2026-06-22 session (see spec **Clarifications**).
This document records the **technical** decisions, each grounded in the existing codebase so the
feature reuses framework primitives (Article VII) rather than rebuilding them.

---

## R1. How does the LLM produce a *structured* per-node configuration?

**Decision**: Use the existing `IStructuredCompletionService.GetStructuredAsync<T>(systemPrompt,
userMessage, toolName, toolDescription, inputSchemaJson, ct)` (Anthropic-backed, forced-tool JSON
output) — one call per node, with `T` = the typed config record for that node's type.

**Rationale**: Article VII mandates structured LLM output via a response schema bound to a typed
record — not free-text parsing. `IStructuredCompletionService` already does exactly this (forces
`tool_choice`, deserializes the tool input to `T`). It is registered in the per-run kernel and the
phase handler; the builder also has a singleton `IChatCompletionService`. Reusing it keeps
realization on the sanctioned primitive.

**Alternatives considered**:
- Free-text generation + manual JSON parsing → rejected (violates Article VII, brittle).
- Reuse `IWorkflowCodeGenerator` to emit code → rejected per clarification #1 (realization
  produces *config*, not source; code-gen stays a separate optional export).

---

## R2. Where is realized configuration stored?

**Decision**: Serialize each node's typed config record into the existing
`WorkflowNode.FunctionConfig` (string JSON). Set `IsConfigured = true` on accept. Store realization
**provenance** — a hash of the plain-language inputs (Label + GoalPrompt + connected edge shape)
at acceptance time — in `WorkflowSettings` (new `RealizationProvenance` dictionary, keyed by node
id), mirroring how `WorkflowSettings.DesignSkillAnswers` already persists per-node data.

**Rationale**: `FunctionConfig` is the field the runtime already reads for Trigger nodes
(`{initialDataDescription}`) and is the documented home for per-type config. Reusing it avoids a
node-model schema change/migration. Provenance must persist (for out-of-date detection, FR-13.5 /
US2 #4) but is not runtime config, so `WorkflowSettings` is the right channel — and it already
round-trips as `SettingsJson`.

**Alternatives considered**:
- New fields on `WorkflowNode` (`RealizedConfig`, `RealizationState`) → rejected to avoid model
  churn and a persistence migration; status is derivable, provenance fits in settings.
- A separate realization table → rejected; workflow JSON blobs are the established persistence
  model and keep realized config atomic with the workflow.

---

## R3. How is a node's realization status / a workflow's readiness determined?

**Decision**: Status is **computed**, not stored, from three inputs: (a) `FunctionConfig` present
& schema-valid for the node type + `IsConfigured`; (b) provenance hash == current intent hash
(else *OutOfDate*); (c) connector binding resolvable & healthy (else *Blocked*). A new async
`IWorkflowReadinessService.EvaluateAsync(workflow, ct)` returns a `WorkflowReadinessReport`
(per-node `NodeRealizationStatus` + overall ready/not-ready + plain-language reasons). It composes
the existing sync `IWorkflowValidator` (structural rules) with per-type config validation and
`IConnectorHealthChecker.CheckAllAsync(ct)`.

**Rationale**: The sync `IWorkflowValidator.Validate(definition)` cannot perform the async
connector health check FR-17.3 requires, so a dedicated async readiness service is needed; reusing
the validator for structural rules avoids duplicating VAL-001..003. Computing status avoids a
stored-state-drift class of bugs (cf. the auto-save clobber we just fixed — derive, don't
duplicate).

**Alternatives considered**:
- Cram everything into `IWorkflowValidator` (make it async) → rejected; it's a sync, pure,
  unit-fast contract used by `SaveAsync`, and connector health is a different concern.
- Persist status on each node → rejected (drift risk; status is a pure function of inputs).

---

## R4. How do realized nodes actually *execute*? (the POC-stub gap)

**Decision**: Upgrade the four function-node `KernelProcessStep`s to consume their realized
`FunctionConfig`: `FunctionNotifyStep` resolves its bound `ConnectorType` (Teams/etc.) and sends;
`FunctionDataStep` performs the bound read/write; `FunctionRouteStep` evaluates realized
conditions against upstream structured output to pick the outgoing port; `FunctionTransformStep`
applies the realized field mapping. `AgenticNodeStep` already reads `GoalPrompt`; extend it to
honor the realized model reference and structured-output shape. **Phase the runtime work**:
Agentic + Notify + Route first (sufficient for the demoable triage flow and SC-6), then Data +
Transform.

**Rationale**: The subsystem map shows these steps currently pass-through/log and ignore
`FunctionConfig`. SC-6 ("a production-ready workflow runs through the existing execution flow and
completes") is unverifiable unless the steps consume the config realization produces. Honesty
(Article I/X): producing config that nothing executes would be a quick-but-dirty illusion of
readiness.

**Alternatives considered**:
- Treat Assumption #2 ("runtime already executes given config") as fully true and skip step work →
  rejected; the map disproves it. The spec's Assumption #2 is corrected here.

---

## R5. Whole-workflow vs single-node realization, and progress UX

**Decision**: `IWorkflowRealizationService` exposes both `ProposeAllAsync(workflow, ct)` (yields
per-node proposals, supports live progress) and `ProposeNodeAsync(workflow, nodeId, ct)`
(single-node, US3). Acceptance is explicit per node (`AcceptProposal`), with a "bulk accept" that
still requires one confirmation (FR-16.1). Re-realization of a single node never mutates other
nodes (US3, SC-5).

**Rationale**: Mirrors `WorkflowDesignSkillService`'s per-node question loop and satisfies US1/US3.
Per-node calls keep each LLM request small and reviewable (SC-3) and give natural progress
granularity (FR-13.3).

**Alternatives considered**:
- Single giant prompt realizing all nodes at once → rejected (poor reviewability, no incremental
  re-realization, larger blast radius on error).

---

## R6. Out-of-date detection

**Decision**: On accept, store `intentHash = hash(Label + GoalPrompt + ordered connected-edge
signature)`. At load/evaluate, recompute; mismatch → `OutOfDate`, offering one-click
re-realization of just that node (FR-13.5, US2 #4). Editing graph edges or the goal changes the
hash.

**Rationale**: Deterministic, cheap, and reuses the same "content signature" technique just proven
in the auto-save fix. No timestamps to drift.

**Alternatives considered**:
- Timestamp comparison → rejected (re-saves bump timestamps without intent change; we hit exactly
  this class of bug in auto-save).

---

## R7. Secrets & connector binding boundary

**Decision**: A realized function/notify/data config stores a `ConnectorType` **reference** plus
non-secret routing (recipient field map, operation name). Credentials are never read into the
proposal, the prompt, or `FunctionConfig`; the runtime step resolves secrets at execution time via
`IConnectorConfigRepository.GetDecryptedSecretsAsync` (existing). Missing/unhealthy connector →
`Blocked` (FR-16.3, US4).

**Rationale**: Article IX — the agent/LLM names *where* a secret goes; the vault/connector repo
resolves it. Keeps secrets out of source, logs, prompts, and persisted workflow JSON.

---

## R8. Testing strategy (Article V)

**Decision**:
- **Unit** (mocked `IStructuredCompletionService`): proposal generation per node type; readiness
  rules (valid/blocked/out-of-date/needs-input); config↔`FunctionConfig` round-trip; single-node
  realization isolation.
- **Integration** (real services): a real structured-output call returns a schema-valid config;
  `IConnectorHealthChecker` correctly gates a Notify node when its connector is absent/unhealthy.
- **E2E** (Playwright, `run-e2e.ps1`): build plain-language workflow → "Make it real" → review &
  accept → readiness badge turns ready → run completes; and the "blocked when connector missing"
  path.
- **TDD**: failing tests authored before each behaviour.

**Rationale**: Matches the three-layer separation and the project's proven Playwright E2E harness.
