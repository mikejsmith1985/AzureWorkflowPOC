# Tasks: Accurate AI Usage Telemetry Capture

## Status (reconciled 2026-08-31)

**Shipped.** The only open item is **T036**, a live ADO write-back round-trip (telemetry → work-item fields on
a real organization) — verification, and it needs a live ADO target. Tracked alongside spec-009-ado and
spec-017 T034.

---

**Feature**: `specs/016-llm-telemetry-capture` · **Branch**: `feature/016-llm-telemetry-capture`
**Inputs**: [plan.md](./plan.md), [spec.md](./spec.md), [data-model.md](./data-model.md),
[contracts/llm-telemetry-capture.md](./contracts/llm-telemetry-capture.md), [research.md](./research.md)

TDD per Constitution Article V: each story's tests precede its implementation. `[P]` = parallelizable
(different files, no incomplete dependencies). Story labels map to the user-value increments below.

- **US1 (P1)** — Real capture: tokens + model actually recorded on both runner and phase-handler paths.
- **US2 (P2)** — Cache metrics: cache tokens, derived cache-hit rate, cache-aware cost.
- **US3 (P3)** — AI error count.

> Resequenced after `/speckit-analyze` to fold in three remediations: **C1** (T015 — persist new event
> fields via the observer), **I1** (T017 — run-context placement), **U1** (T011 — non-blocking test).

---

## Phase 1: Setup

- [x] T001 Move aside any existing dev DB so the additive columns provision cleanly: rename
  `src/DBAIAzure.Web/pipeline.db*` to `*.bak` (schema is created by `EnsureCreated`, not migrations).

## Phase 2: Foundational (blocking prerequisites for all stories)

- [x] T002 [P] Create `LlmUsage` record (model, input/output/cacheRead/cacheCreation tokens, IsError,
  DurationMs) in `src/DBAIAzure.Core/Models/AdoTelemetry/LlmUsage.cs`.
- [x] T003 [P] Create `ILlmUsageReporter` (`void Report(LlmUsage usage)`, must not throw) in
  `src/DBAIAzure.Core/Interfaces/ILlmUsageReporter.cs`.
- [x] T004 [P] Create `LlmRunContext` (`AsyncLocal<string?> CurrentRunId`) in
  `src/DBAIAzure.Core/Diagnostics/LlmRunContext.cs`.
- [x] T005 Add `LlmCacheReadTokens`/`LlmCacheCreationTokens` (`int?`) to
  `src/DBAIAzure.Core/Models/WorkflowExecutionEvent.cs`.
- [x] T006 Mirror the two nullable columns on
  `src/DBAIAzure.Storage/Entities/WorkflowExecutionEventEntity.cs`.
- [x] T007 Extend `LlmTelemetrySample` with `CacheReadTokens`, `CacheCreationTokens`, `IsError` in
  `src/DBAIAzure.Core/Models/AdoTelemetry/RunTelemetryAggregate.cs`.

---

## Phase 3: US1 — Real capture (tokens + model) on both paths (P1) 🎯 MVP

**Goal**: After an AI-backed run, model + input/output tokens are recorded (no longer empty) for both
the workflow runner and the phase-handler validation, and flow to the work item via #42.
**Independent test**: run an AgenticReason workflow → Run History shows model + tokens; a phase run
emits an `LlmCallCompleted` event tagged with the phase run id.

### Tests (write first)
- [x] T008 [P] [US1] Unit test: Anthropic `usage` (incl. both cache fields) + `model` parsed from a
  canned response body, in `tests/DBAIAzure.Tests/AnthropicUsageParseTests.cs`.
- [x] T009 [P] [US1] Unit test: connector calls `ILlmUsageReporter.Report` with correct `LlmUsage` on a
  success response (fake reporter + stubbed `HttpMessageHandler`), in
  `tests/DBAIAzure.Tests/AnthropicUsageReporterTests.cs`.
- [x] T010 [P] [US1] Unit test: Web `LlmUsageReporter` records a `WorkflowExecutionEvent` with the
  ambient run id + tokens via the observer fan-out, in
  `tests/DBAIAzure.Tests/AdoTelemetry/LlmUsageReporterTests.cs`.
- [x] T011 [P] [US1] **(U1)** Unit test: a throwing observer/reporter does NOT bubble out of the call
  path (FR-010 non-blocking) — `LlmUsageReporter.Report` swallows exceptions; assert the connector call
  still returns, in `tests/DBAIAzure.Tests/AdoTelemetry/LlmUsageReporterTests.cs`.

### Implementation
- [x] T012 [US1] Add `usage` + `model` to the Anthropic wire model and parse them in
  `src/DBAIAzure.Connectors/AnthropicChatCompletionService.cs` (both `GetChatMessageContentsAsync` and
  `GetStructuredAsync`). Do NOT populate `ChatMessageContent.Metadata["Usage"]` — the connector-level
  reporter is the single capture point; populating metadata would re-activate the dormant
  `WorkflowFunctionInvocationFilter` and double-emit events.
- [x] T013 [US1] Add optional `ILlmUsageReporter? usageReporter` ctor param to
  `AnthropicChatCompletionService` and call `Report(...)` after each successful response (null → no-op).
- [x] T014 [P] [US1] Implement `LlmUsageReporter` (records a `WorkflowExecutionEvent` via
  `IEnumerable<IWorkflowObserver>`, run id from `LlmRunContext`, never throws) in
  `src/DBAIAzure.Web/Services/LlmUsageReporter.cs`.
- [x] T015 [US1] **(C1)** Update the event→entity write mapping in
  `src/DBAIAzure.Web/Services/SqlWorkflowObserver.cs` (and any other event-persisting mapper) to copy
  `LlmCacheReadTokens`/`LlmCacheCreationTokens`, so the new fields actually persist for aggregation.
- [x] T016 [US1] Set `LlmRunContext.CurrentRunId` in the workflow runner
  (`src/DBAIAzure.Processes/Pipeline/WorkflowExecutionOrchestrator.cs`) and migrate
  `WorkflowFunctionInvocationFilter` to read `LlmRunContext` (its never-set static `CurrentRunId` field
  is removed; nothing else referenced it → no breakage).
- [x] T017 [US1] **(I1)** Set `LlmRunContext.CurrentRunId = state.RunId` in `PhaseHandlerOrchestrator`
  **immediately before the phase process is executed** (not at kernel-build time), so the `AsyncLocal`
  flows to the validation call; clear/restore it after. File: the phase-handler orchestrator in
  `src/DBAIAzure.Processes/` (with DI wiring in `src/DBAIAzure.Web/Program.cs`).
- [x] T018 [US1] Register `ILlmUsageReporter` and inject it into the `AnthropicChatCompletionService`
  registrations (runner kernel + phase-handler kernel) in `src/DBAIAzure.Web/Program.cs`.
- [x] T019 [US1] Widen `SqlRunTelemetrySource` query to all `LlmCallCompleted` events for the run and
  project the new cache columns + error outcome into `LlmTelemetrySample`, in
  `src/DBAIAzure.Storage/Repositories/SqlRunTelemetrySource.cs`.
- [x] T020 [US1] Update `RunTelemetryAggregate.FromSamples` so `LlmCallCount` counts non-error samples
  and model selection ignores error samples, in
  `src/DBAIAzure.Core/Models/AdoTelemetry/RunTelemetryAggregate.cs`.

---

## Phase 4: US2 — Cache metrics (P2)

**Goal**: Work item shows AI Cache Tokens + AI Cache Hit Rate %, and estimated cost reflects cache use.
**Independent test**: a cache-using run → `AICacheTokens` > 0 and `AICacheHitRatePct` populated on the
work item; cost differs from the no-cache estimate.

### Tests (write first)
- [x] T021 [P] [US2] Unit test: `FromSamples` sums cache-read/creation and derives
  `CacheHitRatePct = cache_read/(cache_read+input)×100`, null when total input = 0, in
  `tests/DBAIAzure.Tests/AdoTelemetry/RunTelemetryAggregateTests.cs`.
- [x] T022 [P] [US2] Unit test: `ModelPricing.EstimateCostUsd` includes cache-read + cache-write
  contributions, in `tests/DBAIAzure.Tests/AdoTelemetry/ModelPricingTests.cs`.
- [x] T023 [P] [US2] Unit test: `TelemetryWriteBackService` writes `Custom.AICacheTokens` +
  `Custom.AICacheHitRatePct` when sourced, omits them at zero, in
  `tests/DBAIAzure.Tests/AdoTelemetry/TelemetryWriteBackServiceTests.cs`.

### Implementation
- [x] T024 [US2] Add `CacheReadTokens`, `CacheCreationTokens`, derived `CacheHitRatePct` to
  `RunTelemetryAggregate` (+ `FromSamples`) in
  `src/DBAIAzure.Core/Models/AdoTelemetry/RunTelemetryAggregate.cs`.
- [x] T025 [US2] Extend `ModelPricing` with per-tier cache-read/write rates and the cache token params
  in `src/DBAIAzure.Core/Models/AdoTelemetry/ModelPricing.cs`.
- [x] T026 [US2] Map `Custom.AICacheTokens` (= cache-read) and `Custom.AICacheHitRatePct` in
  `BuildFieldValues`, and pass cache tokens to the cost call, in
  `src/DBAIAzure.Web/Services/TelemetryWriteBackService.cs`.

---

## Phase 5: US3 — AI error count (P3)

**Goal**: Work item shows AI API Errors; a failed AI call is counted without crashing the run.
**Independent test**: trigger a run with a bad key → `LlmCallCompleted` event with `Outcome="error"`,
run completes, `AIAPIErrors` ≥ 1 on a produced work item.

### Tests (write first)
- [x] T027 [P] [US3] Unit test: connector reports `LlmUsage{IsError=true}` on a non-2xx response then
  rethrows, in `tests/DBAIAzure.Tests/AnthropicUsageReporterTests.cs`.
- [x] T028 [P] [US3] Unit test: `FromSamples` sets `ErrorCount` from error samples; `TelemetryWriteBackService`
  writes `Custom.AIAPIErrors` when > 0, in the respective AdoTelemetry test files.

### Implementation
- [x] T029 [US3] Report `LlmUsage{IsError=true}` on non-success/exception (both call paths) before
  rethrowing, in `src/DBAIAzure.Connectors/AnthropicChatCompletionService.cs`.
- [x] T030 [US3] Add `ErrorCount` to `RunTelemetryAggregate.FromSamples` in
  `src/DBAIAzure.Core/Models/AdoTelemetry/RunTelemetryAggregate.cs`.
- [x] T031 [US3] Map `Custom.AIAPIErrors` (when > 0) in
  `src/DBAIAzure.Web/Services/TelemetryWriteBackService.cs`.

---

## Phase 6: Polish & Cross-Cutting

- [x] T032 [P] Update `CHANGELOG.md` under `[Unreleased]` (capture fix + cache/error fields).
- [x] T033 Run the full unit suite (`dotnet test tests/DBAIAzure.Tests`) — confirm green except the
  known pre-existing `ConnectorSettings_WhenSaveClicked` bUnit failure.
- [x] T034 Code-quality pass against the constitution (naming, XML docs, guard clauses, <40-line
  methods, no suppressed nullable warnings) across all changed files.
- [x] T035 Live capture verification (Scenarios A/B core) — `AnthropicUsageCaptureIntegrationTests`
  drives the real connector with the vaulted Anthropic key and asserts real tokens + model are
  captured. **Passed** against the live API (Article X evidence for FR-001/FR-002/SC-002).
- [ ] T036 Live ADO write-back round-trip (Scenarios C/D) — telemetry → work item fields on a real
  phase run. **Deferred**: needs configured ADO/LLM connectors + a phase pipeline writing real work
  items (and ideally the Azure site, currently billing-blocked). Logic is unit-covered; this is the
  remaining end-to-end proof.

---

## Dependencies & Order

- **Setup (T001)** → **Foundational (T002–T007)** → stories.
- **US1 (T008–T020)** is the MVP and unblocks US2/US3 (they extend the same aggregate/write-back/connector).
- Within US1, the persistence chain must be coherent: T012/T013 (capture) → T014 (reporter) →
  **T015 (observer mapping persists new fields, C1)** → T019 (query reads them). T016/T017 (run-context)
  gate correct run correlation; T017 must set the `AsyncLocal` at process-invocation time (I1).
- **US2 (T021–T026)** and **US3 (T027–T031)** are independent of each other but both edit
  `RunTelemetryAggregate.cs` and `TelemetryWriteBackService.cs` — sequence those shared-file edits.
- **Polish (T032–T035)** last.

## Parallel Opportunities

- Foundational: T002, T003, T004 in parallel (distinct new files).
- US1 tests: T008, T009, T010, T011 in parallel; T014 in parallel with T012/T013 (different files).
- US2 tests: T021, T022, T023 in parallel.

## MVP Scope

**US1 only** (T001–T020): makes telemetry real — model + input/output tokens captured on both paths,
persisted (incl. the C1 observer fix), and written to the work item. Delivers immediate value (the
fields stop being empty) before cache/error.
