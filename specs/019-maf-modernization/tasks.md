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
- [X] T003 [P] Add AI provider config records (`AiProviderConfig`, active-provider settings) and OpenTelemetry `SourceNames` constants in `src/DBAIAzure.Core/Models/Ai/`.
- [X] T002a [P] **[Parity harness]** Build a deterministic **record/replay (or stub) `IChatClient`** test harness in `tests/DBAIAzure.Tests/Parity/RecordedChatClient.cs` that returns fixed, recorded responses (incl. token `UsageDetails` and streaming updates), so every parity test asserts framework equivalence against a **pinned** model output rather than a live, non-deterministic model. All parity tests (T014–T016, T035, T036) consume this harness; live calls are reserved for the smoke path only. *(Resolves analysis finding U1.)*
- [X] T003a **[Governance — do early]** Amend `.specify/memory/constitution.md` **Article VII** to name Microsoft Agent Framework (Workflows / `RequestPort` / checkpointing / `IChatClient`) as the governing framework in place of the SK Process Framework (FR-017). Done **now**, before any pipeline work, so the governing rule matches the migration in progress. *(Resolves analysis finding C1 — was T053 in Polish.)*

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the provider-neutral `IChatClient` seam every migrated pipeline and LLM call depends on. **⚠️ No user-story work begins until this phase is complete.**

- [X] T004 [P] Write FAILING unit tests for `AnthropicChatClientProvider` (default Claude → `IChatClient`; model/key from config) in `tests/DBAIAzure.Tests/Ai/AnthropicChatClientProviderTests.cs`.
- [X] T005 [P] Write FAILING unit tests for `HotReloadChatClient` (per-call re-resolve of active provider/model from configuration) in `tests/DBAIAzure.Tests/Ai/HotReloadChatClientTests.cs`.
- [X] T006 [P] Write FAILING unit tests for `CostCapturingChatClient` (reads `ChatResponse.Usage`; streaming `UsageContent`; hashes rendered messages; tags provider+model; writes the existing ledger) in `tests/DBAIAzure.Tests/Ai/CostCapturingChatClientTests.cs`. *(Provider tag lands with the US3/T035 ledger-schema slice; model+token capture done now.)*
- [X] T007 Define `IChatClientProvider` + `IChatClientProviderRegistry` in `src/DBAIAzure.Core/Interfaces/` per `contracts/ichatclient-provider.md`.
- [X] T008 Implement `AnthropicChatClientProvider` using the official `Anthropic` SDK `.AsIChatClient(model)` in `src/DBAIAzure.Connectors/Ai/AnthropicChatClientProvider.cs`; make T004 pass.
- [X] T009 Implement `HotReloadChatClient : DelegatingChatClient` in `src/DBAIAzure.Connectors/Ai/HotReloadChatClient.cs`; make T005 pass.
- [X] T010 Implement `CostCapturingChatClient : DelegatingChatClient` in `src/DBAIAzure.Web/Services/Ai/CostCapturingChatClient.cs`, reusing the existing cost ledger + binding key + ingest; make T006 pass. *(Feeds the existing `ILlmUsageReporter` → event → downstream ledger, so cost path is unchanged; not yet wired into DI — that is T012, coupled with US1.)*
- [X] T011 Re-express `IStructuredCompletionService` atop `IChatClient` (`ChatResponseFormat.ForJsonSchema(schema, name, desc)` + deserialize `response.Text`) in `src/DBAIAzure.Connectors/Ai/ChatClientStructuredCompletionService.cs`. *(Provider-neutral; forced-tool via `RawRepresentationFactory` is a later Anthropic-native refinement if a provider ignores schema. Not yet wired into DI — T012.)*
- [ ] T012 Compose the `IChatClient` pipeline in DI (`src/DBAIAzure.Web/Program.cs` and `src/DBAIAzure.Runner/Program.cs`): `provider → HotReload → CostCapturing → UseOpenTelemetry(SourceName) → UseFunctionInvocation`; register the provider registry; retire the SK `AnthropicChatCompletionService`/`HotReloadAnthropicService` registrations.
- [X] T013 Repoint observability: the Web `IChatClient` pipeline is wrapped in `OpenTelemetryChatClient(source = AiTelemetrySourceNames.ChatClient)` so model calls emit gen_ai spans; `DBAIAzure.Runner/Program.cs` registers the MAF/M.E.AI sources (`AiTelemetrySourceNames.ChatClient` + `.Agents`) on the tracer provider **alongside** `Microsoft.SemanticKernel*` (kept until the atomic cutover so there's no trace gap), Azure Monitor exporter unchanged.
- [ ] T011a Re-target **all design-time LLM consumers** off SK `IChatCompletionService` onto `IChatClient` (and `IStructuredCompletionService` onto the T011 re-expression): `WorkflowDesignSkillService` (incl. its `[KernelFunction("AnalyseTopology")]` → an `AIFunction`/MAF tool — FR-014), `WorkflowCodeGenerator`, `LlmAvailabilityMonitor`, `WorkflowInputTranslator`, `WorkflowRealizationService` in `src/DBAIAzure.Web/Services/` and `src/DBAIAzure.Processes/`. Ensures **100%** of LLM paths use the modern client (SC-005), not just the pipeline steps. *(Resolves analysis finding G1/G2.)*

**Checkpoint**: **every** model call (pipeline steps + design-time services) flows through `IChatClient` with cost capture + OTel; SK pipelines still run (on the new client) — nothing user-visible changed yet.

---

## Phase 3: User Story 1 — Orchestration on MAF, no behavior change (Priority: P1) 🎯 MVP

**Goal**: all three pipelines execute on MAF Workflows with identical observable behavior.
**Independent Test**: run intake, phase-handler, and a multi-node visual workflow against baseline inputs; same steps, routing, work items, and run history; suite passes.

- [X] T014 [P] [US1] Write FAILING parity test for the **intake** pipeline (step sequence + output vs. baseline), driven by the T002a recorded `IChatClient` harness, in `tests/DBAIAzure.Tests/Parity/IntakePipelineParityTests.cs`. *(Red: ready→Intake/Validation/Estimation/Action; not-ready→Intake/Validation/GapAnalysis/HITL-suspend. Fails on NotImplemented until T017/T019.)*
- [X] T015 [P] [US1] Write FAILING parity test for the **phase-handler** pipeline (T002a harness) in `tests/DBAIAzure.Tests/Parity/PhaseHandlerParityTests.cs`. *(Red: ReadArtifacts/PhaseValidation/Approval-suspend.)*
- [X] T016 [P] [US1] Write FAILING parity test for the **visual workflow** (agentic/route/transform/notify/data nodes, incl. port routing; T002a harness) in `tests/DBAIAzure.Tests/Parity/WorkflowRuntimeParityTests.cs`. *(Red: FunctionRoute routes along the chosen port's conditional edge; port labels captured.)*

> **US1 seam scaffolding in place (Red drivers):** `Pipeline/Maf/{MafExecutorIds, MafIntakeWorkflowFactory, MafPhaseHandlerWorkflowFactory, MafWorkflowRuntimeFactory}` (factories throw `NotImplementedException`), test harness `Parity/MafWorkflowRunner.cs` (runs a `Workflow` via `InProcessExecution`, folds `ExecutorInvokedEvent`/`WorkflowOutputEvent`/`RequestInfoEvent`). MAF Workflows 1.13.0 package added to `DBAIAzure.Processes` + `DBAIAzure.Tests`. Parity tests carry `[Trait("Category","US1Parity")]` so regression runs exclude the intentional Reds. **Next:** T017/T018 executors → T019/T020/T021 graphs → T022 orchestrators → T023 green.
- [X] T017 [US1] Convert the stateless steps → MAF `Executor` in `src/DBAIAzure.Processes/Executors/` per `contracts/workflow-executor-mapping.md`. Intake set (Intake/Validation/Estimation/Action/GapAnalysis + shared `ExecutorLlm`) **and** phase-handler set (ReadArtifacts/PhaseValidation) done, each porting its SK step's exact prompt/parse; routing via `[SendsMessage]`/`[YieldsOutput]` + broadcast/conditional/directed edges. CreateWorkItem executor (post-approval) is US2 (resume).
- [X] T018 [US1] Convert the stateful graph steps → MAF node executors in `src/DBAIAzure.Processes/Executors/`. **All six node types done:** `AgenticNodeExecutor` (LLM), `FunctionRouteExecutor` (directed send to the chosen port's target — GA has no `AddSwitch`), `FunctionTransformExecutor` + `FunctionDataExecutor` (reuse the SK steps' pure helpers), `FunctionNotifyExecutor`, and HumanApproval → a `RequestPort` in `MafWorkflowRuntimeFactory`. Node-chain test (Agentic→Transform→Data→Notify) green. **Note (I1)**: `AdoTelemetryPreflightStep` is standalone/preflight — not converted.
- [X] T019 [US1] Port `IntakePipelineBuilder` → `WorkflowBuilder` graph (edges + labelled **conditional edges** — `AddEdge(src, tgt, condition, label)`; **no `AddSwitch` in GA 1.13.0**, see research D1-reality) in `src/DBAIAzure.Processes/Pipeline/Maf/MafIntakeWorkflowFactory.cs` (stub in place; T014 defines target).
- [X] T020 [US1] Port `PhaseHandlerPipelineBuilder` → `WorkflowBuilder` graph (read→validate→approval `RequestPort`) in `src/DBAIAzure.Processes/Pipeline/Maf/MafPhaseHandlerWorkflowFactory.cs`. *(Create-on-approval + resume edge is US2. T015 green.)*
- [X] T021 [US1] Port `WorkflowRuntimeBuilder` → build a `Workflow` at runtime from `WorkflowDefinition` (node→executor by id, edge→`AddEdge`, route port→**directed send** to the port's target node, terminal→`WithOutputFrom`, `PortLabelsByNodeId` retained) in `src/DBAIAzure.Processes/Pipeline/Maf/MafWorkflowRuntimeFactory.cs`. *(Route/Notify node executors done — T016 green; other node types throw pending T018.)*
- [~] T022 [US1] Rewire the three orchestrators to `InProcessExecution.RunStreamingAsync` + event-stream consumption. **Intake + phase-handler DONE** (flag-gated MAF path via `MafWorkflowExecution` + `MafExecutorServices`; executors self-report progress; intake ready→complete / not-ready→suspend, phase-handler read→validate→approval suspend — full resume is US2). Additive `IChatClient` pipeline in `Program.cs` (provider registry → `HotReloadChatClient` from DB → `CostCapturingChatClient`), `Maf:Enabled` **default off**. **Runtime fix:** `WatchStreamAsync` does not complete at `RunStatus.PendingRequests` on a background thread → `MafWorkflowExecution` breaks the stream on `RequestInfoEvent` (regression-guarded); it also surfaces `ExecutorFailedEvent`/`WorkflowErrorEvent` and caps active execution at 3 min. **Pending:** `WorkflowExecutionOrchestrator` (visual).
- [ ] T023 [US1] Make T014–T016 pass; update SK-typed unit assertions to MAF equivalents (do not delete tests — FR-015).

**Checkpoint**: MVP — all three pipelines run on MAF end-to-end (happy path), behavior-equivalent.

---

## Phase 4: User Story 2 — HITL pause/resume + durable checkpoints (Priority: P1)

**Goal**: every human-in-the-loop gate suspends and resumes, including across restart, on MAF.
**Independent Test**: trigger each of the three HITL surfaces; one resumes after an app restart; pre-cutover paused runs auto-migrate.

- [~] T024 [P] [US2] `RequestPort` HITL suspend **and resume** tests. **Intake + phase-handler DONE**: intake — in-process suspend→answer→`SendResponseAsync`→re-validate→complete (clarification loop, `RequestPort<TicketState,TicketState>` + `hitl→validation` loop edge). Phase-handler — read→validate→suspend at the approval `RequestPort<PhaseHandlerState,PhaseHandlerState>`→resume on the reviewer's decision→`CreateWorkItemExecutor` writes the board on approval (nothing on rejection, FR-006); `PhaseHandlerMafResumeTests` proves both approve→create-Epic and reject→no-write. The board-write logic was extracted into a framework-neutral `PhaseWorkItemWriter` shared by the SK `CreateWorkItemStep` and the MAF `CreateWorkItemExecutor` (identical board state — the 5 SK `CreateWorkItemStepTests` still pass unchanged). **Visual DONE**: `WorkflowExecutionOrchestrator.AwaitApprovalAndResumeAsync` suspends the visual run as `Paused` at a HumanApproval `RequestPort`, arms the run's `ApprovalTcs` + auto-reject watchdog, and on `SubmitApproval` responds with the decided `WorkflowStepData` (`IsApproved` set) and drives to `Completed` (recursing for a second downstream gate) — parity with the SK gate, which continues past the gate carrying the decision. `VisualOrchestratorApprovalResumeTests` proves pause→approve→complete and pause→reject→complete. **All three HITL surfaces now suspend and resume on MAF.**
- [~] T025 [P] [US2] Paused-run resume **across restart** — **core proven**: `EfCheckpointStoreTests.PausedRun_ResumesFromCheckpoint_WithAFreshStore` runs a phase-handler run with EF checkpointing to the approval suspension, then a **fresh store + workflow instance over the same DB** resumes from the checkpoint and re-emits the outstanding request (SC-003). **Pending:** production wiring via the rehydration service (T032).
- [ ] T026 [P] [US2] Write FAILING test: the one-time **SK-paused-run → checkpoint migration** converts and resumes a representative record in `tests/DBAIAzure.Tests/Hitl/PausedRunMigrationTests.cs`.
- [ ] T027 [US2] Replace `HumanApprovalStep`/`ApprovalPauseStep`/`HitlPauseStep` + `HitlExternalChannel`/`ApprovalExternalChannel` with `RequestPort` nodes in the three builders per `contracts/hitl-request-response.md`; delete the SK channels/proxy steps.
- [ ] T028 [US2] Bridge the host layer (Review Queue store, `WorkflowRunHub` SignalR, `TaskCompletionSource` gating) to `RequestInfoEvent` / `SendResponseAsync` in `src/DBAIAzure.Web/` and the orchestrators.
- [ ] T029 [US2] Preserve approval timeout / escalation / auto-resolution by resolving the outstanding request with the timeout decision (`src/DBAIAzure.Processes/Pipeline/WorkflowExecutionOrchestrator.cs`, PhaseHandler).
- [X] T030 [US2] Implement `EfCheckpointStore : JsonCheckpointStore` over EF Core in `src/DBAIAzure.Storage/Checkpointing/EfCheckpointStore.cs` (create/retrieve/index over a `WorkflowCheckpoints` table keyed by session+id, parent-linked); built the manager with `CheckpointManager.CreateJson(store, opts)`. Round-trip + restart-resume tests green. Client-side index ordering (SQLite can't `ORDER BY DateTimeOffset`). MAF Workflows pkg added to `DBAIAzure.Storage`. *(DI/rehydration wiring is T032.)*
- [ ] T031 [US2] Implement `OnCheckpointingAsync` / `OnCheckpointRestoredAsync` for stateful executors in `src/DBAIAzure.Processes/Executors/`.
- [X] T032 [US2] Checkpointing **live** + boot-resume for intake: `MafWorkflowSession` threads a `CheckpointManager` (checkpointed per super-step) + `ResumeAsync`; both orchestrators accept/pass the manager; `Program.cs` registers `EfCheckpointStore` + `CheckpointManager`. `PipelineOrchestrator.RehydratePausedRun(pausedTicket, checkpoint)` resumes a run from its checkpoint (via `EfCheckpointStore.GetLatestCheckpointAsync`, ordered by a new monotonic per-session `Sequence`) and re-enters the clarification loop. `BootResumeTests` proves a run paused before a restart is rehydrated by a **fresh orchestrator** and completes on the answer. Fixed an arming race (don't pre-set AwaitingHuman before the drive loop arms the gate). **Hosted startup service done:** `PausedRunRehydrationService` (registered, `Maf:Enabled`-gated) enumerates persisted AwaitingHuman intake runs, loads each ticket + latest checkpoint, and calls `RehydratePausedRun`; `PausedRunRehydrationServiceTests` proves the full boot path end-to-end (persist paused → restart → service rehydrates → completes on answer). **Phase-handler rehydration DONE**: `PhaseHandlerOrchestrator.RehydratePausedRun(placeholder, checkpoint)` resumes a run paused at the approval gate from its checkpoint (shared `DriveApprovalSessionAsync`; paused state read from the re-emitted request, not local memory), fixed a real arming race (seed the rehydrated run at a non-awaiting status so `AwaitingApproval` is only set after the gate is armed); new `IPhaseRunRepository.ListByStatusAsync` + the startup service now enumerates AwaitingApproval phase runs; `PhaseHandlerBootResumeTests` + `PausedRunRehydrationServiceTests.StartupService_RehydratesPausedPhaseRun_ThatCreatesOnApprove` prove restart→rehydrate→approve→board-write. **Visual rehydration DONE**: `WorkflowExecutionOrchestrator.RehydratePausedRun(record, definition, checkpoint)` truly resumes the visual MAF session from its checkpoint (reloads the `WorkflowDefinition` via new `IWorkflowRepository.GetByIdAsync`, `ResumeAsync` → `AwaitApprovalAndResumeAsync`) — previously it only reconstituted the approval TCS with no live session; the `WorkflowRunRehydrationService` now loads definition + checkpoint and calls it (falls back to reconstitute-only when either is absent). `VisualBootResumeTests` proves restart→rehydrate→approve→complete. **All three HITL surfaces (intake, phase-handler, visual) now rehydrate across an application restart.**
- [~] T033 [US2] SK-paused-run → checkpoint migration — **core done** (`SkPausedRunMigration`): idempotent (skips runs that already have a checkpoint), reconstructs a MAF checkpoint by running a **resume-seed workflow** (`MafIntakeWorkflowFactory.BuildResumeWorkflow` + `IntakeResumeSeedExecutor` forward the paused ticket straight to the HITL port — **no LLM re-run**), logs the outcome, returns the pause `CheckpointInfo`. Test proves SC-009 for intake: convert → idempotent skip → resume → recover the request → answer → complete through the real loop. **Pending:** the deploy-time startup hook that iterates SK-paused records from the repos, and the phase-handler resume-seed (needs its create-on-approval downstream).
- [ ] T034 [US2] Make T024–T026 pass.

**Checkpoint**: durable HITL works on MAF; paused runs survive restart and cutover.

---

## Phase 5: User Story 3 — AI metering + structured output + streaming parity (Priority: P2)

**Goal**: model access is through `IChatClient` and fully metered; structured output and streaming preserved. *(Core seam built in Foundational; this phase proves parity and re-expresses structured/streaming.)*
**Independent Test**: token/cost equals baseline (0% delta), tagged by provider/model; RouteDecision/realization deserialize identically; Run Detail Stream tab streams tokens.

- [X] T035 [P] [US3] Cost/metering parity: `CostParityTests` pins a response's token usage and asserts `CostCapturingChatClient` captures the same input/output/cache tokens + model, so `ModelPricing.EstimateCostUsd` yields an identical cost (0% delta).
- [X] T036 [P] [US3] Structured-output parity: `StructuredOutputParityTests` — `ChatClientStructuredCompletionService` binds `RouteDecision` and `PhaseValidationResult` to identical typed records.
- [X] T037 [P] [US3] `RunDetailStreamTabTests` (bUnit): a MAF intake run streams tokens into `PipelineRun.TokenStream`; rendering `RunDetail` and selecting the **Stream** tab shows the streamed tokens grouped by step. Paired with `PipelineOrchestratorTests.MafRuntime_ReadyTicket_StreamsTokensToRunTokenStream` (data-path proof, streaming request + token reconstruction).
- [X] T038 [US3] Streaming: added `ExecutorLlm.CompleteStreamingAsync` (streams via `IChatClient.GetStreamingResponseAsync`, forwards each chunk to the run-bound `IProgressReporter.ReportToken` → `PipelineRun.TokenStream` → Run Detail Stream tab). The four intake LLM executors (Intake/Validation/GapAnalysis/Estimation) stream — parity with the SK steps that streamed via `GetStreamingChatMessageContentsAsync`; Route/Agentic stayed non-streaming (the SK steps did not report tokens). `CostCapturingChatClient.GetStreamingResponseAsync` still captures usage from the final `UsageContent` (T010) — cost parity holds under streaming.
- [X] T039 [US3] SC-005 gate: `ExecutionPathSkFreeTests` (reflection) asserts no MAF executor/factory depends on SK `IChatCompletionService` and the model executors inject `IChatClient`.

**Checkpoint**: metering is identical and provider-tagged; structured output + streaming preserved.

---

## Phase 6: User Story 6 — Bring your own AI (Priority: P2)

**Goal**: provider/model selectable by configuration, Claude default, per-instance, no orchestration change.
**Independent Test**: only-Claude runs OOTB; switching config to a second adapter runs the same flow with zero pipeline/step change; unknown provider fails loud.

- [X] T040 [P] [US6] `ProviderSelectionTests`: both built-in providers resolve by config id (default `anthropic`); unknown provider throws a provider-named `AiProviderException` with no silent fallback.
- [X] T041 [P] [US6] `ProviderSwapParityTests`: swapping the active provider runs the intake flow with an **identical** executor sequence — zero pipeline/executor code change (SC-008).
- [X] T042 [US6] Config-driven per-instance selection: `Program.cs` reads `AI:Provider` (default anthropic); a non-default provider reads key/model/endpoint from `AI:<Provider>:*` (secret by reference, FR-009c); default keeps the DB hot-reload. `AiProviderConfig` gained an optional `Endpoint`.
- [X] T043 [US6] Second provider: `OpenAiChatClientProvider` (GA `Microsoft.Extensions.AI.OpenAI` — stable, not prerelease) reaches any OpenAI-compatible endpoint (OpenAI/Azure OpenAI/Ollama/LM Studio); registered alongside anthropic with **no** pipeline change. Fail-loud is the existing named `AiProviderException` (no separate `NamedProviderException` needed).
- [X] T044 [US6] T040–T041 green.

**Checkpoint**: BYO-AI works by configuration; no other AI subscription required.

---

## Phase 7: User Story 4 — MCP tool delivery through the agent (Priority: P3)

**Goal**: MCP-backed delivery keeps working through MAF's agent/tool model.
**Independent Test**: a workflow whose step delivers via MCP executes the tool call and delivers as before.

- [X] T045 [P] [US4] `McpDeliveryFromMafPipelineTests`: a MAF intake run suspends at the clarification gate and its HITL notification reaches the MCP send-message tool through the production chain (`MessagingHitlNotifier` → `MessageDelivery` → `IMcpMessageGateway`), recorded by the fake gateway — MCP delivery works from a MAF workflow.
- [X] T046 [US4] **No re-expression needed** (resolves finding U2): `McpMessageGateway` + `MessageDelivery` live in `DBAIAzure.Connectors/Messaging` and are **already framework-neutral** — they call the MCP tool via the official MCP SDK (`ModelContextProtocol.Core`) + HTTP, with **zero** Semantic Kernel coupling. Delivery is a deterministic tool call (not LLM-driven), so there is nothing to move onto the MAF/IChatClient tool model; verification (T045) suffices.
- [X] T047 [US4] T045 green.

---

## Phase 8: User Story 5 — Traces reach Azure Monitor (Priority: P3)

**Goal**: orchestration + model-call spans reach Azure Monitor under MAF/M.E.AI sources, no gap. *(Wiring done in T013; this validates.)*
**Independent Test**: a run's spans appear in Azure Monitor sourced from the new framework.

- [X] T048 [P] [US5] `TelemetrySourceTests`: an `OpenTelemetryChatClient` under `AiTelemetrySourceNames.ChatClient` emits an OTel `Activity` from that source on each model call (captured via `ActivityListener`).
- [X] T049 [US5] Model-call spans flow to the registered MAF/M.E.AI source (T048 green); the Runner tracer provider registers the source + keeps the exporter unchanged (T013). *(Full live Azure Monitor round-trip is a deploy-time smoke check.)*

---

## Phase 9: Polish & Cutover

- [ ] T050 Remove ALL `Microsoft.SemanticKernel*` package references and every `SKEXP0080` pragma across `src/`; add a grep gate proving zero matches (FR-003 / SC-002).
- [ ] T051 Update `src/DBAIAzure.Runner/Program.cs` console host to build via `WorkflowBuilder` and drive the `RequestPort` console HITL loop.
- [X] T052 [P] Code-quality pass (Article IV) on the new executors/clients: the new files carry XML docs + self-documenting names + guard clauses throughout; `MafWorkflowRuntimeFactory.Build` decomposed into `CreateBindings`/`DeclareTerminalOutputs`/`RecordRoutePortLabels` (~20-line orchestrator). Two long methods intentionally left (documented in CHANGELOG): intake `HandleAsync` length is prompt-*data*, and `CostCapturingChatClient.GetStreamingResponseAsync` needs the manual-enumerator pattern. Parity/visual tests green.
- [X] T053 [P] Article VII amendment verified in place (constitution names MAF/Workflows/`RequestPort`/checkpointing/`IChatClient`); recorded the SK↔MAF **interop-shim inventory + removal conditions** as a table in the CHANGELOG `[Unreleased]` entry (flag-gated dual paths, the kernel-container reuse for board-write deps, dual OTel sources, retained SK steps/builders; `PhaseWorkItemWriter` noted as permanent, not a shim). FR-016 / SC-007.
- [X] T054 [P] `CHANGELOG.md` `[Unreleased]` carries the full modernization entry (per-increment through all six user stories + the polish/cutover-readiness section). FR-017.
- [X] T055 Performance-budget check (SC-010): `FrameworkPerfBaselineTests` drives the intake pipeline through the SK Process Framework and MAF Workflows with an **identical instant scripted model on both paths** (so the measured delta is pure framework/orchestration overhead, the model latency factored out), 40 timed runs interleaved after warmup, driven **directly** to completion (no fire-and-forget poll artifact). Result: **MAF median 1.94 ms/run vs SK 10.48 ms/run — −81.4% (≈5.4× faster), well within the ≤10% budget**; per-model-call overhead 0.65 ms (MAF) vs 3.49 ms (SK). MAF Workflows is materially lighter than the SK Process Framework, so the perf gate does not block cutover. (Tagged `[Perf]`; report written to `maf-perf-baseline.txt`.)
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
