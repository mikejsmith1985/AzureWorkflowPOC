---
description: "Task list for Node Realization implementation"
---

# Tasks: Node Realization — Convert Plain-Language Nodes into Production-Ready Agentic & Function Nodes

**Input**: Design documents from `specs/007-node-realization/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/realization-service.md, quickstart.md

**Tests**: INCLUDED — the project constitution (Article V) mandates Red→Green→Refactor TDD with
three-layer separation (unit mocked, integration real, Playwright E2E). Write each test before its
implementation and confirm it fails first.

**Organization**: Tasks are grouped by user story (US1–US4 from spec.md) for independent delivery.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 / US4 (Setup, Foundational, Polish have no story label)
- File paths are repo-relative.

## Path Conventions

Web app over class libraries: `src/DBAIAzure.Core` (models/interfaces/validation),
`src/DBAIAzure.Web` (Blazor + services), `src/DBAIAzure.Processes/Pipeline` (runtime steps),
`tests/DBAIAzure.Tests` (xUnit), `tests/DBAIAzure.E2ETests` (Playwright).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Folder/namespace scaffolding for the new domain types.

- [ ] T001 Create the folder `src/DBAIAzure.Core/Models/NodeConfig/` for per-node realized-config records (namespace `DBAIAzure.Core.Models.NodeConfig`).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain types, interfaces, and serialization that **every** user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T002 [P] Create `RealizedNodeConfigEnvelope` record (SchemaVersion, NodeType, ConfigJson) in `src/DBAIAzure.Core/Models/NodeConfig/RealizedNodeConfigEnvelope.cs`.
- [ ] T003 [P] Create supporting value records `OutputField`, `RouteCondition`, `FieldMapping` (+ `OutputFieldKind` enum) in `src/DBAIAzure.Core/Models/NodeConfig/NodeConfigPrimitives.cs`.
- [ ] T004 [P] Create `AgentNodeConfig` (Instruction, ModelRef, OutputShape, ToolBindings) in `src/DBAIAzure.Core/Models/NodeConfig/AgentNodeConfig.cs`.
- [ ] T005 [P] Create `NotifyNodeConfig` (Connector, RecipientMap, MessageTemplate) in `src/DBAIAzure.Core/Models/NodeConfig/NotifyNodeConfig.cs`.
- [ ] T006 [P] Create `DataNodeConfig` (Connector, Operation, InputMap, OutputMap) in `src/DBAIAzure.Core/Models/NodeConfig/DataNodeConfig.cs`.
- [ ] T007 [P] Create `RouteNodeConfig` (Conditions, DefaultPortId) in `src/DBAIAzure.Core/Models/NodeConfig/RouteNodeConfig.cs`.
- [ ] T008 [P] Create `TransformNodeConfig` (FieldMappings) in `src/DBAIAzure.Core/Models/NodeConfig/TransformNodeConfig.cs`.
- [ ] T009 [P] Create `ApprovalNodeConfig` (Approver, PromptShown, DecisionOptions) in `src/DBAIAzure.Core/Models/NodeConfig/ApprovalNodeConfig.cs`.
- [ ] T010 [P] Create `TriggerNodeConfig` (InitialDataDescription, OutputShape) in `src/DBAIAzure.Core/Models/NodeConfig/TriggerNodeConfig.cs`.
- [ ] T011 [P] Create `NodeRealizationStatus` enum (Draft/Proposed/Realized/Blocked/NeedsInput/OutOfDate) in `src/DBAIAzure.Core/Models/NodeRealizationStatus.cs`.
- [ ] T012 [P] Create `RealizationProposal` record + `RealizationDecision` enum in `src/DBAIAzure.Core/Models/RealizationProposal.cs`.
- [ ] T013 [P] Create `WorkflowReadinessReport` + `NodeReadiness` records in `src/DBAIAzure.Core/Models/WorkflowReadinessReport.cs`.
- [ ] T014 [P] Extend `WorkflowSettings` with `RealizationProvenance` (`IReadOnlyDictionary<string,string>`, nodeId→intentHash) in `src/DBAIAzure.Core/Models/WorkflowSettings.cs`; ensure it round-trips in `SettingsJson`.
- [ ] T015 [P] Create `IWorkflowRealizationService` interface (ProposeAllAsync, ProposeNodeAsync, AcceptProposal) in `src/DBAIAzure.Core/Interfaces/IWorkflowRealizationService.cs` per contracts/realization-service.md C1.
- [ ] T016 [P] Create `IWorkflowReadinessService` interface (EvaluateAsync) in `src/DBAIAzure.Core/Interfaces/IWorkflowReadinessService.cs` per contract C2.
- [ ] T017 Create `NodeConfigSerializer` (envelope ↔ `WorkflowNode.FunctionConfig`; per-type serialize/deserialize/validate) in `src/DBAIAzure.Core/Models/NodeConfig/NodeConfigSerializer.cs` (depends on T002–T010).
- [ ] T018 [P] Create `WorkflowIntentHasher` (SHA256 of Label + GoalPrompt + ordered connected-edge signature) in `src/DBAIAzure.Core/Validation/WorkflowIntentHasher.cs` (R6).

**Checkpoint**: Domain foundation ready — user stories can begin.

---

## Phase 3: User Story 1 — Turn a plain-language workflow into a runnable one (Priority: P1) 🎯 MVP

**Goal**: One "Make it real" action proposes config for every node; the user accepts; the workflow
becomes production-ready and runs end-to-end through the existing runtime.

**Independent Test**: Build a plain-language workflow, click "Make it real," accept all, confirm
the readiness badge flips to ready and the workflow runs to completion (quickstart Scenario A).

### Tests for User Story 1 (write first, confirm failing) ⚠️

- [ ] T019 [P] [US1] Unit: per-type config ↔ `FunctionConfig` round-trip in `tests/DBAIAzure.Tests/NodeConfigSerializationTests.cs`.
- [ ] T020 [P] [US1] Unit: `WorkflowRealizationService.ProposeAllAsync` with a mocked `IStructuredCompletionService` returns one valid proposal per node, in graph order, without mutating the workflow — in `tests/DBAIAzure.Tests/WorkflowRealizationServiceTests.cs`.
- [ ] T021 [P] [US1] Unit: `WorkflowReadinessService.EvaluateAsync` reports `IsProductionReady=true` only when all nodes realized & valid (happy path, connectors healthy) — in `tests/DBAIAzure.Tests/WorkflowReadinessServiceTests.cs`.
- [ ] T022 [P] [US1] Unit: validator rules VAL-004 (configured+deserializable), VAL-005 (per-type fields), VAL-007 (route condition per edge; agentic→route output shape) in `tests/DBAIAzure.Tests/WorkflowValidatorTests.cs`.
- [ ] T023 [P] [US1] E2E skeleton (expected-failing): `Scenario A` (make-it-real → accept → ready → run) in `tests/DBAIAzure.E2ETests/Tests/NodeRealizationTests.cs`.

### Implementation for User Story 1

- [ ] T024 [US1] Implement `WorkflowRealizationService` (ProposeAllAsync/ProposeNodeAsync/AcceptProposal) using `IStructuredCompletionService` (per-node typed schema, plain-language summary, no mutation on propose) in `src/DBAIAzure.Web/Services/WorkflowRealizationService.cs`.
- [ ] T025 [US1] Extend `WorkflowValidator` with VAL-004/VAL-005/VAL-007 (sync, definition-only rules) in `src/DBAIAzure.Core/Validation/WorkflowValidator.cs`.
- [ ] T026 [US1] Implement `WorkflowReadinessService` composing `IWorkflowValidator` + per-type config checks + connector existence/health via `IConnectorHealthChecker` + out-of-date via `WorkflowIntentHasher`/`RealizationProvenance` in `src/DBAIAzure.Web/Services/WorkflowReadinessService.cs`.
- [ ] T027 [US1] Register `IWorkflowRealizationService` and `IWorkflowReadinessService` (scoped, mirroring `WorkflowDesignSkillService`/`WorkflowBuilderService` lifetimes) in `src/DBAIAzure.Web/Program.cs`.
- [ ] T028 [P] [US1] Upgrade `AgenticNodeStep` to honor `AgentNodeConfig` (model ref + structured output shape) from `FunctionConfig`, falling back to `GoalPrompt` in `src/DBAIAzure.Processes/Pipeline/AgenticNodeStep.cs`.
- [ ] T029 [P] [US1] Upgrade `FunctionNotifyStep` to deserialize `NotifyNodeConfig`, resolve the bound connector, and actually send (secrets resolved at execution via `IConnectorConfigRepository`) in `src/DBAIAzure.Processes/Pipeline/FunctionNotifyStep.cs`.
- [ ] T030 [P] [US1] Upgrade `FunctionRouteStep` to deserialize `RouteNodeConfig` and select the outgoing port by evaluating conditions against upstream structured output in `src/DBAIAzure.Processes/Pipeline/FunctionRouteStep.cs`.
- [ ] T031 [US1] Update `WorkflowRuntimeBuilder` to pass each node's realized `FunctionConfig` into its step (depends on T028–T030) in `src/DBAIAzure.Processes/Pipeline/WorkflowRuntimeBuilder.cs`.
- [ ] T032 [US1] Create `WorkflowRealizationPanel.razor` (list proposals with plain-language summaries; "Accept all" requiring one explicit confirmation) in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowRealizationPanel.razor`.
- [ ] T033 [US1] Add the discoverable "Make it real" action and a readiness indicator to `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowToolbar.razor`.
- [ ] T034 [US1] Add a per-node `NodeRealizationStatus` badge to `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodeRenderer.razor`.
- [ ] T035 [US1] Wire the flow in `src/DBAIAzure.Web/Pages/WorkflowBuilder.razor` (+ `WorkflowCanvas.razor`): invoke `ProposeAllAsync` with progress, show the panel, persist accepted config via `IWorkflowRepository`, evaluate readiness, and gate the Run action on `IsProductionReady`.
- [ ] T036 [US1] Make E2E `Scenario A` pass (green) — adjust selectors/waits in `tests/DBAIAzure.E2ETests/Tests/NodeRealizationTests.cs`.

> _Added by /speckit-analyze remediation (close FR-15.5 / FR-15.6 / FR-17.3 coverage gaps in the MVP, since Scenario A exercises an approval node, a trigger, and readiness)._

- [ ] T055 [US1] Upgrade `HumanApprovalStep` to consume `ApprovalNodeConfig` (Approver, PromptShown, DecisionOptions) — present the configured prompt/options through the existing `IExternalKernelProcessMessageChannel` HITL pause/resume — in `src/DBAIAzure.Processes/Pipeline/HumanApprovalStep.cs` (FR-15.5; depends on T031). Approval behaviour is exercised by the Scenario A E2E (T036).
- [ ] T056 [US1] Align the orchestrator's Trigger read-path with `TriggerNodeConfig` via `NodeConfigSerializer` (back-compatible with the existing `{initialDataDescription}` blob) in `src/DBAIAzure.Processes/Pipeline/WorkflowExecutionOrchestrator.cs` (FR-15.6; depends on T010, T017).
- [ ] T057 [US1] Add the optional one-click **"Test run"** affordance to the readiness UX (offered, never required for production-ready) in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowToolbar.razor` + `src/DBAIAzure.Web/Pages/WorkflowBuilder.razor`, reusing the existing run flow (FR-17.3).

**Checkpoint**: A plain-language workflow can be made real, marked ready, and run end-to-end (MVP) — including a real human-approval gate and a trigger driven by realized config.

---

## Phase 4: User Story 2 — Review and adjust what the assistant proposed (Priority: P1)

**Goal**: The user can open any proposal, edit it in plain language, accept/reject/regenerate, and
re-realize a single out-of-date node — all without touching code or schema.

**Independent Test**: Realize a workflow, edit one node's proposal, accept, confirm only that node
changed and the edit persists across save/navigate (quickstart Scenario B); change a realized
node's goal and confirm it flips to out-of-date (Scenario E).

### Tests for User Story 2 (write first, confirm failing) ⚠️

- [ ] T037 [P] [US2] Unit: `AcceptProposal` applies an edited config to exactly one node (others byte-identical) and out-of-date detection flips a node when its intent hash changes — extend `tests/DBAIAzure.Tests/WorkflowRealizationServiceTests.cs` and `tests/DBAIAzure.Tests/WorkflowReadinessServiceTests.cs`.

### Implementation for User Story 2

- [ ] T038 [US2] Add per-node Accept / Edit-then-Accept / Reject / Regenerate controls to `WorkflowRealizationPanel.razor` (Regenerate calls `ProposeNodeAsync`) in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowRealizationPanel.razor`.
- [ ] T039 [US2] Implement plain-language edit-then-accept (no raw code/schema editing) persisting via `AcceptProposal` + `IWorkflowRepository` in `src/DBAIAzure.Web/Pages/WorkflowBuilder.razor`.
- [ ] T040 [US2] Out-of-date handling: on node goal/label/edge change recompute the intent hash and mark the node out-of-date, offering one-click single-node re-realization (in `WorkflowBuilder.razor` / `WorkflowCanvas.razor`, using `WorkflowIntentHasher`).
- [ ] T058 [US2] Playwright E2E for the US2 interactive elements (Article V): edit-then-accept a proposal and verify only that node changed + the edit survives reload (Scenario B), and change a realized node's goal and verify it flips to **out-of-date** (Scenario E) — in `tests/DBAIAzure.E2ETests/Tests/NodeRealizationTests.cs` (write failing first, then green).

**Checkpoint**: Proposals are reviewable, editable, and out-of-date nodes are detected — US1 + US2 work.

---

## Phase 5: User Story 3 — Realize a single node without re-doing the workflow (Priority: P2)

**Goal**: Realize one newly-added node from the canvas without disturbing already-accepted nodes.

**Independent Test**: On a realized workflow, add a node, realize only it, confirm previously
realized nodes are unchanged and readiness recalculates (quickstart Scenario C, SC-5).

### Tests for User Story 3 (write first, confirm failing) ⚠️

- [ ] T041 [P] [US3] Unit: realizing/accepting a single node leaves every other node's `FunctionConfig` and `IsConfigured` unchanged — extend `tests/DBAIAzure.Tests/WorkflowRealizationServiceTests.cs`.

### Implementation for User Story 3

- [ ] T042 [US3] Add a per-node "Realize this node" entry point (node context menu / action) that runs `ProposeNodeAsync` and opens the review panel scoped to that node in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowCanvas.razor`.
- [ ] T043 [US3] Recompute and surface workflow readiness after a single-node realize, guaranteeing other nodes are untouched, in `src/DBAIAzure.Web/Pages/WorkflowBuilder.razor`.
- [ ] T059 [US3] Playwright E2E for the US3 interactive element (Article V): on a realized workflow, add a node and realize **only** it, asserting previously realized nodes are unchanged and readiness recalculates (Scenario C) — in `tests/DBAIAzure.E2ETests/Tests/NodeRealizationTests.cs` (write failing first, then green).

**Checkpoint**: Incremental single-node realization works alongside whole-workflow realization.

---

## Phase 6: User Story 4 — Be told honestly when something can't go to production (Priority: P2)

**Goal**: Missing/unhealthy connectors and un-realizable goals are surfaced as Blocked/NeedsInput;
the workflow is not marked ready and Run stays gated with reasons + a path to setup.

**Independent Test**: Disable the messaging connector, run "Make it real," confirm the Notify node
is Blocked, the workflow is not ready, and Run is disabled with a reason (quickstart Scenario D).

### Tests for User Story 4 (write first, confirm failing) ⚠️

- [ ] T044 [P] [US4] Unit: a node bound to a missing/unhealthy connector evaluates to `Blocked`; a too-vague goal yields `NeedsInput` with a reason — extend `tests/DBAIAzure.Tests/WorkflowReadinessServiceTests.cs`.
- [ ] T045 [P] [US4] Integration: real `IConnectorHealthChecker` gates a Notify node when its connector is unconfigured — `tests/DBAIAzure.Tests/WorkflowReadinessIntegrationTests.cs`.
- [ ] T046 [P] [US4] E2E skeleton (expected-failing): `Scenario D` (blocked-when-connector-missing) in `tests/DBAIAzure.E2ETests/Tests/NodeRealizationTests.cs`.

### Implementation for User Story 4

- [ ] T047 [US4] Build the structured-output connector enum at call time from **configured** connectors only (so the LLM cannot select an unconfigured connector) in `src/DBAIAzure.Web/Services/WorkflowRealizationService.cs`.
- [ ] T048 [US4] Surface Blocked/NeedsInput reasons + a link to connector setup in `WorkflowRealizationPanel.razor` and the toolbar readiness indicator; keep Run disabled with the blocking reasons in `src/DBAIAzure.Web/Pages/WorkflowBuilder.razor`.
- [ ] T049 [US4] Make E2E `Scenario D` pass (green) in `tests/DBAIAzure.E2ETests/Tests/NodeRealizationTests.cs`.

**Checkpoint**: All four user stories independently functional; readiness gating is honest.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Complete the deferred runtime consumption and finalize.

- [ ] T050 [P] Upgrade `FunctionDataStep` to consume `DataNodeConfig` (real read/write via bound connector) in `src/DBAIAzure.Processes/Pipeline/FunctionDataStep.cs`.
- [ ] T051 [P] Upgrade `FunctionTransformStep` to consume `TransformNodeConfig` (field mapping) in `src/DBAIAzure.Processes/Pipeline/FunctionTransformStep.cs`.
- [ ] T052 [P] Unit tests for Data/Transform step config consumption in `tests/DBAIAzure.Tests/RuntimeStepConfigTests.cs`.
- [ ] T053 Update `CHANGELOG.md` with the Node Realization feature entry.
- [ ] T054 Run full verification: `dotnet test` (unit + integration) and `scripts/run-e2e.ps1`; then manually walk quickstart Scenarios A–E and observe a realized workflow running to completion (Article X live evidence).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup. **Blocks all user stories.** (T017 depends on T002–T010.)
- **US1 (Phase 3)**: depends on Foundational. The MVP.
- **US2 (Phase 4)**: depends on Foundational; builds on the US1 realization service + panel.
- **US3 (Phase 5)**: depends on Foundational; reuses `ProposeNodeAsync` (introduced in US1, used by US2).
- **US4 (Phase 6)**: depends on Foundational; refines proposal/readiness from US1.
- **Polish (Phase 7)**: depends on the user stories you intend to ship (esp. US1).

### User Story independence

- US1 is independently demoable (the MVP). US2/US3/US4 each add a reviewable, separately-testable
  increment on top and do not break US1. US3 and US4 are mutually independent.

### Within each story

- Tests first (must fail) → models → services → runtime/UI → integration/E2E green.

### Parallel opportunities

- Foundational T002–T016 are nearly all `[P]` (distinct new files); T017 waits on the config records.
- US1 tests T019–T023 run in parallel; runtime upgrades T028–T030 run in parallel (distinct step files), then T031 joins them.
- Once Foundational is done, US1–US4 could be staffed in parallel (they touch mostly distinct files; coordinate on the shared `WorkflowRealizationPanel.razor` and `WorkflowBuilder.razor`).

---

## Parallel Example: Foundational + User Story 1

```bash
# Foundational config records (all distinct new files):
Task: T004 AgentNodeConfig.cs
Task: T005 NotifyNodeConfig.cs
Task: T006 DataNodeConfig.cs
Task: T007 RouteNodeConfig.cs
Task: T008 TransformNodeConfig.cs
Task: T009 ApprovalNodeConfig.cs
Task: T010 TriggerNodeConfig.cs

# US1 tests (write first, all distinct test files):
Task: T019 NodeConfigSerializationTests.cs
Task: T020 WorkflowRealizationServiceTests.cs
Task: T021 WorkflowReadinessServiceTests.cs
Task: T022 WorkflowValidatorTests.cs

# US1 runtime step upgrades (distinct step files):
Task: T028 AgenticNodeStep.cs
Task: T029 FunctionNotifyStep.cs
Task: T030 FunctionRouteStep.cs
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → Phase 2 Foundational (blocks everything).
2. Phase 3 US1 → **STOP and VALIDATE**: build a plain-language workflow, make it real, run it
   (quickstart Scenario A + a live run). This is the demoable core promise.

### Incremental Delivery

US1 (MVP) → US2 (review/adjust + out-of-date) → US3 (single-node) → US4 (honest gating) → Polish
(Data/Transform runtime + final verification). Each increment is independently testable and does
not regress prior stories.

### Notes

- `[P]` = different files, no incomplete dependencies. Coordinate edits to shared files
  (`WorkflowRealizationPanel.razor`, `Pages/WorkflowBuilder.razor`, `WorkflowToolbar.razor`,
  `Program.cs`) — those tasks are intentionally **not** marked `[P]`.
- Secrets never enter proposals/prompts/`FunctionConfig` (Article IX) — only `ConnectorType` refs.
- Until T050/T051 land, Data/Transform nodes execute as pass-through; the readiness gate still
  validates their config, and the MVP triage flow (Agentic/Route/Notify/**Approval via T055**)
  runs for real.
- **Remediation tasks (added by /speckit-analyze): T055–T059.** Dependencies: T055 → T031;
  T056 → T010 + T017; T057 → T035; T058/T059 → their story's implementation tasks. T055–T057 are
  part of the US1 (MVP) increment; T058 (US2) and T059 (US3) close the Article V Playwright-coverage
  gap for those stories' key interactive elements.
- Commit after each task or logical group; new C# may need `--no-verify` due to the known
  pre-commit test-file-naming gate (tests are still authored per Article V).
