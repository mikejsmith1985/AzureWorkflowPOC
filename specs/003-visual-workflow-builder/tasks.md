# Tasks: Visual Workflow Builder

**Input**: Design documents from `specs/003-visual-workflow-builder/`

**Prerequisites**: plan.md ✅ | spec.md ✅ | data-model.md ✅ | research.md ✅ | contracts/ ✅ | quickstart.md ✅

**Tests**: Included — Article V of the project constitution mandates TDD (Red → Green → Refactor).
Unit tests are written before implementation; integration tests follow.

**Organization**: Tasks grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Parallelizable (different files, no shared dependency on an incomplete task)
- **[Story]**: User story this task belongs to (US1–US6)
- All file paths are project-relative

## Path Conventions

```
src/DBAIAzure.Core/         ← domain models + interfaces
src/DBAIAzure.Storage/      ← EF Core entities + repositories
src/DBAIAzure.Processes/    ← SK process steps + orchestrators
src/DBAIAzure.Web/          ← Blazor pages + components + services
tests/DBAIAzure.Tests/      ← xUnit test project
```

---

## Phase 1: Setup

**Purpose**: Project initialization — new packages, folder structure, spike to verify SK API.

- [X] T001 Add `Blazor.Diagrams` 3.1.x and `Blazor.Diagrams.Core` 3.1.x NuGet references to `src/DBAIAzure.Web/DBAIAzure.Web.csproj`
- [X] T073 Add `bUnit` 1.x NuGet reference to `tests/DBAIAzure.Tests/DBAIAzure.Tests.csproj` — required for Blazor Razor component tests (T026, T033, T079 and US6 tests)
- [X] T002 Create all new source folders per plan.md: `src/DBAIAzure.Web/Components/WorkflowBuilder/`, `src/DBAIAzure.Web/Services/`, `src/DBAIAzure.Processes/Steps/` (already exists — verify), `src/DBAIAzure.Processes/Pipeline/` (already exists — verify)
- [X] T003 Verify `LocalKernelProcessFactory.RunToEndAsync` accepts a `TimeSpan timeout` parameter in SK 1.77.0-alpha; if absent, design the `CancellationTokenSource` fallback and document the decision in `specs/003-visual-workflow-builder/research.md`

---

## Phase 2: Foundational — Domain Models, Interfaces & Storage

**Purpose**: All domain types, seam interfaces, EF entity, and SQLite repository. Blocks every user story.

**⚠️ CRITICAL**: No user story work can begin until T004–T025 are complete.

### Enums and value types (all parallelizable)

- [X] T004 [P] Create `WorkflowNodeType` enum (6 values: AgenticReason, FunctionRoute, FunctionTransform, FunctionNotify, FunctionData, HumanApproval) in `src/DBAIAzure.Core/Models/WorkflowNodeType.cs`
- [X] T005 [P] Create `WorkflowRunStatus` enum (7 values: NotStarted, Running, Paused, Completed, Failed, TimedOut, Cancelled) and `NodeStatus` enum (6 values: NotStarted, Active, Completed, Failed, Skipped, TimedOut) in `src/DBAIAzure.Core/Models/WorkflowRunStatus.cs`
- [X] T006 [P] Create `PortDirection` enum (Input, Output) and `WorkflowPort` record (Id, Label, Direction) in `src/DBAIAzure.Core/Models/WorkflowPort.cs`
- [X] T007 [P] Create `WorkflowSettings` record (ExecutionTimeoutMinutes default 5, DesignSkillAnswers dictionary, LastRunInputDescription) in `src/DBAIAzure.Core/Models/WorkflowSettings.cs`
- [X] T008 [P] Create `ChatRole` enum (User, Assistant) and `WorkflowChatMessage` record (Role, Content, IsCodeBlock, Timestamp) in `src/DBAIAzure.Core/Models/WorkflowChatMessage.cs`
- [X] T009 [P] Create `DiffLineKind` enum (Unchanged, Added, Removed), `DiffLine` record (LineNumber, Kind, Content), and `CodeDiff` record (Lines) in `src/DBAIAzure.Core/Models/CodeDiff.cs`
- [X] T010 [P] Create `NodeExecutionState` record (NodeId, Status, OutputSummary, FullOutput, FailureReason, StartedAt, CompletedAt) in `src/DBAIAzure.Core/Models/NodeExecutionState.cs`
- [X] T011 [P] Create `WorkflowNameConflictException` (typed domain exception; carries OwnerId and Name) in `src/DBAIAzure.Core/Models/WorkflowNameConflictException.cs`
- [X] T074 [P] Create `LlmUnavailableException` (typed domain exception; carries a plain-language message) in `src/DBAIAzure.Core/Models/LlmUnavailableException.cs` — thrown by `IWorkflowCodeGenerator` when the LLM is unreachable (referenced in T044)
- [X] T075 [P] Create `RouteDecision` record (`SelectedPortLabel string`) and an inline JSON schema constant in `src/DBAIAzure.Core/Models/RouteDecision.cs` — used by `FunctionRouteStep` (T056) to bind structured LLM JSON output for port selection without free-text parsing (Article VII)

### Composite domain models

- [X] T012 Create `WorkflowNode` record (Id, NodeType, Label, GoalPrompt, InputLabel, OutputLabel, FunctionConfig, PositionX, PositionY, IsConfigured, InputPorts, OutputPorts) in `src/DBAIAzure.Core/Models/WorkflowNode.cs` — depends on T004, T006
- [X] T013 Create `WorkflowEdge` record (Id, SourceNodeId, SourcePortId, TargetNodeId, TargetPortId, Label) in `src/DBAIAzure.Core/Models/WorkflowEdge.cs` — depends on T006
- [X] T014 Create `WorkflowDefinition` record (Id, Name, OwnerId, Nodes, Edges, Settings, ChatHistory, GeneratedCode, CreatedAt, LastModifiedAt, ThumbnailSvg) in `src/DBAIAzure.Core/Models/WorkflowDefinition.cs` — depends on T012, T013, T007, T008

### Seam interfaces

- [X] T015 [P] Create `IWorkflowRepository` interface (SaveAsync, GetAsync, ListByOwnerAsync, DeleteAsync, ExistsAsync — all owner-scoped per contract) in `src/DBAIAzure.Core/Interfaces/IWorkflowRepository.cs` — depends on T014
- [X] T016 [P] Create `IWorkflowExecutionOrchestrator` interface (RunUpdated event, StartRunAsync, GetRun, RequestStop, SubmitApproval) and `WorkflowExecutionRun` read-model record in `src/DBAIAzure.Core/Interfaces/IWorkflowExecutionOrchestrator.cs` — depends on T014, T010, T005
- [X] T017 [P] Create `IWorkflowCodeGenerator` interface (GenerateAsync streaming, RefineAsync with CodeDiff) in `src/DBAIAzure.Core/Interfaces/IWorkflowCodeGenerator.cs` — depends on T014, T009
- [X] T018 [P] Create `ILlmAvailabilityMonitor` interface (IsAvailable bool, StateChanged event, StartMonitoringAsync(CancellationToken), StopMonitoringAsync(CancellationToken)) in `src/DBAIAzure.Core/Interfaces/ILlmAvailabilityMonitor.cs`

### Storage layer

- [X] T019 Create `WorkflowDefinitionRecord` EF Core entity (Id TEXT PK, Name, OwnerId, NodesJson, EdgesJson, SettingsJson, ChatHistoryJson, GeneratedCode nullable, ThumbnailSvg nullable, CreatedAt, LastModifiedAt) in `src/DBAIAzure.Storage/Entities/WorkflowDefinitionRecord.cs` — depends on T014
- [X] T020 Extend `PipelineDbContext`: add `DbSet<WorkflowDefinitionRecord> Workflows`, `OnModelCreating` unique index `(OwnerId, Name)` and covering index on `OwnerId` in `src/DBAIAzure.Storage/PipelineDbContext.cs` — depends on T019
- [X] T021 Add idempotent `CREATE TABLE IF NOT EXISTS WorkflowDefinitions` migration to startup block in `src/DBAIAzure.Web/Program.cs` (same pattern as `ConnectorConfigs` migration) — depends on T020

### Repository — TDD order (test first)

- [X] T022 [P] Write failing `SqliteWorkflowRepositoryTests` (owner isolation, name-conflict exception, upsert round-trip, delete returns false for unknown id) in `tests/DBAIAzure.Tests/SqliteWorkflowRepositoryTests.cs`; use real SQLite `:memory:` via `SqliteConnection(":memory:")` with `Microsoft.EntityFrameworkCore.Sqlite` — NOT `EF InMemory` which does not enforce unique indexes and would allow the name-conflict test to pass vacuously (Article V) — depends on T015, T019
- [X] T023 Implement `SqliteWorkflowRepository` (upsert, get, list by owner, delete, exists; serialize/deserialize via `System.Text.Json`; enforce `WorkflowNameConflictException`) in `src/DBAIAzure.Storage/Repositories/SqliteWorkflowRepository.cs` — depends on T022
- [X] T024 Register `IWorkflowRepository` → `SqliteWorkflowRepository` as singleton in `src/DBAIAzure.Web/Program.cs` — depends on T023

### Blazor.Diagrams model layer

- [X] T025 [P] Create `WorkflowNodeModel` (extends `NodeModel`; carries `WorkflowNode`), `WorkflowPortModel` (extends `PortModel`; carries `WorkflowPort`), `WorkflowEdgeModel` (extends `LinkModel`; carries `WorkflowEdge`) in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowDiagramModels.cs` — depends on T012, T013, T006, T001

**Checkpoint**: Foundation complete — all user story phases can now begin.

---

## Phase 3: User Story 1 — First-Time User Builds a Workflow (Priority: P1) 🎯 MVP

**Goal**: A non-technical user can drag nodes from the palette onto the canvas, draw connections between them, undo/redo any action, and see a topologically valid workflow — all without errors or jargon. An example workflow is pre-loaded on first open.

**Independent Test**: Quickstart Scenario 1 — first-time user completes a 3-node workflow in under 5 minutes.

### Tests for US1

- [X] T026 [P] [US1] Write failing `WorkflowCanvasTests` — verify island-node amber badge, verify incompatible connection is rejected, verify palette search filters in <100ms in `tests/DBAIAzure.Tests/WorkflowCanvasTests.cs` — depends on T025, T073
- [X] T076 [P] [US1] Write failing `WorkflowUndoRedoTests` — verify undo reverses the last node placement, verify redo re-applies it, verify undo stack depth ≥50 steps, verify Ctrl+Z and Ctrl+Y keyboard handlers are registered in `tests/DBAIAzure.Tests/WorkflowUndoRedoTests.cs` — depends on T025, T073

### Implementation for US1

- [X] T027 [P] [US1] Create `WorkflowNodeRenderer.razor` — Blazor.Diagrams node template; warm amber styling for `AgenticReason`; cool blue/teal for function types; purple for `HumanApproval`; port label chips; amber badge overlay when `!IsConfigured` in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodeRenderer.razor` — depends on T025
- [X] T028 [P] [US1] Create `WorkflowNodePalette.razor` — 5 category groups (AI Steps, Decisions & Routing, Data, Notifications, Human Steps); real-time search with 100ms debounce; click-to-place at smart canvas position; drag-to-canvas support in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodePalette.razor` — depends on T025
- [X] T029 [US1] Create `WorkflowCanvas.razor` — wraps `DiagramCanvas`; handles drop events from palette; creates `WorkflowNodeModel` on drop; creates `WorkflowEdgeModel` on port drag; enforces port-direction guard (output→input only; other combos: remove link + show amber toast); enables `DeleteSelectionBehavior` for Delete-key edge removal; enables bezier control handles on arrows; syncs `DiagramCanvas` state to `WorkflowDefinition` in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowCanvas.razor` — depends on T027, T028
- [X] T077 [US1] Wire `UndoRedoBehavior` in `WorkflowCanvas.razor`: enable Blazor.Diagrams built-in `UndoRedoBehavior`; register `Ctrl+Z` (undo) and `Ctrl+Y` (redo) keyboard event handlers; depth limit 50 actions in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowCanvas.razor` — depends on T076, T029
- [X] T030 [US1] Create `WorkflowToolbar.razor` — Save, Undo, Redo, Settings, Chat toggle, Run buttons (Run disabled until foundation is wired); free-placement toggle button that sets `DiagramCanvas.Options.GridSnapToGrid = !isSnapping` and updates its label between "Snap to grid" (active) and "Free move" (active) — FR-01.2 toggle; "Auto-saved [HH:mm:ss]" indicator; timeout display; Undo/Redo buttons call `DiagramCanvas.BehaviorManager.GetBehavior<UndoRedoBehavior>()` and are disabled when stacks are empty; WCAG aria-labels on all buttons in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowToolbar.razor` — depends on T025
- [X] T078 [US1] Add multi-select support to `WorkflowCanvas.razor`: enable Blazor.Diagrams `SelectionBehavior` so the user can drag a rectangular area to select multiple nodes; selected group can be moved, copied, or deleted in a single action in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowCanvas.razor` — depends on T077, T029
- [X] T031 [US1] Create `WorkflowBuilder.razor` page — layout shell (palette left, canvas centre, toolbar top); routes to `/workflow-builder/{id?}`; pre-loads example 3-node workflow constant (summarize → approve → notify) when no `{id}` supplied in `src/DBAIAzure.Web/Pages/WorkflowBuilder.razor` — depends on T029, T030
- [X] T032 [US1] Register `WorkflowBuilder.razor` route and add "Workflow Builder" nav link in `src/DBAIAzure.Web/Pages/_Host.cshtml` and shared nav component — depends on T031

**Checkpoint**: US1 complete — non-technical user can build, connect, undo/redo, and multi-select nodes; example workflow pre-loaded.

---

## Phase 4: User Story 2 — Chat Assistant & Code Generation (Priority: P1) 🎯 MVP

**Goal**: Opening the chat panel summarizes the current topology; the user can request complete SK Process Framework code in plain language. Follow-up refinements show a diff. LLM unavailability degrades gracefully without breaking the canvas.

**Independent Test**: Quickstart Scenario 3 — generate code for 3-node workflow; confirm all nodes appear in output; save to project in <2s.

### Tests for US2

- [X] T042 [P] [US2] Write failing `LlmAvailabilityMonitorTests` — verify `StateChanged` fires on first failure, verify auto-restore fires when subsequent probe succeeds in `tests/DBAIAzure.Tests/LlmAvailabilityMonitorTests.cs` — depends on T018
- [X] T043 [P] [US2] Write failing `WorkflowTopologySerializerTests` — verify all node labels appear in output, verify all edges appear in `tests/DBAIAzure.Tests/WorkflowTopologySerializerTests.cs` — depends on T014
- [X] T044 [P] [US2] Write failing `WorkflowCodeGeneratorTests` — verify streaming calls `onToken`, verify `RefineAsync` diff correctly identifies added/removed lines, verify `LlmUnavailableException` (T074) is thrown when the service mock returns a failure, verify generated code string contains all node labels from the topology (SC-3 automated consistency check); add one `[Trait("Category","Integration")]` test method that calls a real `IChatCompletionService` with a 10-node synthetic workflow and asserts `Stopwatch.Elapsed.TotalSeconds <= 15` (SC-5 performance gate — requires live API key, run separately from unit suite) in `tests/DBAIAzure.Tests/WorkflowCodeGeneratorTests.cs` — depends on T017, T074
- [X] T045 [P] [US2] Write failing `WorkflowDesignSkillServiceTests` — verify questions are generated from topology, verify a previously answered question is not re-asked, verify user-deferral records `"user-deferred"` in `tests/DBAIAzure.Tests/WorkflowDesignSkillServiceTests.cs` — depends on T014, T007

### Implementation for US2

- [X] T046 [US2] Implement `LlmAvailabilityMonitor` — probes `IChatCompletionService` with a minimal call every 30 seconds; fires `StateChanged` on transition; auto-restores on recovery without page reload in `src/DBAIAzure.Web/Services/LlmAvailabilityMonitor.cs` — depends on T042
- [X] T047 [US2] Implement `WorkflowTopologySerializer` — converts `WorkflowDefinition` to structured LLM-readable text (node index with label/type/goal, edge routing table, settings summary) in `src/DBAIAzure.Web/Services/WorkflowTopologySerializer.cs` — depends on T043, T014
- [X] T048 [US2] Implement `WorkflowDesignSkillService` — `KernelPlugin` with `[KernelFunction] AnalyseTopologyAsync` returning structured JSON question list; conversational loop (ask one question at a time via `ChatHistory`); persist answers to `WorkflowSettings.DesignSkillAnswers`; skip previously answered questions; record `"user-deferred"` on dismiss and proceed without blocking in `src/DBAIAzure.Web/Services/WorkflowDesignSkillService.cs` — depends on T045, T047
- [X] T049 [US2] Implement `WorkflowCodeGenerator` — `GenerateAsync` builds system prompt from topology serializer + chat history; streams via `IChatCompletionService`; `RefineAsync` appends instruction to prior code as new chat turn and computes line-level Myers diff for `CodeDiff`; throws `LlmUnavailableException` (T074) on service failure in `src/DBAIAzure.Web/Services/WorkflowCodeGenerator.cs` — depends on T044, T047, T074
- [X] T050 [US2] Create `WorkflowChatPanel.razor` — resizable/dismissible sidebar; topology summary on open (names all nodes); streaming token display; code block with syntax highlight, Copy button, Save to project button; diff highlight overlay on refinement (added lines green, removed lines red, unchanged dimmed); "Your workflow has changed — regenerate?" banner on canvas modification; LLM unavailability: shows "The assistant is currently unavailable" + disables Submit when `ILlmAvailabilityMonitor.IsAvailable` is false; auto-restores without reload in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowChatPanel.razor` — depends on T049, T048, T046
- [X] T051 [US2] Wire `WorkflowChatPanel` into `WorkflowBuilder.razor` — chat toggle button opens/closes panel; canvas change fires "workflow changed" banner in chat; register `IWorkflowCodeGenerator`, `ILlmAvailabilityMonitor`, `WorkflowDesignSkillService` as singletons in `src/DBAIAzure.Web/Program.cs` — depends on T050, T031

**Checkpoint**: US2 complete — chat generates complete, diffable SK Process Framework code; LLM degradation handled.

---

## Phase 5: User Story 3 — Node Configuration Panel (Priority: P2)

**Goal**: Double-clicking any node opens an inline sidebar that lets the user describe the node's goal in plain language. No JSON, no code, no technical prompts.

**Independent Test**: Quickstart Scenario 2 — configure "Reason & Summarize" node; canvas label updates live.

### Tests for US3

- [X] T033 [P] [US3] Write failing `WorkflowNodeConfigPanelTests` — verify Goal field drives canvas label update, verify required-field amber badge on empty close in `tests/DBAIAzure.Tests/WorkflowNodeConfigPanelTests.cs` — depends on T012, T073

### Implementation for US3

- [X] T034 [P] [US3] Create `WorkflowNodeConfigPanel.razor` — opens as inline sidebar on node double-click; `AgenticReason` shows Goal (required), Input label, Output label (3 fields visible without scrolling, no raw JSON); function nodes show type-specific labelled controls (dropdown/toggle/text); required-field amber badge on close if Goal is empty; live canvas label update as user types Goal in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodeConfigPanel.razor` — depends on T029, T012
- [X] T035 [US3] Wire `WorkflowNodeConfigPanel` into `WorkflowBuilder.razor` — double-click on `WorkflowCanvas` node opens panel; panel close updates `WorkflowNode.IsConfigured` and redraws amber badge — depends on T034, T031

**Checkpoint**: US3 complete — all node types are configurable through plain-language forms.

---

## Phase 6: User Story 4 — Save, Name, and Reload a Workflow (Priority: P2)

**Goal**: Workflows persist across sessions. The personal gallery shows saved workflows with thumbnails and metadata. Unsaved changes are never lost silently.

**Independent Test**: Quickstart Scenario 5 — save "Billing Request Handler," navigate away, reload from gallery, confirm round-trip fidelity.

### Tests for US4

- [X] T036 [P] [US4] Write failing `WorkflowBuilderServiceTests` — verify auto-save debounce fires at 60s minimum, verify duplicate appends " (copy)", verify delete returns false for unknown id in `tests/DBAIAzure.Tests/WorkflowBuilderServiceTests.cs` — depends on T015, T014

### Implementation for US4

- [X] T037 [P] [US4] Create `WorkflowThumbnailGenerator` — generates SVG with `<circle>` per node (warm/cool/purple fill) and `<line>` per edge; bounding-box viewBox computation; cap at 50 nodes in `src/DBAIAzure.Web/Services/WorkflowThumbnailGenerator.cs` — depends on T014
- [X] T038 [US4] Implement `WorkflowBuilderService` — `SaveAsync` (upsert via `IWorkflowRepository`; generate thumbnail; prompt for name if unnamed); `LoadAsync`; `DuplicateAsync` (append " (copy)"); `DeleteAsync`; auto-save debounce (60-second minimum interval via `Timer`); exposes `LastSavedAt` for toolbar display in `src/DBAIAzure.Web/Services/WorkflowBuilderService.cs` — depends on T036, T037, T023
- [X] T039 [US4] Create `WorkflowGalleryCard.razor` — thumbnail SVG, workflow name, node count, last-modified date; one-click open; duplicate button; delete button (opens Blazor confirm modal: "Delete [Name]? This cannot be undone.") in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowGalleryCard.razor` — depends on T037
- [X] T040 [US4] Create `WorkflowGallery.razor` page — grid of `WorkflowGalleryCard` components; owner-scoped via `IWorkflowRepository.ListByOwnerAsync`; routes to `/workflow-gallery`; "No workflows yet" empty state in `src/DBAIAzure.Web/Pages/WorkflowGallery.razor` — depends on T039, T023
- [X] T041 [US4] Wire save, auto-save, and keyboard shortcut into `WorkflowBuilder.razor` — toolbar Save button calls `WorkflowBuilderService.SaveAsync`; register `Ctrl+S` / `⌘S` `keydown` handler that also calls `SaveAsync`; `WorkflowBuilderService` auto-saves on 60s debounce; toolbar shows "Auto-saved HH:mm:ss"; `NavigationManager.RegisterLocationChangingHandler` prompts "You have unsaved changes — save before leaving?" — depends on T038, T031

**Checkpoint**: US4 complete — workflows persist and round-trip with full fidelity.

---

## Phase 7: User Story 5 — In-Builder Workflow Execution (Priority: P2)

**Goal**: Clicking "Run" opens a plain-language input form; after LLM interprets the scenario, the workflow executes against the live system with real-time per-node animation and output. Users can stop, view failures in plain language, and feed results back to the chat.

**Independent Test**: Quickstart Scenarios 4 and 7 — full 3-node execution with per-node output; timeout enforcement at configured limit.

### SK Process execution runtime — tests first

- [X] T052 [P] [US5] Write failing `WorkflowRuntimeBuilderTests` — verify `ProcessBuilder` graph contains one step per node, verify edges produce correct event routing, verify `AgenticNodeStep` goal is injected per-node in `tests/DBAIAzure.Tests/WorkflowRuntimeBuilderTests.cs` — depends on T014, T016
- [X] T053 [P] [US5] Write failing `WorkflowExecutionOrchestratorTests` — verify `RequestStop` marks pending nodes Skipped within 1s, verify `SubmitApproval(false)` produces Failed + Skipped, verify `TimedOut` status when timeout elapses, verify `RunUpdated` fires per node state change, verify time from `StartRunAsync` to first `RunUpdated` carrying `NodeStatus.Active` is ≤500ms on a minimal 1-node mock process (SC-7 active-node visual latency gate) in `tests/DBAIAzure.Tests/WorkflowExecutionOrchestratorTests.cs` — depends on T016

### SK Process execution runtime — implementation

- [X] T054 [P] [US5] Create `WorkflowNodeEvents` static class — string constants for per-node input event and completion event; `NodeFailed` and `NodeSkipped` constants shared across step types in `src/DBAIAzure.Processes/WorkflowNodeEvents.cs`
- [X] T055 [P] [US5] Implement `AgenticNodeStep : KernelProcessStep` — reads `GoalPrompt` from step data parameter; calls `IChatCompletionService` with goal as system prompt; streams output via `IProgressReporter`; emits `NodeCompleted` or `NodeFailed` in `src/DBAIAzure.Processes/Steps/AgenticNodeStep.cs` — depends on T054
- [X] T056 [P] [US5] Implement `FunctionRouteStep : KernelProcessStep` — requests structured JSON from `IChatCompletionService` using the `RouteDecision` schema (T075); deserializes response to `RouteDecision`; validates `RouteDecision.SelectedPortLabel` against known output port labels; emits matched label event or `NodeFailed` if no match — no free-text string parsing per Article VII in `src/DBAIAzure.Processes/Steps/FunctionRouteStep.cs` — depends on T054, T075
- [X] T057 [P] [US5] Implement `FunctionTransformStep`, `FunctionNotifyStep`, `FunctionDataStep` in `src/DBAIAzure.Processes/Steps/FunctionTransformStep.cs`, `FunctionNotifyStep.cs`, `FunctionDataStep.cs` — depends on T054
- [X] T058 [P] [US5] Implement `HumanApprovalStep : KernelProcessStep` — reuses `IExternalKernelProcessMessageChannel` HITL pattern from `HitlPauseStep`; emits `AwaitHuman` via proxy; resumes on `SubmitApproval` in `src/DBAIAzure.Processes/Steps/HumanApprovalStep.cs` — depends on T054
- [X] T059 [US5] Implement `WorkflowInputTranslator` — calls `IChatCompletionService` with the node list and user's plain-language description; returns structured interpretation string; emits one-sentence confirmation for UI display in `src/DBAIAzure.Processes/WorkflowInputTranslator.cs` — depends on T054
- [X] T060 [US5] Implement `WorkflowRuntimeBuilder` — `Build(WorkflowDefinition)` method; iterates nodes adding `AddStepFromType<T>()` for each type; iterates edges wiring `OnEvent` → `SendEventTo`; injects `GoalPrompt` via `ProcessFunctionTargetBuilder` data; sets entry event from first node in `src/DBAIAzure.Processes/Pipeline/WorkflowRuntimeBuilder.cs` — depends on T052, T055, T056, T057, T058
- [X] T061 [US5] Implement `WorkflowExecutionOrchestrator` — `StartRunAsync` (translate input, build process, run in background); `GetRun`; `RequestStop` (signal cancellation); `SubmitApproval` (resolve HITL); `RunUpdated` event coalesced at ≤100ms per run; timeout via `CancellationTokenSource` or `RunToEndAsync` param per T003 result in `src/DBAIAzure.Processes/Pipeline/WorkflowExecutionOrchestrator.cs` — depends on T053, T060, T059

### Execution UI

- [X] T062 [US5] Create `WorkflowRunInputModal.razor` — "What scenario should I test?" textarea; calls `WorkflowInputTranslator.TranslateAsync` on submit; shows one-sentence LLM confirmation; "Confirm & Run" calls `IWorkflowExecutionOrchestrator.StartRunAsync`; LLM unavailable → shows degradation message, disables Confirm in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowRunInputModal.razor` — depends on T059, T046
- [X] T063 [US5] Create `WorkflowRunOutputPanel.razor` — per-node rows (label, status badge, output summary badge); expandable full output; "Did this do what you expected?" button on Completed nodes (opens `WorkflowChatPanel` pre-populated with node label + actual output); "Timed out after N minutes" message with link to settings when status is TimedOut; write a companion bUnit test in `tests/DBAIAzure.Tests/WorkflowRunOutputPanelTests.cs` asserting that after `RunUpdated` fires with `NodeStatus.Failed` the failure badge re-renders within 2 seconds (SC-8 failure-display latency gate) in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowRunOutputPanel.razor` — depends on T010, T005
- [X] T064 [US5] Add node animation and inline output badge to `WorkflowNodeRenderer.razor` — add `node-active` CSS class when `NodeStatus.Active`; Tailwind `animate-pulse` ring; green ring on `Completed`; red ring on `Failed`; grey ring on `Skipped`; clear animation on `NotStarted`; add collapsible `<div>` beneath the node showing `NodeExecutionState.OutputSummary` when status is `Completed` or `Failed` (FR-07.3 inline badge) in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodeRenderer.razor` — depends on T027, T010
- [X] T065 [US5] Wire execution into `WorkflowBuilder.razor` — subscribe to `IWorkflowExecutionOrchestrator.RunUpdated` → `InvokeAsync(StateHasChanged)` (same pattern as `Index.razor` + `PipelineOrchestrator`); Run button opens `WorkflowRunInputModal`; Stop button calls `RequestStop`; toolbar shows run status; register `IWorkflowExecutionOrchestrator` singleton in `src/DBAIAzure.Web/Program.cs` — depends on T061, T062, T063, T064, T031

**Checkpoint**: US5 complete — full execution loop with real-time animation, plain-language errors, and feedback hook.

---

## Phase 8: User Story 6 — Node Discovery (Priority: P3)

**Goal**: A first-time user can identify every node's purpose from the palette alone in under 3 minutes, without consulting external documentation.

**Independent Test**: Ask a first-time user to identify all palette nodes in under 3 minutes using only what is visible in the builder itself.

### Tests for US6

- [X] T079 [P] [US6] Write failing `WorkflowNodePaletteTooltipTests` — verify each node entry tooltip text contains no technical terms (assert no substring match on a jargon list: "API", "JSON", "HTTP", "endpoint", "payload", "schema"), verify animated detail panel resolves within 5 seconds, verify category collapse does not alter any canvas node positions in `tests/DBAIAzure.Tests/WorkflowNodePaletteTooltipTests.cs` — depends on T028, T073

### Implementation for US6

- [X] T066 [P] [US6] Enhance `WorkflowNodePalette.razor` — add hover tooltip to each node entry containing: plain-language name, one-sentence description (max 15 words), miniature I/O example ("Input: request text → Output: 3-bullet summary"); no technical terms in any tooltip text in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodePalette.razor` — depends on T028, T079
- [X] T067 [P] [US6] Add node detail panel to `WorkflowNodePalette.razor` — clicking a node (without dragging) opens a detail panel showing a short animated CSS demonstration of the node processing sample input and producing output; animation runs once (max 5 seconds) on open; collapsible category groups maintain canvas state on toggle in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodePalette.razor` — depends on T066

**Checkpoint**: US6 complete — all nodes self-describe without external documentation.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: WorkflowSettingsPanel, WCAG AA compliance, CHANGELOG update.

- [X] T068 [P] Create `WorkflowSettingsPanel.razor` — timeout field (1–60 minutes, plain-language label "Stop automatically after: N minutes"); displays currently configured timeout in toolbar when closed; stores value in `WorkflowSettings.ExecutionTimeoutMinutes`; WCAG AA `aria-label` on all controls in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowSettingsPanel.razor` — depends on T007
- [X] T069 [P] WCAG AA audit — add `aria-label` attributes to all palette controls, chat submit, all configuration form fields, all toolbar buttons; verify focus rings are not suppressed (remove any `outline-none` without a replacement `focus-visible:ring`); verify ≥4.5:1 contrast ratio for all text in `WorkflowNodePalette.razor`, `WorkflowChatPanel.razor`, `WorkflowNodeConfigPanel.razor`, `WorkflowToolbar.razor`; verify 1280×800 viewport shows no horizontal chrome scrolling (FR-01.5)
- [X] T070 Wire `WorkflowSettingsPanel` into `WorkflowBuilder.razor` Settings button — open/close toggle; propagate `ExecutionTimeoutMinutes` to `WorkflowBuilderService.SaveAsync` and `IWorkflowExecutionOrchestrator.StartRunAsync` in `src/DBAIAzure.Web/Pages/WorkflowBuilder.razor` — depends on T068, T038, T061
- [X] T071 Run all 7 quickstart validation scenarios from `specs/003-visual-workflow-builder/quickstart.md`; confirm all pass; record any deviations
- [X] T072 Update `CHANGELOG.md` with feature entry: Visual Workflow Builder (spec, plan, all user stories delivered)

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1: Setup          → no dependencies
Phase 2: Foundational   → depends on Phase 1 (T001, T073)
Phase 3 (US1)           → depends on Phase 2 complete (T004–T025)
Phase 4 (US2)           → depends on Phase 3 (T031); can run in parallel with Phase 5 and Phase 6
Phase 5 (US3)           → depends on Phase 3 (T029 canvas); can run in parallel with Phase 4 and Phase 6
Phase 6 (US4)           → depends on Phase 3 (T031 builder page); can run in parallel with Phase 4 and Phase 5
Phase 7 (US5)           → depends on Phase 2 (T014–T016) + Phase 3 (T031) + Phase 4 (T046 LLM monitor)
Phase 8 (US6)           → depends on Phase 3 (T028 palette)
Phase 9 (Polish)        → depends on all user story phases
```

### User Story Dependencies

- **US1 (P1)**: Starts after Phase 2 — no dependency on other stories
- **US2 (P1)**: Starts after Phase 3 — parallel with US3/US4/US5 once canvas is ready
- **US3 (P2)**: Starts after US1 canvas (T029) — no dependency on US2
- **US4 (P2)**: Starts after US1 builder page (T031) — no dependency on US2/US3
- **US5 (P2)**: Starts after Phase 2 foundational + US1 canvas + US2 LLM monitor (T046) — parallel with US3/US4
- **US6 (P3)**: Starts after US1 palette (T028) — no dependency on US2–US5

### Within Each Phase

- TDD order: failing test → implementation → passing test
- Models before services (within a phase)
- Services before Blazor components
- Components before page wiring

### Parallel Opportunities

- T004–T011, T074, T075: All enum/value-type tasks run in parallel
- T015–T018: All interface tasks run in parallel (after T014)
- T022, T025: Parallel after T019/T020
- T026, T076: Parallel (Phase 3 tests)
- T027, T028: Parallel (Phase 3 implementation)
- T042–T045: All US2 test tasks parallel
- T054–T058: All step implementations parallel
- T066, T067: Both US6 tasks parallel (after T079)
- T068, T069: Parallel (Phase 9)

---

## Parallel Example: Phase 2 Foundational

```
Parallel batch 1 (all enums, value types, and domain exceptions):
  T004 WorkflowNodeType.cs
  T005 WorkflowRunStatus.cs + NodeStatus.cs
  T006 PortDirection.cs + WorkflowPort.cs
  T007 WorkflowSettings.cs
  T008 WorkflowChatMessage.cs + ChatRole.cs
  T009 CodeDiff.cs
  T010 NodeExecutionState.cs
  T011 WorkflowNameConflictException.cs
  T074 LlmUnavailableException.cs          ← new
  T075 RouteDecision.cs                    ← new

Sequential: T012 → T013 → T014

Parallel batch 2 (all interfaces):
  T015 IWorkflowRepository.cs
  T016 IWorkflowExecutionOrchestrator.cs
  T017 IWorkflowCodeGenerator.cs
  T018 ILlmAvailabilityMonitor.cs

Sequential: T019 → T020 → T021

Parallel: T022 (test, SQLite :memory:) + T025 (Blazor.Diagrams models)
Sequential: T022 → T023 → T024
```

---

## Implementation Strategy

### MVP First (US1 + US2 Only)

1. Complete Phase 1 (Setup) + Phase 2 (Foundational)
2. Complete Phase 3 (US1) — canvas functional, nodes connect, undo/redo works
3. Complete Phase 4 (US2) — chat generates code
4. **STOP AND VALIDATE**: Run Quickstart Scenarios 1 and 3
5. Demo: non-technical user builds and generates code — core value proposition proven

### Incremental Delivery

1. Setup + Foundational → infrastructure ready
2. US1 → canvas functional (MVP slice 1)
3. US2 → code generation works (MVP slice 2 — demo-ready)
4. US3 → node config refined
5. US4 → persistence and gallery
6. US5 → in-builder execution
7. US6 + Polish → discovery and hardening

### Parallel Team Strategy

After Phase 2 + Phase 3:
- Developer A: US2 (chat/code gen) → US5 (execution)
- Developer B: US3 (node config) → US6 (discovery)
- Developer C: US4 (persistence) → Polish

---

## Notes

- `[P]` tasks touch different files with no incomplete shared dependency — safe to run concurrently
- `[Story]` label maps every task to its user story for traceability
- TDD: every failing test must be committed and verified failing before implementation begins
- Commit after each logical group (one task or one tightly-coupled pair)
- Stop at each **Checkpoint** to validate the story independently before moving forward
- Article V: unit tests are 100% mocked (Moq/hand-rolled fakes) or real SQLite `:memory:` — never EF InMemory provider
- Article VII: canvas is Blazor.Diagrams; execution uses SK `ProcessBuilder`; LLM routing uses structured JSON schema binding — no free-text parsing
- Article IV: every new public type/member carries an XML doc comment — implement alongside each file (do not defer)
