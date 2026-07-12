---
description: "Task list for modernizing the agent stack onto Microsoft Agent Framework (MAF)"
---

# Tasks: Modernize the Agent Stack onto Microsoft Agent Framework (MAF)

**Input**: Design documents from `specs/019-maf-modernization/`
**Prerequisites**: plan.md, spec.md, research.md (D1–D10), data-model.md, contracts/ (5), quickstart.md

**Tests**: **INCLUDED** — Constitution Article V mandates TDD (Red→Green), and the spec requires the
existing suite to pass with no test deleted (FR-015 / SC-001). The dominant test type here is
**parity**: assert the migrated code produces the same observable result as the pre-migration build.
Because a live LLM is non-deterministic, parity tests run against a **recorded/stubbed `IChatClient`**
harness (T002a) so equivalence is falsifiable; live calls are reserved for the smoke path.

**Organization**: Tasks are grouped by the six user stories. ⚠️ Unlike a greenfield feature, this is a
**migration**: the stories are more sequential than independent — the shared `IChatClient` seam
(Foundational) blocks everything, and US1 (orchestration) is the MVP that US2 builds on. Story labels
still mark which story each task serves.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- **[Story]**: US1–US6 (Setup/Foundational/Polish carry no story label)
- Absolute intent, project-relative paths. Baseline = the pre-migration build on this branch's parent.

---

## Phase 1: Setup (Shared Infrastructure)

- [ ] T001 [P] Add GA MAF/M.E.AI packages (`Microsoft.Agents.AI`, `Microsoft.Agents.AI.Workflows`, `Microsoft.Extensions.AI`) and the official `Anthropic` SDK to `src/DBAIAzure.Connectors`, `src/DBAIAzure.Processes`, `src/DBAIAzure.Storage`, `src/DBAIAzure.Web`, `src/DBAIAzure.Runner` `.csproj` files; pin versions per `research.md` (NO prerelease packages).
- [ ] T002 [P] Capture the pre-migration **baseline fixtures** (sample intake/phase-handler/visual run outputs, step-history, and token/cost snapshots) under `tests/DBAIAzure.Tests/Parity/Baseline/` for the parity tests to assert against.
- [ ] T003 [P] Add AI provider config records (`AiProviderConfig`, active-provider settings) and OpenTelemetry `SourceNames` constants in `src/DBAIAzure.Core/Models/Ai/`.
- [ ] T002a [P] **[Parity harness]** Build a deterministic **record/replay (or stub) `IChatClient`** test harness in `tests/DBAIAzure.Tests/Parity/RecordedChatClient.cs` that returns fixed, recorded responses (incl. token `UsageDetails` and streaming updates), so every parity test asserts framework equivalence against a **pinned** model output rather than a live, non-deterministic model. All parity tests (T014–T016, T035, T036) consume this harness; live calls are reserved for the smoke path only. *(Resolves analysis finding U1.)*
- [ ] T003a **[Governance — do early]** Amend `.specify/memory/constitution.md` **Article VII** to name Microsoft Agent Framework (Workflows / `RequestPort` / checkpointing / `IChatClient`) as the governing framework in place of the SK Process Framework (FR-017). Done **now**, before any pipeline work, so the governing rule matches the migration in progress. *(Resolves analysis finding C1 — was T053 in Polish.)*

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the provider-neutral `IChatClient` seam every migrated pipeline and LLM call depends on. **⚠️ No user-story work begins until this phase is complete.**

- [ ] T004 [P] Write FAILING unit tests for `AnthropicChatClientProvider` (default Claude → `IChatClient`; model/key from config) in `tests/DBAIAzure.Tests/Ai/AnthropicChatClientProviderTests.cs`.
- [ ] T005 [P] Write FAILING unit tests for `HotReloadChatClient` (per-call re-resolve of active provider/model from configuration) in `tests/DBAIAzure.Tests/Ai/HotReloadChatClientTests.cs`.
- [ ] T006 [P] Write FAILING unit tests for `CostCapturingChatClient` (reads `ChatResponse.Usage`; streaming `UsageContent`; hashes rendered messages; tags provider+model; writes the existing ledger) in `tests/DBAIAzure.Tests/Ai/CostCapturingChatClientTests.cs`.
- [ ] T007 Define `IChatClientProvider` + `IChatClientProviderRegistry` in `src/DBAIAzure.Core/Interfaces/` per `contracts/ichatclient-provider.md`.
- [ ] T008 Implement `AnthropicChatClientProvider` using the official `Anthropic` SDK `.AsIChatClient(model)` in `src/DBAIAzure.Connectors/Ai/AnthropicChatClientProvider.cs`; make T004 pass.
- [ ] T009 Implement `HotReloadChatClient : DelegatingChatClient` in `src/DBAIAzure.Connectors/Ai/HotReloadChatClient.cs`; make T005 pass.
- [ ] T010 Implement `CostCapturingChatClient : DelegatingChatClient` in `src/DBAIAzure.Web/Services/Ai/CostCapturingChatClient.cs`, reusing the existing cost ledger + binding key + ingest; make T006 pass.
- [ ] T011 Re-express `IStructuredCompletionService` atop `IChatClient` (`ChatResponseFormat.ForJsonSchema<T>()` + forced-tool via `ChatOptions.RawRepresentationFactory`) in `src/DBAIAzure.Connectors/Ai/ChatClientStructuredCompletionService.cs`.
- [ ] T012 Compose the `IChatClient` pipeline in DI (`src/DBAIAzure.Web/Program.cs` and `src/DBAIAzure.Runner/Program.cs`): `provider → HotReload → CostCapturing → UseOpenTelemetry(SourceName) → UseFunctionInvocation`; register the provider registry; retire the SK `AnthropicChatCompletionService`/`HotReloadAnthropicService` registrations.
- [ ] T013 Repoint observability: add `.UseOpenTelemetry(SourceName)` and replace `AddSource("Microsoft.SemanticKernel*")` with the MAF/M.E.AI source name(s) on **both** tracer and meter providers (`src/DBAIAzure.Web/Program.cs`, `src/DBAIAzure.Runner/Program.cs`); exporter unchanged.
- [ ] T011a Re-target **all design-time LLM consumers** off SK `IChatCompletionService` onto `IChatClient` (and `IStructuredCompletionService` onto the T011 re-expression): `WorkflowDesignSkillService` (incl. its `[KernelFunction("AnalyseTopology")]` → an `AIFunction`/MAF tool — FR-014), `WorkflowCodeGenerator`, `LlmAvailabilityMonitor`, `WorkflowInputTranslator`, `WorkflowRealizationService` in `src/DBAIAzure.Web/Services/` and `src/DBAIAzure.Processes/`. Ensures **100%** of LLM paths use the modern client (SC-005), not just the pipeline steps. *(Resolves analysis finding G1/G2.)*

**Checkpoint**: **every** model call (pipeline steps + design-time services) flows through `IChatClient` with cost capture + OTel; SK pipelines still run (on the new client) — nothing user-visible changed yet.

---

## Phase 3: User Story 1 — Orchestration on MAF, no behavior change (Priority: P1) 🎯 MVP

**Goal**: all three pipelines execute on MAF Workflows with identical observable behavior.
**Independent Test**: run intake, phase-handler, and a multi-node visual workflow against baseline inputs; same steps, routing, work items, and run history; suite passes.

- [ ] T014 [P] [US1] Write FAILING parity test for the **intake** pipeline (step sequence + output vs. baseline), driven by the T002a recorded `IChatClient` harness, in `tests/DBAIAzure.Tests/Parity/IntakePipelineParityTests.cs`.
- [ ] T015 [P] [US1] Write FAILING parity test for the **phase-handler** pipeline (T002a harness) in `tests/DBAIAzure.Tests/Parity/PhaseHandlerParityTests.cs`.
- [ ] T016 [P] [US1] Write FAILING parity test for the **visual workflow** (agentic/route/transform/notify/data nodes, incl. port routing; T002a harness) in `tests/DBAIAzure.Tests/Parity/WorkflowRuntimeParityTests.cs`.
- [ ] T017 [US1] Convert the stateless steps → MAF `Executor` (Intake, Validation, GapAnalysis, Estimation, Action, ReadArtifacts, PhaseValidation, CreateWorkItem) in `src/DBAIAzure.Processes/Executors/` per `contracts/workflow-executor-mapping.md`.
- [ ] T018 [US1] Convert the stateful graph steps → `Executor<NodeRuntimeConfig>` (AgenticNode, FunctionRoute, FunctionTransform, FunctionNotify, FunctionData) in `src/DBAIAzure.Processes/Executors/`, preserving the route **enum** for switch routing. **Note (finding I1)**: `AdoTelemetryPreflightStep` is used **standalone/preflight**, not in a graph — verify first; if standalone, re-target its LLM/service use (T011a) but do **not** convert it to a workflow executor.
- [ ] T019 [US1] Port `IntakePipelineBuilder` → `WorkflowBuilder` graph (edges + `AddSwitch`) in `src/DBAIAzure.Processes/IntakePipelineBuilder.cs`.
- [ ] T020 [US1] Port `PhaseHandlerPipelineBuilder` → `WorkflowBuilder` graph in `src/DBAIAzure.Processes/PhaseHandlerPipelineBuilder.cs`.
- [ ] T021 [US1] Port `WorkflowRuntimeBuilder` → build a `Workflow` at runtime from `WorkflowDefinition` (node→executor, edge→`AddEdge`/`AddSwitch`, `PortLabelsByNodeId` → switch cases) in `src/DBAIAzure.Processes/Pipeline/WorkflowRuntimeBuilder.cs`.
- [ ] T022 [US1] Rewire the three orchestrators to `InProcessExecution.RunStreamingAsync` + event-stream consumption (non-HITL happy path) in `src/DBAIAzure.Processes/Pipeline/{PipelineOrchestrator,PhaseHandlerOrchestrator,WorkflowExecutionOrchestrator}.cs`.
- [ ] T023 [US1] Make T014–T016 pass; update SK-typed unit assertions to MAF equivalents (do not delete tests — FR-015).

**Checkpoint**: MVP — all three pipelines run on MAF end-to-end (happy path), behavior-equivalent.

---

## Phase 4: User Story 2 — HITL pause/resume + durable checkpoints (Priority: P1)

**Goal**: every human-in-the-loop gate suspends and resumes, including across restart, on MAF.
**Independent Test**: trigger each of the three HITL surfaces; one resumes after an app restart; pre-cutover paused runs auto-migrate.

- [ ] T024 [P] [US2] Write FAILING tests: `RequestPort` HITL suspends and resumes for each surface (intake prompt, phase-handler approval, visual approval) in `tests/DBAIAzure.Tests/Hitl/RequestPortHitlTests.cs`.
- [ ] T025 [P] [US2] Write FAILING test: a paused run resumes in place **across an application restart** (checkpoint rehydration) in `tests/DBAIAzure.Tests/Hitl/CheckpointRestartTests.cs`.
- [ ] T026 [P] [US2] Write FAILING test: the one-time **SK-paused-run → checkpoint migration** converts and resumes a representative record in `tests/DBAIAzure.Tests/Hitl/PausedRunMigrationTests.cs`.
- [ ] T027 [US2] Replace `HumanApprovalStep`/`ApprovalPauseStep`/`HitlPauseStep` + `HitlExternalChannel`/`ApprovalExternalChannel` with `RequestPort` nodes in the three builders per `contracts/hitl-request-response.md`; delete the SK channels/proxy steps.
- [ ] T028 [US2] Bridge the host layer (Review Queue store, `WorkflowRunHub` SignalR, `TaskCompletionSource` gating) to `RequestInfoEvent` / `SendResponseAsync` in `src/DBAIAzure.Web/` and the orchestrators.
- [ ] T029 [US2] Preserve approval timeout / escalation / auto-resolution by resolving the outstanding request with the timeout decision (`src/DBAIAzure.Processes/Pipeline/WorkflowExecutionOrchestrator.cs`, PhaseHandler).
- [ ] T030 [US2] Implement `EfCheckpointStore : ICheckpointStore<JsonElement>` over EF Core in `src/DBAIAzure.Storage/Checkpointing/EfCheckpointStore.cs`; wire `CheckpointManager.CreateJson(...)` per `contracts/checkpoint-store.md`.
- [ ] T031 [US2] Implement `OnCheckpointingAsync` / `OnCheckpointRestoredAsync` for stateful executors in `src/DBAIAzure.Processes/Executors/`.
- [ ] T032 [US2] Update `WorkflowRunRehydrationService` to resume paused runs from checkpoints via `InProcessExecution.ResumeStreamingAsync` in `src/DBAIAzure.Web/Services/`.
- [ ] T033 [US2] Implement the idempotent one-time SK-paused-run → checkpoint migration + startup hook in `src/DBAIAzure.Storage/Checkpointing/SkPausedRunMigration.cs`.
- [ ] T034 [US2] Make T024–T026 pass.

**Checkpoint**: durable HITL works on MAF; paused runs survive restart and cutover.

---

## Phase 5: User Story 3 — AI metering + structured output + streaming parity (Priority: P2)

**Goal**: model access is through `IChatClient` and fully metered; structured output and streaming preserved. *(Core seam built in Foundational; this phase proves parity and re-expresses structured/streaming.)*
**Independent Test**: token/cost equals baseline (0% delta), tagged by provider/model; RouteDecision/realization deserialize identically; Run Detail Stream tab streams tokens.

- [ ] T035 [P] [US3] Write FAILING parity test: captured token counts + computed cost equal the baseline (0% delta) and carry provider+model tags, using the T002a recorded harness (fixed `UsageDetails`), in `tests/DBAIAzure.Tests/Parity/CostParityTests.cs`.
- [ ] T036 [P] [US3] Write FAILING test: structured outputs (`RouteDecision`, node realization) deserialize to identical typed records in `tests/DBAIAzure.Tests/Ai/StructuredOutputParityTests.cs`.
- [ ] T037 [P] [US3] Write FAILING bUnit/E2E test: Run Detail **Stream** tab shows live token streaming in `tests/DBAIAzure.E2ETests/Tests/RunDetailStreamTests.cs`.
- [ ] T038 [US3] Ensure the streaming path (`GetStreamingResponseAsync` → `IAsyncEnumerable<ChatResponseUpdate>`) drives the Stream tab and captures usage from the final update in `src/DBAIAzure.Web/Pages/RunDetail.razor` + streaming service.
- [ ] T039 [US3] Make T035–T037 pass; add a grep gate confirming no execution-path dependency on SK `IChatCompletionService` (SC-005).

**Checkpoint**: metering is identical and provider-tagged; structured output + streaming preserved.

---

## Phase 6: User Story 6 — Bring your own AI (Priority: P2)

**Goal**: provider/model selectable by configuration, Claude default, per-instance, no orchestration change.
**Independent Test**: only-Claude runs OOTB; switching config to a second adapter runs the same flow with zero pipeline/step change; unknown provider fails loud.

- [ ] T040 [P] [US6] Write FAILING tests: active provider selected by config (default `anthropic`); unknown/misconfigured provider throws a provider-named error with no silent fallback in `tests/DBAIAzure.Tests/Ai/ProviderSelectionTests.cs`.
- [ ] T041 [P] [US6] Write FAILING test: switching the active provider (to a second stub `IChatClientProvider`) runs an identical flow with **zero** pipeline/step code change in `tests/DBAIAzure.Tests/Ai/ProviderSwapParityTests.cs`.
- [ ] T042 [US6] Generalize the registry for config-driven per-instance selection (`AI:Provider`/`AI:Model`), resolving each provider's secrets by reference (FR-009c) in `src/DBAIAzure.Connectors/Ai/ChatClientProviderRegistry.cs`.
- [ ] T043 [US6] Implement fail-loud `NamedProviderException` and register a second sample `IChatClientProvider` (proving extensibility with no core change) in `src/DBAIAzure.Connectors/Ai/`.
- [ ] T044 [US6] Make T040–T041 pass.

**Checkpoint**: BYO-AI works by configuration; no other AI subscription required.

---

## Phase 7: User Story 4 — MCP tool delivery through the agent (Priority: P3)

**Goal**: MCP-backed delivery keeps working through MAF's agent/tool model.
**Independent Test**: a workflow whose step delivers via MCP executes the tool call and delivers as before.

- [ ] T045 [P] [US4] Write FAILING test: MCP-backed delivery works via the MAF tool model in `tests/DBAIAzure.Tests/Messaging/McpDeliveryParityTests.cs`.
- [ ] T046 [US4] Re-express MCP tool delivery (`McpMessageGateway`) through the MAF agent/tool model / `IChatClient` tools in `src/DBAIAzure.Web/Services/Messaging/` (the project that owns `McpMessageGateway` today). *(Resolves finding U2 — target project pinned.)*
- [ ] T047 [US4] Make T045 pass.

---

## Phase 8: User Story 5 — Traces reach Azure Monitor (Priority: P3)

**Goal**: orchestration + model-call spans reach Azure Monitor under MAF/M.E.AI sources, no gap. *(Wiring done in T013; this validates.)*
**Independent Test**: a run's spans appear in Azure Monitor sourced from the new framework.

- [ ] T048 [P] [US5] Write validation test: orchestration + model-call activities are emitted under the registered MAF/M.E.AI `SourceName`(s) in `tests/DBAIAzure.Tests/Observability/TelemetrySourceTests.cs`.
- [ ] T049 [US5] Validate the Azure Monitor export end-to-end (quickstart scenario 7): sources registered on tracer+meter, exporter unchanged; make T048 pass.

---

## Phase 9: Polish & Cutover

- [ ] T050 Remove ALL `Microsoft.SemanticKernel*` package references and every `SKEXP0080` pragma across `src/`; add a grep gate proving zero matches (FR-003 / SC-002).
- [ ] T051 Update `src/DBAIAzure.Runner/Program.cs` console host to build via `WorkflowBuilder` and drive the `RequestPort` console HITL loop.
- [ ] T052 [P] Code-quality pass (Article IV) across the new executors/clients (self-documenting names, XML doc comments, guard clauses, <40-line methods).
- [ ] T053 [P] Verify the **Article VII** amendment (done early in T003a) is in place and consistent with the shipped MAF stack; record the interim SK↔MAF **interop-shim inventory + removal conditions** (or assert none remain) in the CHANGELOG entry (FR-016 / SC-007). *(Resolves analysis finding G3; C1 amendment itself moved to T003a.)*
- [ ] T054 [P] Update `CHANGELOG.md` `[Unreleased]` with the modernization entry (FR-017).
- [ ] T055 Performance-budget check: end-to-end run latency + per-model-call overhead within **10%** of baseline on the same host/model (SC-010 / quickstart scenario 9); a larger regression blocks cutover.
- [ ] T056 **Cutover gate**: `dotnet test` + `./scripts/run-e2e.ps1` fully green with no test deleted (SC-001), paused-run migration verified against representative records (SC-009), and the grep gate (T050) clean — authorizes the atomic release.

---

## Dependencies & Execution Order

### Phase dependencies
- **Setup (P1)** → no deps. Includes the **Article VII amendment (T003a, done early)** and the
  **parity harness (T002a)**.
- **Foundational (P2)** → after Setup; **blocks all stories** (the `IChatClient` seam, incl. the
  design-time-consumer migration T011a).
- **US1 (P1)** → after Foundational. The orchestration MVP.
- **US2 (P1)** → after **US1** (needs migrated workflows to attach `RequestPort` + checkpoints).
- **US3 (P2)** → after Foundational (parity of the seam); validation strongest after US1 runs exercise it.
- **US6 (P2)** → after Foundational (generalizes the registry); independent of US1/US2.
- **US4 (P3)** → after Foundational; independent of US1/US2.
- **US5 (P3)** → after T013 (wiring); validation after US1.
- **Polish/Cutover (P9)** → after all desired stories; T050/T055/T056 are the release gates.

### Critical path
Setup → Foundational → **US1** → **US2** → Cutover. US3/US4/US5/US6 attach along the way.

## Parallel Opportunities
- **Setup**: T001 ∥ T002 ∥ T003.
- **Foundational**: T004 ∥ T005 ∥ T006 (failing tests); then T007→T008/T009/T010/T011.
- **Per story**: the `[P]` failing-test tasks are authored together first (e.g., T014 ∥ T015 ∥ T016).
- **Cross-story after Foundational**: US6 (T040–T044) and US4 (T045–T047) can proceed in parallel with US1/US2 since they touch their own files.
- **Polish**: T052 ∥ T053 ∥ T054.

## Implementation Strategy

### MVP first
1. Setup → Foundational (the `IChatClient` seam, proven at the model layer).
2. **US1** — all three pipelines on MAF Workflows (happy path). STOP and validate parity.
3. **US2** — durable HITL + checkpoints + paused-run migration.

### Incremental delivery (off-production, per D10)
Layer US3 (metering/streaming parity) → US6 (BYO-AI) → US4 (MCP) → US5 (observability), validating each
independently, then Polish. **Production cutover is a single atomic release** (T056) — SK is removed and
the paused-run migration ships together; no dual-runtime in production.

## Notes
- `[P]` = different files, no incomplete-task dependency.
- Tests are mandatory (Article V) and **parity-first**: assert equivalence to the pre-migration baseline via the deterministic T002a harness; write them failing first.
- Zero prerelease packages in the execution path (research.md) — Claude via the official `Anthropic` SDK, not `Microsoft.Agents.AI.Anthropic`.
- Article VII amendment (**T003a, done early**) is required because the governing framework changes SK → MAF; T053 later verifies it and records interop-shim removal conditions.
