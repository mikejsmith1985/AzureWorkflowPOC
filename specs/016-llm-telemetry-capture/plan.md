# Implementation Plan: Accurate AI Usage Telemetry Capture

**Feature**: `specs/016-llm-telemetry-capture` · **Branch**: `feature/016-llm-telemetry-capture`
**Spec**: [spec.md](./spec.md) · **Created**: 2026-06-29

## Summary

Make per-call AI telemetry real (it is silently empty today because the Anthropic connector discards
the `usage` block) and extend it with cache tokens, a derived cache-hit rate, and an AI error count —
written onto the run's ADO work item by the existing write-back (#42). Capture must cover **both** the
workflow-runner AI calls and the phase-handler validation AI calls.

## Technical Context

- **Language/runtime**: C# / .NET 8; Semantic Kernel Process Framework.
- **LLM connector**: `AnthropicChatCompletionService` (`DBAIAzure.Connectors`) — direct HttpClient to the
  Anthropic Messages API; implements `IChatCompletionService` (runner path) and
  `IStructuredCompletionService` (phase-handler validation path).
- **Telemetry persistence**: `WorkflowExecutionEvent` → `WorkflowExecutionEventEntity` →
  `WorkflowExecutionEvents` table (SQLite/EF Core, `PipelineDbContext`). Schema is provisioned via
  `EnsureCreated` (no migrations).
- **Existing capture seam**: `WorkflowFunctionInvocationFilter` (reads `Result.Metadata["Usage"]`
  reflectively) — fires only for kernel function invocations.
- **Existing aggregate + write-back (#42)**: `IRunTelemetrySource`/`SqlRunTelemetrySource`,
  `RunTelemetryAggregate`, `ModelPricing`, `TelemetryWriteBackService`, `IBoardsClient.UpdateFieldsAsync`.
- **Run-id correlation**: `WorkflowFunctionInvocationFilter.CurrentRunId` (`AsyncLocal`) — set by the
  workflow orchestrator only; **not** set for phase-handler runs.

## Constitution Check

| Article | Gate | Verdict |
|---------|------|---------|
| I — Best route | No quick-but-dirty; fix the root cause (usage discarded), not a patch | ✅ Plan fixes capture at the connector |
| IV — Code quality | Naming, guard clauses, XML docs, <40-line methods, nullable honored | ✅ Enforced in tasks |
| V — Testing (TDD) | Unit tests first; pure parsing/aggregation/derivation are 100% mocked | ✅ See contracts + quickstart |
| VII — **Framework-First** | SK provides function-invocation filters; do not hand-roll capture for kernel calls. **Documented gap**: `IStructuredCompletionService.GetStructuredAsync` is a *direct* service call, not a kernel function, so an SK filter cannot observe it. | ✅ Justified custom seam (below) |
| IX — Secrets | No secret values in telemetry or logs | ✅ Only tokens/model/counts |
| X — Verification | Evidence via tests + a real round-trip in quickstart | ✅ |

**Framework-First justification (recorded at the seam):** Semantic Kernel's `IFunctionInvocationFilter`
only observes kernel *function* invocations. The phase-handler validation uses
`IStructuredCompletionService.GetStructuredAsync` — a direct service call the kernel never wraps — so no
SK filter can capture its usage. We therefore introduce a thin **`ILlmUsageReporter`** seam that the
connector calls after every Messages API response (chat *and* structured). This is the single point
where the raw `usage` is parsed, so it captures all paths uniformly and supersedes the reflective
filter for token data (which never worked against this connector). The reporter is optional on the
connector (null → no-op), keeping `DBAIAzure.Connectors` free of an observer dependency.

## Approach

1. **Surface usage at the connector.** Add a `usage` shape to the Anthropic wire model and parse
   `input_tokens`, `output_tokens`, `cache_read_input_tokens`, `cache_creation_input_tokens`, plus the
   response `model`. After each call (success or failure), invoke an optional injected
   `ILlmUsageReporter` with an `LlmUsage` record (model, the four token counts, `IsError`, duration).
2. **Record as run-correlated events.** The Web-layer `ILlmUsageReporter` implementation writes a
   `WorkflowExecutionEvent` (via the existing `IWorkflowObserver` fan-out) tagged with the ambient run
   id. Generalize the ambient run-id (`LlmRunContext.CurrentRunId`) and set it in **both** the workflow
   orchestrator and the phase-handler orchestrator before their kernels run.
3. **Persist new fields.** Add `LlmCacheReadTokens` + `LlmCacheCreationTokens` to the event model and
   entity; errors are recorded as events with `Outcome = "error"` (existing field).
4. **Aggregate.** Extend `RunTelemetryAggregate` with cache-read, cache-creation, and error count; widen
   `SqlRunTelemetrySource`'s query to include all `LlmCallCompleted` events (not just those with tokens)
   so error-only events are counted.
5. **Write back.** `TelemetryWriteBackService` adds `Custom.AICacheTokens` (= cache-read),
   `Custom.AICacheHitRatePct` (derived), and `Custom.AIAPIErrors`. `ModelPricing` adds cache read/write
   rates so cost reflects cache usage. Tool-accept rate remains unwritten (out of scope, FR-009).

## Phase 0 — Research

See [research.md](./research.md). All Technical Context items resolved; no open NEEDS CLARIFICATION.

## Phase 1 — Design & Contracts

- [data-model.md](./data-model.md) — event/aggregate/usage field changes.
- [contracts/llm-telemetry-capture.md](./contracts/llm-telemetry-capture.md) — `ILlmUsageReporter`,
  `LlmUsage`, the connector seam, and the extended write-back field set.
- [quickstart.md](./quickstart.md) — how to verify capture end-to-end.

## Post-Design Constitution Re-check

No new violations. The single custom seam (`ILlmUsageReporter`) is justified against the documented SK
gap and recorded at the component. Schema additions are additive; `EnsureCreated` recreates dev stores
(noted in spec Assumptions). **Gate: PASS.**

## Next

`/speckit-tasks` to generate the dependency-ordered `tasks.md`.
