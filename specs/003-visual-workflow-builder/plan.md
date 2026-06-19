# Implementation Plan: Visual Workflow Builder

**Branch**: `feature/visual-workflow-builder` | **Date**: 2026-06-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/003-visual-workflow-builder/spec.md`

---

## Summary

Add a Visual Workflow Builder to the existing Blazor Server application that allows any user —
regardless of technical background — to design automated workflows by dragging agentic and
function-based nodes onto a canvas, connecting them visually, and generating complete SK Process
Framework source code via a built-in LLM chat assistant. Includes in-builder workflow execution
with real-time per-node status streaming, per-workflow configurable timeout, a personal workflow
gallery, and a Workflow Design Skill that guides users through logical gaps conversationally.
LLM features degrade gracefully when the assistant is unavailable; the canvas remains fully
operational at all times.

---

## Technical Context

**Language/Version**: C# 12 / .NET 8.0

**Primary Dependencies**:
- ASP.NET Core 8.0 (Blazor Server — existing)
- Semantic Kernel 1.77.0 / Process Framework 1.77.0-alpha (existing)
- Entity Framework Core 8.0.11 / SQLite (existing — `PipelineDbContext`)
- `Blazor.Diagrams` 3.1.x + `Blazor.Diagrams.Core` 3.1.x **[NEW — canvas library]**
- Tailwind CSS CDN (existing dashboard palette)
- `System.Text.Json` (transitive — existing)

**Storage**: SQLite via EF Core — new `WorkflowDefinitionRecord` entity added to existing
`PipelineDbContext`. No new storage backend (Assumption 4 in spec).

**Testing**: xUnit + Moq + **bUnit 1.x** [NEW — required for Blazor Razor component tests];
unit tests 100% mocked (no I/O, <10 ms each); repository tests use real SQLite `:memory:`
(not EF InMemory — EF InMemory does not enforce unique indexes per Article V);
integration tests against real SK Process Framework execution.

**Target Platform**: ASP.NET Core web server hosting Blazor Server (`DBAIAzure.Web`)

**Performance Goals**:
- Chat assistant produces complete code in under 15 seconds for workflows up to 10 nodes (SC-5)
- Active node visually distinguishable within 500 ms of execution reaching it (SC-7)
- Palette search results update within 100 ms of last keystroke (FR-02.3)
- Save completes in under 3 seconds (FR-06.1)

**Constraints**:
- Cycles/loops permitted; no hardcoded structural guardrails in UI (Clarification Q4)
- Per-workflow configurable timeout; 5-minute default (Clarification Q3)
- Personal gallery — each user sees only their own workflows (Clarification Q2)
- Canvas is pointer-only; all non-canvas surfaces meet WCAG 2.1 Level AA (Clarification Q1)
- LLM features degrade gracefully; canvas never requires LLM availability (Clarification Q5)

**Scale/Scope**: Single-tenant POC — personal gallery per authenticated user.

---

## Constitution Check

| Article | Gate | Status | Notes |
|---------|------|--------|-------|
| I — Prime Directive | Best route, production-ready | ✅ PASS | Framework-first throughout; Blazor.Diagrams avoids hand-rolling canvas |
| IV — Code Quality | Self-documenting names, XML docs, ≤40-line methods | ✅ PASS | All new public types carry XML doc comments |
| V — Testing | TDD; unit (mocked, <10 ms); integration (real SK execution) | ✅ PASS | Each contract defines explicit test obligations |
| VII — Framework-First | SK primitives before hand-rolling | ✅ PASS — detail below | |
| IX — Secrets | Never hard-coded | ✅ PASS | LLM key resolved from `IConnectorConfigRepository` at run time |

**Article VII detail**:

| Concern | Framework primitive used | Custom? | Drift justification |
|---------|-------------------------|---------|---------------------|
| Drag-and-drop canvas | — | `Blazor.Diagrams` 3.1.x | SK has no browser UI component |
| Topology serialization | — | JSON blob in SQLite | SK `KernelProcess` not stably serializable in 1.77.0-alpha (`SKEXP0080`) |
| Runtime execution | `ProcessBuilder` (dynamic) | Thin `WorkflowRuntimeBuilder` wrapper | All routing, state, HITL owned by SK |
| LLM chat / code gen | `ChatHistory` + `IChatCompletionService` | None | Already registered in DI |
| Workflow Design Skill | `KernelPlugin` + `[KernelFunction]` | Thin plugin class | Native SK capability |
| Real-time streaming | Extend `IProgressReporter` pattern | Node-level event type only | Existing `BoundProgressReporter` pattern |
| Persistence | EF Core `PipelineDbContext` (existing) | New entities + DbSet | Same pattern as `ConnectorConfigRecord` |
| HITL in execution | `IExternalKernelProcessMessageChannel` | `HumanApprovalStep` reuses existing | Pattern established in `HitlPauseStep` |

---

## Project Structure

### Documentation (this feature)

```text
specs/003-visual-workflow-builder/
├── plan.md                                         ← this file
├── research.md                                     ← Phase 0 decisions and rationale
├── data-model.md                                   ← domain types, EF entities, state transitions
├── quickstart.md                                   ← end-to-end validation guide (7 scenarios)
├── contracts/
│   ├── iworkflow-repository.md                     ← persistence seam
│   ├── iworkflow-execution-orchestrator.md         ← execution lifecycle seam
│   └── iworkflow-code-generator.md                 ← code generation seam
└── tasks.md                                        ← generated by /speckit-tasks
```

### Source Code Layout

```text
src/
├── DBAIAzure.Core/
│   ├── Interfaces/
│   │   ├── IWorkflowRepository.cs                  [NEW] persistence seam
│   │   ├── IWorkflowExecutionOrchestrator.cs       [NEW] execution lifecycle seam + WorkflowExecutionRun record
│   │   ├── IWorkflowCodeGenerator.cs               [NEW] code generation seam
│   │   └── ILlmAvailabilityMonitor.cs              [NEW] LLM health / graceful degradation
│   └── Models/
│       ├── WorkflowDefinition.cs                   [NEW] canonical in-memory workflow
│       ├── WorkflowNode.cs                         [NEW] canvas node (Goal, InputLabel, OutputLabel — no Constraints field)
│       ├── WorkflowEdge.cs                         [NEW] directed connection
│       ├── WorkflowPort.cs                         [NEW] named connection point
│       ├── WorkflowSettings.cs                     [NEW] per-workflow config (timeout, etc.)
│       ├── WorkflowChatMessage.cs                  [NEW] one turn in persisted chat history
│       ├── WorkflowNodeType.cs                     [NEW] enum — 6 node types across 5 categories
│       ├── WorkflowRunStatus.cs                    [NEW] enum — 7 states
│       ├── NodeExecutionState.cs                   [NEW] runtime per-node status record
│       ├── CodeDiff.cs                             [NEW] diff lines for chat panel highlight
│       ├── RouteDecision.cs                        [NEW] typed record for FunctionRouteStep JSON schema binding (Article VII)
│       ├── WorkflowNameConflictException.cs        [NEW] typed domain exception
│       └── LlmUnavailableException.cs              [NEW] typed domain exception for LLM connectivity failures
│
├── DBAIAzure.Storage/
│   ├── Entities/
│   │   └── WorkflowDefinitionRecord.cs             [NEW] EF Core entity (JSON blob columns)
│   ├── PipelineDbContext.cs                        [MODIFY] add DbSet<WorkflowDefinitionRecord>
│   └── Repositories/
│       └── SqliteWorkflowRepository.cs             [NEW] IWorkflowRepository impl
│
├── DBAIAzure.Processes/
│   ├── Steps/
│   │   ├── AgenticNodeStep.cs                      [NEW] generic LLM step (goal as system prompt)
│   │   ├── FunctionRouteStep.cs                    [NEW] LLM-evaluated branching step
│   │   ├── FunctionTransformStep.cs                [NEW] LLM-driven data reshape step
│   │   ├── FunctionNotifyStep.cs                   [NEW] deterministic notification step
│   │   ├── FunctionDataStep.cs                     [NEW] deterministic read/write step
│   │   └── HumanApprovalStep.cs                    [NEW] HITL pause via IExternalKernelProcessMessageChannel
│   ├── Pipeline/
│   │   ├── WorkflowRuntimeBuilder.cs               [NEW] ProcessBuilder from WorkflowDefinition
│   │   └── WorkflowExecutionOrchestrator.cs        [NEW] IWorkflowExecutionOrchestrator impl
│   ├── WorkflowNodeEvents.cs                       [NEW] string event constants for node routing
│   └── WorkflowInputTranslator.cs                  [NEW] plain-language → structured ProcessInput
│
└── DBAIAzure.Web/
    ├── Program.cs                                  [MODIFY] register new services + Blazor.Diagrams
    ├── Pages/
    │   ├── WorkflowGallery.razor                   [NEW] personal gallery (/workflow-gallery)
    │   └── WorkflowBuilder.razor                   [NEW] builder shell (/workflow-builder/{id?})
    ├── Components/WorkflowBuilder/
    │   ├── WorkflowCanvas.razor                    [NEW] Blazor.Diagrams DiagramCanvas wrapper
    │   ├── WorkflowNodePalette.razor               [NEW] categorized palette + real-time search
    │   ├── WorkflowChatPanel.razor                 [NEW] chat sidebar + code block + diff view
    │   ├── WorkflowNodeConfigPanel.razor           [NEW] inline double-click config sidebar
    │   ├── WorkflowSettingsPanel.razor             [NEW] per-workflow settings (timeout, etc.)
    │   ├── WorkflowRunOutputPanel.razor            [NEW] per-node outputs + run status
    │   ├── WorkflowRunInputModal.razor             [NEW] plain-language execution input form
    │   ├── WorkflowToolbar.razor                   [NEW] Run/Stop, Save, Settings, Chat buttons
    │   ├── WorkflowNodeRenderer.razor              [NEW] Blazor.Diagrams node template (styled per type)
    │   └── WorkflowGalleryCard.razor               [NEW] single gallery card (thumbnail + metadata)
    └── Services/
        ├── WorkflowBuilderService.cs               [NEW] save/load/duplicate/delete + auto-save
        ├── WorkflowCodeGenerator.cs               [NEW] IWorkflowCodeGenerator impl
        ├── WorkflowDesignSkillService.cs           [NEW] Workflow Design Skill conversational loop
        ├── WorkflowTopologySerializer.cs           [NEW] WorkflowDefinition → LLM-readable text
        ├── WorkflowThumbnailGenerator.cs           [NEW] SVG thumbnail from node positions
        └── LlmAvailabilityMonitor.cs              [NEW] ILlmAvailabilityMonitor impl

tests/
└── DBAIAzure.Tests/
    ├── SqliteWorkflowRepositoryTests.cs            [NEW] owner isolation, name uniqueness, upsert (real SQLite :memory:)
    ├── WorkflowRuntimeBuilderTests.cs              [NEW] ProcessBuilder graph from topology
    ├── WorkflowCodeGeneratorTests.cs               [NEW] prompt serialization, diff accuracy, SC-3 node-label consistency check
    ├── WorkflowExecutionOrchestratorTests.cs       [NEW] timeout, stop, approval, RunUpdated
    ├── WorkflowDesignSkillServiceTests.cs          [NEW] question generation, answer persistence
    ├── LlmAvailabilityMonitorTests.cs              [NEW] StateChanged, auto-restore
    ├── WorkflowTopologySerializerTests.cs          [NEW] node labels and edges appear in serialized output
    ├── WorkflowCanvasTests.cs                      [NEW] island badge, invalid connection rejection, palette search (bUnit)
    ├── WorkflowNodeConfigPanelTests.cs             [NEW] Goal drives canvas label, required-field badge (bUnit)
    ├── WorkflowBuilderServiceTests.cs              [NEW] auto-save debounce, duplicate suffix, delete no-op
    ├── WorkflowUndoRedoTests.cs                    [NEW] undo/redo depth ≥50, keyboard handlers (bUnit)
    ├── WorkflowNodePaletteTooltipTests.cs          [NEW] no technical terms in tooltips, animation ≤5s (bUnit)
    └── WorkflowNodePaletteTests.cs                 ← covered by above two files
```

---

## Implementation Phases

### Phase 1 — Storage & Domain Foundation

**Goal**: All domain types, interfaces, EF entity, SQLite repository. No UI. Repository
tests pass against in-memory SQLite. Produces the stable seam all other phases depend on.

**Deliverables**:
- All `DBAIAzure.Core` models and interfaces listed above
- `WorkflowDefinitionRecord` + `PipelineDbContext` `DbSet` + `OnModelCreating` indexes
- `SqliteWorkflowRepository` (upsert, get, list, delete, exists; owner-scoped)
- `CREATE TABLE IF NOT EXISTS` idempotent startup migration in `Program.cs`
- `SqliteWorkflowRepositoryTests` — owner isolation, name conflict, round-trip, delete no-op

**Key design note**: `WorkflowDefinitionRecord` stores nodes, edges, settings, and chat
history as JSON TEXT blob columns — no child tables. The topology is always read/written
atomically, so a blob is simpler and faster than 3 join tables; this mirrors the existing
`ConnectorConfigRecord.ConfigJson` pattern.

---

### Phase 2 — SK Process Execution Runtime

**Goal**: A `WorkflowDefinition` can be executed as a live SK `KernelProcess`. Fires
`RunUpdated` events. Works with mocked `IChatCompletionService` in tests.

**Deliverables**:
- `WorkflowNodeEvents` string constants (one input + one completion event per node type)
- Six `KernelProcessStep` subclasses — see source layout above
- `WorkflowRuntimeBuilder.Build(WorkflowDefinition)` → `KernelProcess` via `ProcessBuilder`
- `WorkflowInputTranslator.TranslateAsync` — plain-language description → structured input
- `WorkflowExecutionOrchestrator` + `WorkflowExecutionRun`
- `WorkflowRuntimeBuilderTests`, `WorkflowExecutionOrchestratorTests`

**Key design notes**:
- `AgenticNodeStep` reads `GoalPrompt` from the step's data parameter (injected by
  `WorkflowRuntimeBuilder` via `ProcessFunctionTargetBuilder`) — one step type, N goals.
- `FunctionRouteStep` requests structured JSON from `IChatCompletionService` bound to a
  `RouteDecision` record (Article VII — no free-text string matching); validates
  `RouteDecision.SelectedPortLabel` against known output port labels; emits `NodeFailed`
  if no match.
- Timeout is enforced by passing `TimeSpan.FromMinutes(settings.ExecutionTimeoutMinutes)`
  to `LocalKernelProcessFactory.RunToEndAsync` — the existing API already accepts this.
- `HumanApprovalStep` reuses `IExternalKernelProcessMessageChannel` exactly as `HitlPauseStep`.

---

### Phase 3 — Canvas UI

**Goal**: Functional drag-and-drop canvas. Nodes placed, connected, configured, and saved/
loaded. No chat or execution wired yet.

**Deliverables**:
- `Blazor.Diagrams` 3.1.x NuGet added to `DBAIAzure.Web.csproj`
- `WorkflowNodeModel`, `WorkflowPortModel`, `WorkflowEdgeModel` (Blazor.Diagrams subclasses)
- `WorkflowCanvas.razor` — `DiagramCanvas` wrapper; palette drop handler; port-direction guard
- `WorkflowNodePalette.razor` — 5 categories, 100 ms debounced search, click-to-place
- `WorkflowNodeRenderer.razor` — Blazor.Diagrams node template; warm/cool/purple styling;
  port labels; amber config badge
- `WorkflowNodeConfigPanel.razor` — inline sidebar; 3 fields for agentic; per-type controls
  for function nodes; live label update on canvas as user types
- `WorkflowSettingsPanel.razor` — timeout field (1–60 min)
- `WorkflowToolbar.razor` — Save, Settings, Chat, Run/Stop; auto-save timestamp
- `WorkflowBuilder.razor` + `WorkflowGallery.razor` pages
- `WorkflowBuilderService` — save with 60-second auto-save debounce; load; duplicate; delete
- `WorkflowThumbnailGenerator` — SVG circles + lines from node positions
- Unsaved-changes guard — `RegisterLocationChangingHandler`

**Key design note**: The only canvas-level connection block is output→output or input→input
(physically impossible). All other topology concerns — including cycles — are handled by the
Workflow Design Skill in Phase 4, not by canvas validation.

---

### Phase 4 — Chat Assistant & Code Generation

**Goal**: Chat panel generates SK Process Framework code. Design Skill activates before
code generation and before execution. Diff view on refinement. LLM degradation handled.

**Deliverables**:
- `WorkflowTopologySerializer` — `WorkflowDefinition` → human-readable node/edge description
- `WorkflowCodeGenerator` — token streaming; `GenerateAsync`; `RefineAsync` with Myers diff
- `WorkflowDesignSkillService` — `KernelPlugin`; `AnalyseTopologyAsync` `[KernelFunction]`;
  question loop; persists answers to `WorkflowSettings.DesignSkillAnswers`; user-deferral
- `WorkflowChatPanel.razor` — topology summary on open; streaming token display;
  code block with Copy/Save buttons; diff highlight; "workflow changed" banner
- `WorkflowRunInputModal.razor` — "What scenario?" form; LLM interpretation confirmation
- `LlmAvailabilityMonitor` — 30-second probe; `StateChanged` event; auto-restore on recovery
- `WorkflowCodeGeneratorTests`, `WorkflowDesignSkillServiceTests`, `LlmAvailabilityMonitorTests`

**Key design note**: The Workflow Design Skill is implemented as a `KernelPlugin` (native SK)
for the structured topology analysis, and plain `ChatHistory` for the conversational follow-up
loop — no parallel mechanism. The Design Skill runs before both Generate and Run; dismissing
a question records `"user-deferred"` and the action proceeds without blocking.

---

### Phase 5 — Execution UI & Real-Time Streaming

**Goal**: Run button wired end-to-end. Node animations. Run Output panel. Stop/cancel.
Timeout UI. Post-run feedback hook.

**Deliverables**:
- `WorkflowRunOutputPanel.razor` — per-node status badges; expandable full output
- Node animation — CSS `animate-pulse` class on `Active` nodes in `WorkflowNodeRenderer.razor`
- `WorkflowBuilder.razor` subscribes to `IWorkflowExecutionOrchestrator.RunUpdated` →
  `InvokeAsync(StateHasChanged)` (same pattern as `Index.razor` + `PipelineOrchestrator`)
- "Did this do what you expected?" — completed node badge → chat panel pre-populated with
  node label + actual output
- Timeout message in toolbar when `WorkflowRunStatus.TimedOut`
- `WorkflowExecutionOrchestratorTests` — stop, timeout, approval scenarios

**Key design note**: `RunUpdated` is coalesced within a 100 ms window in the orchestrator to
prevent flooding Blazor's render queue (≤10 Hz per run, per execution contract).

---

### Phase 6 — Polish, Gallery, and WCAG

**Goal**: Auto-save, unsaved-changes prompt, full gallery CRUD, thumbnails, WCAG AA,
and pre-loaded example workflow.

**Deliverables**:
- Auto-save: 60-second debounce in `WorkflowBuilderService`; toolbar timestamp update
- Unsaved-changes: `RegisterLocationChangingHandler` intercept with single-sentence prompt
- Gallery: grid layout; duplicate (append " (copy)"); delete with Blazor confirm modal
- Thumbnail: SVG bounding-box calculation; capped at 50 nodes for performance
- WCAG AA: `aria-label` on all interactive controls; focus rings; ≥4.5:1 text contrast
- Pre-loaded example workflow: hard-coded 3-node constant (summarize → approve → notify)

---

## Dependency Order

```
Phase 1 (Domain + Storage)
    │
    ├──── Phase 2 (SK Runtime)     ─── independent of Phase 3
    │
    └──── Phase 3 (Canvas UI)      ─── independent of Phase 2
                    │
                    └──── Phase 4 (Chat + Code Gen + Design Skill)
                                    │
                                    └──── Phase 5 (Execution UI)
                                                    │
                                                    └──── Phase 6 (Polish)
```

Phases 2 and 3 can be developed in parallel after Phase 1 completes.

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| `LocalKernelProcessFactory.RunToEndAsync` timeout param absent in 1.77.0-alpha | Medium | High | Verify signature before Phase 2 starts; fallback: wrap with `CancellationTokenSource` |
| Blazor.Diagrams 3.1.x breaking API change in minor version | Low | Medium | Pin exact NuGet version; review changelog before any upgrade |
| `FunctionRouteStep` LLM output mismatches available port labels → routing dead-end | Medium | High | Validate LLM output against known labels; emit `NodeFailed` on any mismatch |
| Auto-save + manual save race → `LastModifiedAt` conflict | Low | Low | Repository upsert is last-writer-wins; atomic SQLite write prevents partial state |
| SVG thumbnail performance on large workflows | Low | Low | Cap thumbnail rendering at 50 nodes; render simplified circles for excess |
