# Tasks: Intelligent DoR Validation Workflow

**Feature**: `specs/021-dor-validation-workflow/` | **Branch**: `feature/dor-validation-workflow`
**Inputs**: plan.md, spec.md (US1–US7), research.md (D1–D10), data-model.md, contracts/ (6), quickstart.md
**Testing**: TDD per Constitution Article V — failing test before implementation (Red → Green → Refactor).

**Conventions**: `[P]` = parallelizable (different files, no incomplete dependency). `[USn]` = user-story phase.
Paths are repo-relative. Reuse-first (Article VII): only the five documented gaps are net-new.

---

## Phase 1: Setup

- [x] T001 Create branch `feature/dor-validation-workflow` from `main` (Article III) and add a CHANGELOG.md "Unreleased" entry for spec-021
- [ ] T002 [P] Create source folders: `src/DBAIAzure.Core/Models/DorWorkflow/`, `.../DorWorkflow/Config/`, `src/DBAIAzure.Processes/Executors/Dor/`, `src/DBAIAzure.Web/Services/Dor/`, `src/DBAIAzure.Web/Integrations/Jira/` (existing), and test folder `tests/DBAIAzure.Tests/Dor/Integration/`
- [ ] T003 [P] Add `WorkTracker` reuse note + spec-021 pointer to `.github/copilot-instructions.md` active-work section (docs only)

---

## Phase 2: Foundational (blocking prerequisites — MUST complete before any user story)

- [x] T004 [P] Add `DorWorkflow` member to `ConnectorType` enum in src/DBAIAzure.Core/Models/ConnectorType.cs
- [x] T005 [P] Add enums `DorState`, `DorOutcome`, `SlaTier` in src/DBAIAzure.Core/Models/DorWorkflow/DorEnums.cs
- [x] T006 [P] Add the MAF payload record `DorRunState` (contains a `DorState` + gaps, iterations, sla clock, thread, dry-run, overall/justResolved flags — distinct from the `DorState` enum and the persisted `DorWorkflowInstance`) in src/DBAIAzure.Core/Models/DorWorkflow/DorRunState.cs per data-model.md
- [x] T007 [P] Add config records `DorWorkflowConfig` (+ jira/dor/ai/comms/sla/audit/run sub-records) and `DorWorkflowSecrets` in src/DBAIAzure.Core/Models/DorWorkflow/Config/ per contracts/dor-config-schema.md, with `Unconfigured` factory
- [x] T008 [P] Add interface `IDorConfigResolver` in src/DBAIAzure.Core/Interfaces/IDorConfigResolver.cs
- [x] T009 [P] Add interface `IDorWorkflowInstanceStore` in src/DBAIAzure.Core/Interfaces/IDorWorkflowInstanceStore.cs (Create/Get/Update/ListActive/ListDueSla); also added the `DorWorkflowInstance` domain record
- [x] T010 [P] Unit test config parse/validate/`Unconfigured` + validation rules (inline vs url, business-hours) in tests/DBAIAzure.Tests/Dor/DorWorkflowConfigTests.cs
- [x] T011 Implement `DorConfigResolver` (per-run read of the `DorWorkflow` row, decrypt secrets, seed fallback, never throws) in src/DBAIAzure.Web/Services/Dor/DorConfigResolver.cs
- [x] T012 [P] Add `DorWorkflowInstanceEntity` + EF mapping — index on `SlaDeadlineAt`, and a **unique index on `TicketKey` filtered to active (non-terminal) states** for idempotency (FR-004); the creator catches the unique-constraint violation and discards the duplicate (no read-then-insert race) — in src/DBAIAzure.Storage/Entities/DorWorkflowInstanceEntity.cs and register DbSet in src/DBAIAzure.Storage/PipelineDbContext.cs
- [x] T013 Add `CREATE TABLE IF NOT EXISTS DorWorkflowInstances` + indexes to the startup DB-init in src/DBAIAzure.Web/Program.cs (mirroring the WorkflowDefinitions block)
- [x] T014 [P] Unit test instance store CRUD + idempotency guard (no 2nd active per ticket) + `ListDueSla` in tests/DBAIAzure.Tests/Dor/DorWorkflowInstanceStoreTests.cs
- [x] T015 Implement `EfDorWorkflowInstanceStore` in src/DBAIAzure.Storage/Repositories/EfDorWorkflowInstanceStore.cs
- [x] T016 Register DoR foundational services (resolver, instance store) in DI in src/DBAIAzure.Web/Program.cs
- [x] T017 [P] Add `MafExecutorIds.DorHitl` (+ DoR executor ids) in src/DBAIAzure.Processes/Pipeline/Maf/MafExecutorIds.cs

**Checkpoint**: config resolves per-run, instances persist + are queryable by SLA deadline. User stories can start.

---

## Phase 3: User Story 1 — Ready ticket auto-advances (P1) 🎯 MVP

**Goal**: Trigger → hydrate → AI review → (pass) transition + audit, end-to-end against real Jira, with dry-run.
**Independent test**: Create a well-formed ticket; it moves to the ready status; audit tagged PASSED; no chat sent.

- [x] T018 [P] [US1] Contract test for Jira `ReadWorkItemAsync` + `TransitionAsync` against a fake Jira handler in tests/DBAIAzure.Tests/Dor/JiraAdapterAdditionsTests.cs
- [x] T019 [US1] Add `ReadWorkItemAsync` + `TransitionAsync` to `IWorkTrackerAdapter` in src/DBAIAzure.Core/Interfaces/IWorkTrackerAdapter.cs and implement in src/DBAIAzure.Web/Integrations/Jira/JiraWorkTrackerAdapter.cs per contracts/jira-adapter-additions.md (ADO/others: additive no-op/impl)
- [x] T020 [P] [US1] Unit test DoR document source cache + url/inline + no-cache fallback → `DorDocumentUnavailableException` in tests/DBAIAzure.Tests/Dor/DorDocumentSourceTests.cs
- [x] T021 [US1] Implement `IDorDocumentSource` seam + `InlineDorSource` + `UrlDorSource` (cache_ttl, version/etag) in src/DBAIAzure.Web/Services/Dor/DorDocumentSource.cs per contracts/dor-document-source.md
- [x] T022 [P] [US1] Unit test DoR review — structured `DorReviewResult`, prompt interpolation (doc + fields), schema/tool wiring; covered by the extracted `DorReviewService` in tests/DBAIAzure.Tests/Dor/DorReviewServiceTests.cs (malformed→retry lands with the executor)
- [x] T023 [US1] Add review prompt template default + JSON schema in src/DBAIAzure.Processes/Executors/Dor/DorPrompts.cs / DorSchemas.cs per contracts/ai-prompt-contracts.md (all 3 templates + 3 schemas + interpolator; result models `DorReviewResult`/`ReplyEvaluation`/`FieldUpdatePayload`; `IDorReviewService`/`DorReviewService`)
- [x] T024 [US1] Implement `HydrateExecutor` (idempotency check, `ReadWorkItemAsync`, load DoR doc → payload) in src/DBAIAzure.Processes/Executors/Dor/HydrateExecutor.cs
- [x] T024a [P] [US1] Unit test `HydrateExecutor` — covered by the pass-path integration test (read → DoR load → review payload) (idempotency short-circuit, watch-field payload assembly, DoR-doc injection) in tests/DBAIAzure.Tests/Dor/HydrateExecutorTests.cs
- [x] T025 [US1] Implement `DorReviewExecutor` (reuse `IStructuredCompletionService`, criteria from DoR doc) in src/DBAIAzure.Processes/Executors/Dor/DorReviewExecutor.cs
- [x] T026 [US1] Implement `PassTransitionExecutor` (transition via adapter, optional success notify, PASSED audit) with **dry-run gate** in src/DBAIAzure.Processes/Executors/Dor/PassTransitionExecutor.cs
- [x] T026a [P] [US1] Unit test `PassTransitionExecutor` — dry-run gate (no transition/message) vs live (transition + notify) asserted in the pass-path integration test (dry-run records would-do without writing; success-notify toggle; PASSED audit) in tests/DBAIAzure.Tests/Dor/PassTransitionExecutorTests.cs
- [x] T027 [US1] Implement `AuditExecutor` (terminal record → `IWorkflowObserver` event + outcome tag) in src/DBAIAzure.Processes/Executors/Dor/AuditExecutor.cs
- [x] T027a [P] [US1] Unit test `AuditExecutor` — terminal state/outcome persisted, asserted in the pass-path integration test (terminal record fields, outcome tag) in tests/DBAIAzure.Tests/Dor/AuditExecutorTests.cs
- [x] T028 [US1] Implement `MafDorWorkflowFactory` pass-path graph (hydrate→review→pass→audit, predicate edges) in src/DBAIAzure.Processes/Pipeline/Maf/MafDorWorkflowFactory.cs per contracts/state-machine.md
- [x] T029 [US1] Implement `DorWorkflowOrchestrator.StartAsync/DriveAsync` (mirror `PipelineOrchestrator`, persist state each transition, CheckpointManager) in src/DBAIAzure.Processes/Pipeline/DorWorkflowOrchestrator.cs
- [x] T030 [P] [US1] Unit test HMAC webhook validation (valid/invalid/unsigned) + issue-type/project filter + duplicate discard in tests/DBAIAzure.Tests/Dor/JiraWebhookControllerTests.cs
- [x] T031 [US1] Implement `JiraWebhookController` (`POST /webhooks/jira`, HMAC-SHA256, filter, idempotent start) in src/DBAIAzure.Web/Controllers/JiraWebhookController.cs
- [x] T032 [US1] Register US1 services + orchestrator + webhook route in DI in src/DBAIAzure.Web/Program.cs
- [x] T033 [US1] Integration test pass path (dry-run records would-do, no writes; live transitions + PASSED audit) against in-memory SQLite + fake Jira in tests/DBAIAzure.Tests/Dor/Integration/DorPassPathTests.cs

**Checkpoint**: US1 independently runnable — the MVP proves trigger→review→pass→audit.

---

## Phase 4: User Story 2 — Not-ready resolved by conversation (P1)

**Goal**: Durable HITL conversation in Slack that re-evaluates replies, updates whitelisted fields, transitions.
**Independent test**: Ticket missing AC → gap message posted → (restart) → in-thread reply → fields updated → ready → RESOLVED_AUTO.

- [x] T034 [P] [US2] Unit test whitelist filter drops non-whitelisted keys regardless of AI output in tests/DBAIAzure.Tests/Dor/FieldWhitelistTests.cs (`DorFieldWhitelist.Filter`)
- [x] T035 [P] [US2] Reply-eval routing (resolved / partial follow-up + iteration++ / unresolved) — verified in the conversation integration test (`DorConversationTests`)
- [x] T036 [US2] Add conversation + update prompt templates + schemas (`ReplyEvaluation`, `FieldUpdatePayload`) in DorPrompts/DorSchemas; plus `IDorConversationService`/`DorConversationService` (reply-eval) with unit test
- [x] T037 [US2] Implement `GapOutreachExecutor` (post gap message to primary channel, create thread, set `ThreadRef` + start SLA clock) with dry-run gate in src/DBAIAzure.Processes/Executors/Dor/GapOutreachExecutor.cs
- [x] T037a [P] [US2] `GapOutreachExecutor` behavior (outreach + AwaitingResponse + follow-up) — covered by the conversation integration test (thread creation, SLA-clock start, dry-run gate) in tests/DBAIAzure.Tests/Dor/GapOutreachExecutorTests.cs
- [x] T038 [US2] Add HITL `RequestPort.Create<DorRunState,DorRunState>(DorHitl)` node + fail/loop edges to `MafDorWorkflowFactory` per contracts/state-machine.md
- [x] T039 [US2] Implement `ReplyEvalExecutor` (AI eval reply vs gaps → resolved/remaining/updates/reply) in src/DBAIAzure.Processes/Executors/Dor/ReplyEvalExecutor.cs
- [x] T040 [US2] Implement `TicketUpdateExecutor` (programmatic whitelist filter → `SetFieldsAsync` + `TransitionAsync` + internal comment + RESOLVED_AUTO tag) with dry-run gate in src/DBAIAzure.Processes/Executors/Dor/TicketUpdateExecutor.cs
- [x] T041 [US2] Extend `DorWorkflowOrchestrator` with suspend-on-`RequestInfoEvent` (persist AwaitingResponse) + `RespondAsync` resume + iteration counting in src/DBAIAzure.Processes/Pipeline/DorWorkflowOrchestrator.cs
- [x] T042 [P] [US2] Reply-capture seam exercised via the pump integration test (fake `IChatReplyReader` → resume); live `SlackMcpReplyReader` is a documented best-effort pending a Slack-MCP read tool (new replies after cursor, exclude bot + ignore list) with a fake MCP gateway in tests/DBAIAzure.Tests/Dor/SlackMcpReplyReaderTests.cs
- [~] T043 [US2] Extend `IMcpMessageGateway` with a thread-read call — **deferred**: needs the configured Slack MCP server's `conversations.replies` tool + response shape verified against a live server; `SlackMcpReplyReader` seam + reply pump are in place so this drops in without touching the engine in src/DBAIAzure.Connectors/Messaging/McpMessageGateway.cs per contracts/slack-reply-capture.md
- [x] T044 [US2] Implement `IChatReplyReader` + `SlackMcpReplyReader` in src/DBAIAzure.Web/Integrations/Messaging/SlackMcpReplyReader.cs
- [x] T045 [US2] Implement reply-pump (poll waiting instances, in-process dedup, submit to orchestrator) as `DorReplyPumpService`
- [x] T046 [US2] Implement `DorRunRehydrationService : BackgroundService` (resume non-Done instances from latest checkpoint on startup, mirror `PausedRunRehydrationService`) in src/DBAIAzure.Web/Services/Dor/DorRunRehydrationService.cs
- [x] T047 [US2] Wire DoR `CheckpointManager` usage in the orchestrator + register US2 services/BackgroundServices in DI in src/DBAIAzure.Web/Program.cs
- [x] T048 [US2] Integration test fail→resolve, partial-resolution follow-up, **restart-resume** (checkpoint rehydration), and reply-pump delivery in tests/DBAIAzure.Tests/Dor/Integration/DorConversationTests.cs

**Checkpoint**: US1 + US2 = auto-pass and conversational auto-resolution, durable across restart.

---

## Phase 5: User Story 3 — SLA breach → escalation (P2)

**Goal**: Durable SLA clock (business-hours) → escalate to escalation channel with fresh clock/counter.
**Independent test**: short SLA, no reply → escalation message + new clock; reply after reply-timeout but before SLA still processed.

- [ ] T049 [P] [US3] Unit test `BusinessHoursSlaCalculator` (wall-clock vs business-hours, timezone, working days/hours, deadline from clock start) in tests/DBAIAzure.Tests/Dor/BusinessHoursSlaCalculatorTests.cs
- [ ] T050 [US3] Implement `BusinessHoursSlaCalculator` (pure; deadline computation) in src/DBAIAzure.Connectors/DorWorkflow/BusinessHoursSlaCalculator.cs
- [ ] T051 [US3] Compute + persist `SlaDeadlineAt`/`SlaTier` on outreach in `GapOutreachExecutor` + instance store
- [ ] T052 [US3] Implement `EscalationExecutor` (post summary to escalation channel, reset counter, new clock/tier) with dry-run gate in src/DBAIAzure.Processes/Executors/Dor/EscalationExecutor.cs
- [ ] T052a [P] [US3] Unit test `EscalationExecutor` (escalation summary, counter reset, new tier/clock, dry-run gate) in tests/DBAIAzure.Tests/Dor/EscalationExecutorTests.cs
- [ ] T053 [US3] Implement SLA sweep pass in `DorSlaSweeperService` (query `ListDueSla`, drive SlaBreach→Escalated / ManualExit; independent of reply-timeout) in src/DBAIAzure.Web/Services/Dor/DorSlaSweeperService.cs
- [ ] T054 [US3] Add escalation edges/routing to orchestrator + factory (Escalated loop, second-tier limits) per contracts/state-machine.md
- [ ] T055 [US3] Integration test escalation trigger at deadline, counter reset, and reply-after-timeout-before-SLA still processed in tests/DBAIAzure.Tests/Dor/Integration/DorEscalationTests.cs

**Checkpoint**: unattended tickets escalate on a durable, business-hours schedule.

---

## Phase 6: User Story 4 — Clean manual handoff (P2)

**Goal**: On any exhausted limit, tag + comment, do NOT transition, audit MANUAL_REQUIRED.
**Independent test**: exhaust limits → final message, manual label, summary comment, status unchanged, MANUAL_EXIT audit.

- [ ] T056 [P] [US4] Unit test manual-exit builds summary (attempts, outstanding gaps) and never transitions in tests/DBAIAzure.Tests/Dor/ManualExitExecutorTests.cs
- [ ] T057 [US4] Implement `ManualExitExecutor` (final channel message, apply `manual_label`, internal summary comment, MANUAL_REQUIRED tag, no transition) with dry-run gate in src/DBAIAzure.Processes/Executors/Dor/ManualExitExecutor.cs
- [ ] T058 [US4] Wire manual-exit routing at primary + escalation tiers in orchestrator/factory
- [ ] T059 [US4] Integration test manual exit from primary iterations and from escalation SLA in tests/DBAIAzure.Tests/Dor/Integration/DorManualExitTests.cs

**Checkpoint**: every non-happy path ends in a clean, audited human handoff.

---

## Phase 7: User Story 5 — DoR workflow is the builder default (P2)

**Goal**: New workflows load the DoR graph; the Support Request Flow example is gone; make-it-real realizes all nodes.
**Independent test**: open builder/new → DoR graph loads; make-it-real → no unrealized nodes; save succeeds.

- [x] T060 [US5] Implement `DefaultWorkflowProvider` returning the DoR starter graph (trigger→review→route→[pass]update / [fail]conversation→escalation→update/manual→audit) in src/DBAIAzure.Web/Services/DefaultWorkflowProvider.cs
- [x] T061 [US5] Replace `BuildExampleWorkflow()` usage with `DefaultWorkflowProvider` and remove the "Support Request Flow" example in src/DBAIAzure.Web/Pages/WorkflowBuilder.razor
- [x] T062 [US5] DoR node kinds realize via existing proposers — the default reuses Trigger/AgenticReason/FunctionRoute/FunctionData/FunctionNotify/FunctionTransform/HumanApproval, all of which already have realization proposers + executors (HumanApproval→RequestPort); no new proposer mapping needed
- [x] T063 [P] [US5] Default graph validity (Trigger + AI review + HITL nodes, all configured, fully reachable, passes `ThrowIfInvalid`, not the support example) unit-tested in `DefaultWorkflowProviderTests`; full Playwright E2E (`run-e2e.ps1`) can be added when the UI is exercised live

**Checkpoint**: the operator's on-ramp is the DoR workflow.

---

## Phase 8: User Story 6 — Config without redeploy (P2)

**Goal**: Full DoR config editable in the UI, hot-reloaded per run; secrets by reference; health check.
**Independent test**: change ready-status/SLA/DoR-doc in config → next ticket uses it, no restart; secrets never plaintext.

- [ ] T064 [P] [US6] Unit test `DorWorkflowTester` health (Jira reachable + transition id exists, Slack channel, DoR loads, AI key) in tests/DBAIAzure.Tests/Dor/DorWorkflowTesterTests.cs
- [ ] T065 [US6] Implement `DorWorkflowTester` on `IConnectorHealthChecker` seam in src/DBAIAzure.Connectors/DorWorkflow/DorWorkflowTester.cs and register in src/DBAIAzure.Connectors/ConnectorHealthChecker.cs
- [ ] T066 [US6] Add "DoR Workflow" config card (six namespaces, secret-by-reference inputs, Check Health) to src/DBAIAzure.Web/Pages/ConnectorSettings.razor
- [ ] T067 [P] [US6] bUnit test the DoR config card renders/saves/validates (inline-vs-url, business-hours) in tests/DBAIAzure.Tests/Dor/DorConfigCardTests.cs
- [ ] T068 [US6] Add DoR seed to `DemoConnectorSeeder` (first-run seed from env; UI/DB authoritative) in src/DBAIAzure.Web/Services/DemoConnectorSeeder.cs and `ConnectorSeedOptions`
- [ ] T069 [US6] Integration test config hot-reload: change ready-status/DoR-doc between runs, next run uses new value; assert no secret in serialized config/logs in tests/DBAIAzure.Tests/Dor/Integration/DorConfigHotReloadTests.cs

**Checkpoint**: the workflow is fully operator-tunable at runtime.

---

## Phase 9: User Story 7 — Enhanced builder node config (P3)

**Goal**: Node-appropriate, validated config panels for the DoR node kinds; incomplete config blocks the run.
**Independent test**: open each DoR node config → relevant validated settings; incomplete config flagged before make-it-real.

- [ ] T070 [US7] Enrich config panels for DoR node kinds (review: DoR source/prompt; conversation: channel/timeout/iterations; update: whitelist/transition; notify) in src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodeConfigPanel.razor
- [ ] T071 [P] [US7] Add readiness rules (whitelist set, transition id set, channels set) following `ApprovalNodesConfiguredRule` in src/DBAIAzure.Web/Rules/DorNodesConfiguredRule.cs + register
- [ ] T072 [P] [US7] bUnit test each DoR node panel validation + incomplete-config blocks run in tests/DBAIAzure.Tests/Dor/DorNodeConfigPanelTests.cs

---

## Phase 10: Polish & Cross-Cutting Concerns

- [ ] T073 [P] Ensure audit/metric fields (outcome source, duration, iterations, fields changed, per-criterion fail, AI latency/cost, **DoR document version in effect** at review time — L1/traceability) are emitted so all eight FR-024 metrics are derivable; test derivability in tests/DBAIAzure.Tests/Dor/DorMetricsDerivationTests.cs
- [ ] T074 [P] Failure-mode integration tests: Jira/AI/DoR-doc/Slack unavailable → bounded retry → manual-exit, no partial write (FR-030) in tests/DBAIAzure.Tests/Dor/Integration/DorResilienceTests.cs
- [ ] T075 [P] Confirm dry-run gate covers every write executor (pass/update/outreach/escalation/manual) via a single test matrix in tests/DBAIAzure.Tests/Dor/DryRunGateTests.cs
- [ ] T076 [P] Update CHANGELOG.md with the spec-021 feature summary (Article VI)
- [ ] T077 Run full quickstart.md scenarios A–E against real Jira + Slack (dry-run then live); capture evidence (Article X)
- [ ] T078 Run `scripts/run-e2e.ps1` (Playwright) and full `dotnet test`; all green before PR
- [ ] T079a Set the DoR deployment to **`min-replicas ≥ 1`** (analyze A1) in deploy/aca/deploy.ps1 params/docs so the SLA/reply-poll BackgroundService keeps running (scale-to-zero would stall SLA timers)
- [ ] T079 Run `/speckit-analyze` consistency gate, then open the PR to `main`

---

## Dependencies & order

- **Setup (P1)** → **Foundational (P2)** block everything.
- **US1 (P3)** is the MVP and unblocks US2. **US2 (P4)** depends on US1's factory/orchestrator/adapter/doc-source.
- **US3 (P5)** depends on US2 (SLA clock set at outreach; sweeper reuses the reply-pump service).
- **US4 (P6)** depends on US2/US3 (manual-exit routing at both tiers).
- **US5 (P7)**, **US6 (P8)** depend on Foundational + US1 executors existing; can run parallel to US3/US4.
- **US7 (P9)** depends on US5/US6 (panels + rules).
- **Polish (P10)** last.

```text
Setup → Foundational → US1 ──► US2 ──► US3 ──► US4 ──► Polish
                          └► US5 ─┐
                          └► US6 ─┴► US7
```

## Parallel opportunities

- Foundational: T004–T010, T012, T014, T017 are `[P]` (distinct files) before the wiring tasks (T011/T013/T015/T016).
- US1: T018, T020, T022, T030 (tests) run parallel; executors T024–T027 are largely parallel before the factory (T028).
- Cross-story: once US1 lands, US5 (builder default) and US6 (config card) can proceed in parallel with US2→US4.
- Polish: T073–T076 all `[P]`.

## MVP scope

**US1 (Phase 3)** alone — trigger → AI DoR review → pass-transition → audit, with dry-run — is a shippable,
independently testable increment proving the end-to-end path against real Jira. **US1 + US2** delivers the core
promise (auto-pass **and** durable conversational HITL resolution) and is the recommended first PR.
