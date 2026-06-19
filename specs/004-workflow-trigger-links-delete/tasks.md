# Tasks: Workflow Trigger Node, Directional Links & Node Deletion

**Input**: Design documents from `specs/004-workflow-trigger-links-delete/`

**Prerequisites**: plan.md ✓ | spec.md ✓ | research.md ✓ | data-model.md ✓ | contracts/ ✓

**Tests**: Included — Article V of the project constitution requires TDD (Red → Green → Refactor).
Write each test task and confirm it fails before implementing the corresponding task.

**Organization**: Tasks are grouped by user story to enable independent implementation
and testing. All four user stories can proceed to Phase 3 once Phase 2 is complete.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no blocking dependencies)
- **[Story]**: Maps to user story (US1–US4) from spec.md
- Exact file paths included in every task description

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Wire new services into DI and scaffold new file locations so all phases
can compile immediately.

- [X] T001 Register `IWorkflowValidator` as a singleton in `src/DBAIAzure.Web/Program.cs` (add `builder.Services.AddSingleton<IWorkflowValidator, WorkflowValidator>()`) — directories `src/DBAIAzure.Core/Exceptions/` and `src/DBAIAzure.Core/Validation/` do not yet exist; create them with placeholder `.gitkeep` files so the solution builds
- [X] T002 [P] Create `src/DBAIAzure.Web/wwwroot/css/workflow-canvas-animations.css` as an empty scaffold file and add a `<link>` reference to it in `src/DBAIAzure.Web/Components/App.razor` (after the existing stylesheet links) so the CSS is served from the first run

**Checkpoint**: `dotnet build` completes with zero errors before proceeding to Phase 2

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core domain model changes that every user story depends on. No user story
work can proceed until this phase is complete and all unit tests pass.

**⚠️ CRITICAL**: Write each test task first, confirm it fails (RED), then implement
the corresponding production code (GREEN).

### Tests — Write First, Confirm Failure Before Implementing

- [X] T003 [P] Write unit test class `WorkflowNodeTypeTests` in `tests/DBAIAzure.Tests/WorkflowNodeTypeTests.cs` covering: (a) `WorkflowNodeType.Trigger` has numeric value 0; (b) `WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start / Trigger")` returns a node with zero `InputPorts`, exactly one `OutputPort` with `Label == "Begin"` and `Direction == PortDirection.Output`, `IsConfigured == false`, and `FunctionConfig` containing the key `initialDataDescription`
- [X] T004 [P] Write unit test class `WorkflowValidatorTests` in `tests/DBAIAzure.Tests/WorkflowValidatorTests.cs` covering: (a) empty Nodes list returns message matching VAL-001 text; (b) two Trigger nodes returns message matching VAL-002 text; (c) one Trigger node with no edges (island) returns VAL-003; (d) valid one-Trigger two-connected-node workflow returns empty list

### Implementation — Run After Tests Are Red

- [X] T005 Add `Trigger = 0` as the first value in `WorkflowNodeType` enum in `src/DBAIAzure.Core/Models/WorkflowNodeType.cs`; shift existing values to 1–6 and update XML doc comments to reflect that numeric value also controls palette sort order
- [X] T006 Extend `WorkflowNode.CreateNew` factory in `src/DBAIAzure.Core/Models/WorkflowNode.cs` to handle `WorkflowNodeType.Trigger`: produce zero `InputPorts`, one `OutputPort` (`Id = "begin"`, `Label = "Begin"`, `Direction = PortDirection.Output`), `Label = "Start / Trigger"`, `InputLabel = "Trigger"`, `OutputLabel = "Begin"`, `FunctionConfig = "{\"initialDataDescription\":\"\"}"`, `IsConfigured = false`
- [X] T007 Add `ThrowIfInvalid()` method to `WorkflowDefinition` record in `src/DBAIAzure.Core/Models/WorkflowDefinition.cs` that throws `InvalidOperationException` with plain-language message when `Nodes.Count(n => n.NodeType == WorkflowNodeType.Trigger) > 1`; called from `WorkflowBuilderService.SaveAsync`
- [X] T008 [P] Create `WorkflowValidationException` in `src/DBAIAzure.Core/Exceptions/WorkflowValidationException.cs` as a sealed class extending `Exception` with a `IReadOnlyList<string> Messages` property; include XML doc comment explaining it carries user-displayable validation messages
- [X] T009 [P] Create `IWorkflowValidator` interface in `src/DBAIAzure.Core/Interfaces/IWorkflowValidator.cs` with single method `IReadOnlyList<string> Validate(WorkflowDefinition definition)`; XML doc on the method must state messages are written for non-technical users
- [X] T010 Implement `WorkflowValidator` in `src/DBAIAzure.Core/Validation/WorkflowValidator.cs` enforcing VAL-001 ("Add a starting trigger to run this workflow."), VAL-002 ("A workflow may contain only one starting trigger. Remove the extra trigger before saving."), and VAL-003 ("One or more steps are not connected to anything. Connect all steps before running."); depends on T008, T009
- [X] T011 Update `WorkflowBuilderService.SaveAsync` in `src/DBAIAzure.Web/Services/WorkflowBuilderService.cs` to call `_validator.Validate(definition)` before `IWorkflowRepository.SaveAsync`; if messages is non-empty, throw `WorkflowValidationException(messages)` without persisting; constructor-inject `IWorkflowValidator`
- [X] T012 Catch `WorkflowValidationException` in `src/DBAIAzure.Web/Pages/WorkflowBuilder.razor` and display each `Messages` entry as an amber Tailwind banner (`bg-amber-900/50 border border-amber-600 text-amber-200`) positioned above the toolbar; banners auto-dismiss after 8 seconds

**Checkpoint**: `dotnet test --filter "FullyQualifiedName~WorkflowNodeType|FullyQualifiedName~WorkflowValidator"` passes (GREEN). Phase 3+ can now begin.

---

## Phase 3: User Story 1 — Trigger Node Placement (Priority: P1) 🎯 MVP

**Goal**: A user can place a "Start / Trigger" node, see it distinguished visually
from all other node types, configure it with plain-language fields, and be blocked
from running or generating code until a Trigger is present. A second Trigger cannot
be placed.

**Independent Test**: Place a Trigger node, attempt to place a second one (must be
blocked with amber banner), double-click to configure (two plain-language fields only),
and attempt to Run with no Trigger (must show toolbar badge).
See quickstart.md Scenario 1 for full pass criteria.

### Tests — Write First (RED)

- [X] T013 [P] [US1] Write bUnit test `WorkflowCanvasTriggerTests` in `tests/DBAIAzure.Tests/WorkflowCanvasTriggerTests.cs`: render `WorkflowNodePalette` and assert the first category rendered is labelled "Triggers" and contains an item whose `data-node-type` attribute equals `"Trigger"`
- [X] T014 [P] [US1] Add to `WorkflowCanvasTriggerTests.cs`: simulate placing one Trigger node, then simulate dropping a second — assert an amber banner with text "Every workflow has exactly one starting trigger" is rendered and no second Trigger `WorkflowNodeModel` is added to the diagram
- [X] T015 [P] [US1] Write bUnit test `WorkflowNodeRendererTriggerTests` in `tests/DBAIAzure.Tests/WorkflowNodeRendererTriggerTests.cs`: render `WorkflowNodeRenderer` with a `WorkflowNodeModel` whose `NodeType == Trigger` — assert: (a) a CSS class containing `green` or `emerald` is present on the node container; (b) no element with the port-alignment `data-port-alignment="Left"` is rendered; (c) text "Start here" is present in the rendered output

### Implementation (GREEN)

- [X] T016 [P] [US1] Add a "Triggers" category entry as the first item in the category list in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodePalette.razor`; include one "Start / Trigger" entry with `NodeType = WorkflowNodeType.Trigger`, description "Marks where your workflow begins. Every workflow has exactly one. Connect it to your first step.", and green/emerald colour accent; update the `GetCategoryOrder` sort so `"Triggers"` always sorts first regardless of enum ordering
- [X] T017 [P] [US1] Update `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodeRenderer.razor` for the Trigger type: apply `border-emerald-500 bg-emerald-950` Tailwind classes when `Node.Domain.NodeType == WorkflowNodeType.Trigger`; render a play-circle SVG icon in emerald; render `<span class="text-xs text-emerald-400 mt-1">Start here</span>` below the node name; suppress the input-ports column (`@if (Node.Domain.NodeType != WorkflowNodeType.Trigger)`) so no left-side ports appear
- [X] T018 [P] [US1] Add a Trigger-specific branch to `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodeConfigPanel.razor`: when `SelectedNode.Domain.NodeType == WorkflowNodeType.Trigger`, replace the standard Goal/Input/Output fields with two `<textarea>` controls labelled "What starts this workflow?" (bound to `GoalPrompt`) and "What information is available at the start?" (bound to `FunctionConfig` JSON key `initialDataDescription`); all other node types render the existing fields unchanged
- [X] T019 [US1] In `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowCanvas.razor`, extend the palette drop handler (`AddNodeToCanvas`): (a) before adding any node whose `NodeType == WorkflowNodeType.Trigger`, check if a Trigger already exists in `_diagram.Nodes`; if yes, cancel the drop, show an amber `_bannerMessage` for 4 seconds, and return early; (b) if no existing Trigger, snap the new Trigger to position `(80, 80)` (upper-left home); (c) in the config panel open handler, when the selected node is a Trigger, append its `GoalPrompt` to `_chatContext` so the assistant receives the trigger description as opening context; (d) add a `[Parameter] public EventCallback<bool> OnTriggerPresenceChanged { get; set; }` parameter and fire it with the current trigger-present boolean after every node add or remove (in `AddNodeToCanvas`, and in the existing node-removed handler) — this makes `WorkflowCanvas` the single source of truth for trigger presence, so the parent page never needs to inspect the diagram directly
- [X] T020 [US1] Update `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowToolbar.razor`: change it to receive `[Parameter] public bool IsTriggerMissing { get; set; }` from the parent `WorkflowBuilder.razor` page (do NOT compute this locally — the canvas owns the state via T019's `OnTriggerPresenceChanged`); when `IsTriggerMissing == true`, render an amber badge `<span class="bg-amber-900/50 border border-amber-600 text-amber-200 text-xs px-2 py-1 rounded">Add a starting trigger to run this workflow</span>` in the toolbar and disable the Run and Generate Code buttons with `disabled` + `opacity-50 cursor-not-allowed`; update `WorkflowBuilder.razor` to (i) declare `bool _isTriggerMissing = true` as a page-level field, (ii) bind `OnTriggerPresenceChanged="@(present => { _isTriggerMissing = !present; StateHasChanged(); })"` on the `<WorkflowCanvas>` tag, and (iii) pass `IsTriggerMissing="@_isTriggerMissing"` to both `<WorkflowToolbar>` and `<WorkflowRunOutputPanel>`
- [X] T020a [US1] Update `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowRunOutputPanel.razor` to display a one-sentence plain-language instruction when no Trigger node is present: add `[Parameter] public bool IsTriggerMissing { get; set; }`; render `<p class="text-amber-300 text-sm">Add a "Start / Trigger" node to your workflow before running.</p>` at the top of the panel body when `IsTriggerMissing == true`; hide this message otherwise; the parent wires this via the `IsTriggerMissing` binding established in T020; satisfies FR-09.4 Run Output panel requirement

**Checkpoint**: Quickstart.md Scenario 1 passes. User Story 1 is independently functional.

---

## Phase 4: User Story 2 — Directional Connections (Priority: P1)

**Goal**: Connection arrows are unambiguously directional at all zoom levels via enlarged
arrowheads and a mid-line accent. Dragging from an input port is rejected with an inline
hint. During execution, a travelling dot animates along active connections.

**Independent Test**: Connect two nodes; verify arrowhead is visible at 50 %–200 % zoom;
drag from an input port and verify rejection hint; run a workflow and observe travelling
dot animation. See quickstart.md Scenario 2 and 3 for full pass criteria.

### Tests — Write First (RED)

- [X] T021 [P] [US2] Write bUnit test `WorkflowDirectionalLinkTests` in `tests/DBAIAzure.Tests/WorkflowDirectionalLinkTests.cs`: construct a `WorkflowEdgeModel` and assert (a) `TargetMarker` is not null; (b) `TargetMarker.Width >= 20`; (c) `SourceMarker` is null or equals `LinkMarker.None`; (d) `IsAnimating` defaults to `false`
- [X] T022 [P] [US2] Add to `WorkflowDirectionalLinkTests.cs`: simulate a `PointerDown` event on a port with `Alignment == PortAlignment.Left` in a rendered canvas component — assert (a) the `ValidateLink` delegate returns `false`; (b) a hint element with text "Connections start from the right side" is rendered within 200 ms

### Implementation (GREEN)

- [X] T023 [P] [US2] In `WorkflowEdgeModel` constructor in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowDiagramModels.cs`: set `TargetMarker = LinkMarker.Arrow(20, 14)` and leave `SourceMarker = LinkMarker.None`; add `public bool IsAnimating { get; set; }` property with XML doc comment explaining it triggers the CSS execution-flow animation
- [X] T024 [P] [US2] Populate `src/DBAIAzure.Web/wwwroot/css/workflow-canvas-animations.css` with: (a) `.edge-flow-active animateMotion` SMIL declaration referenced by the `<circle>` overlay element that will be conditionally rendered on active edges (`dur="0.8s"`, `repeatCount="indefinite"`); (b) `.edge-mid-accent` style using `stroke-dasharray: 8 16` and a single `›` (U+203A) `<textPath>` chevron at `startOffset="50%"` with `rotate="auto"` — applied to all `.workflow-edge` path elements unconditionally as a passive directional cue
- [X] T025 [US2] Extend the `ValidateLink` delegate in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowCanvas.razor` to detect drag starts from `PortAlignment.Left` ports: in the `Diagram.Links.Added` guard, check `sourcePort.Alignment == PortAlignment.Left`; if true, cancel the pending link and set `_inputPortHintVisible = true` for 3 seconds (a Blazor state flag that renders `<div class="text-amber-300 text-sm">Connections start from the right side (output) of a node.</div>` in the canvas overlay); depends on T023
- [X] T026 [US2] Wire `WorkflowEdgeModel.IsAnimating` to execution state in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowCanvas.razor`: subscribe to the execution orchestrator's node-status-changed event; when a node transitions to `NodeStatus.Active`, set `IsAnimating = true` on all incoming `WorkflowEdgeModel` instances for that node; when the node transitions to `NodeStatus.Completed` or `NodeStatus.Failed`, set `IsAnimating = false`; call `StateHasChanged()` after each toggle to repaint the canvas

**Checkpoint**: Quickstart.md Scenario 2 and 3 pass. Directional connections are fully functional.

---

## Phase 5: User Story 3 — Node Deletion (Priority: P1)

**Goal**: A selected node (and all its edges) is removed by pressing Delete/Backspace or
by choosing "Delete node" from the right-click context menu. Deletion is instantly undoable.

**Independent Test**: Place three connected nodes, delete the middle node, verify edges are
removed and flanking nodes unchanged; press Ctrl+Z and verify full restoration.
See quickstart.md Scenario 4 for full pass criteria.

### Tests — Write First (RED)

- [X] T027 [P] [US3] Write bUnit test `NodeContextMenuTests` in `tests/DBAIAzure.Tests/NodeContextMenuTests.cs`: render a canvas with three connected nodes; simulate selecting the middle node and pressing the Delete key — assert (a) the middle node is no longer in `_diagram.Nodes`; (b) both edges that connected to it are no longer in `_diagram.Links`; (c) the flanking nodes remain in `_diagram.Nodes` at unchanged positions
- [X] T028 [P] [US3] Add to `NodeContextMenuTests.cs`: after the Delete in T027, simulate Ctrl+Z (undo) — assert (a) the middle node reappears in `_diagram.Nodes`; (b) its `PositionX`/`PositionY` match the pre-deletion values; (c) both edges are restored in `_diagram.Links`

### Implementation (GREEN)

- [X] T029 [P] [US3] Create `src/DBAIAzure.Web/Components/WorkflowBuilder/NodeContextMenu.razor`: an absolutely-positioned `<div>` rendered at `(CanvasX, CanvasY)` pixels from the canvas container; contains a single menu item `<button class="text-red-400 hover:bg-gray-700 ...">Delete node</button>` that invokes an `EventCallback<string> OnDeleteNode` parameter passing the target node ID; Escape key or outside-click sets `IsVisible = false` via parent state; keyboard-accessible (Tab/Enter/Escape as per WCAG 2.1 AA)
- [X] T030 [US3] Implement `UndoDeleteNodeCommand` as a `sealed record` nested inside `WorkflowCanvas.razor` code-behind (or in the same `.razor` file's `@code` block) with fields `WorkflowNodeModel Node`, `Point Position`, `WorkflowEdgeModel[] AttachedEdges`; implement `Execute(BlazorDiagram d)` (removes node + edges) and `Undo(BlazorDiagram d)` (re-adds node at `Position`, re-adds all edges); depends on T029
- [X] T031 [US3] Extend `HandleKeyDown` in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowCanvas.razor` to handle Delete and Backspace when a `WorkflowNodeModel` is selected and no text input field has focus: collect all `WorkflowEdgeModel` instances where `SourceNodeId == node.Id || TargetNodeId == node.Id`; record the set of neighbour node IDs from those edges; push an `UndoDeleteNodeCommand`; call `Execute` to remove node and edges; after removal, for each former neighbour still in `_diagram.Nodes`, check whether it now has zero remaining connections — if so, invoke the existing amber-badge validation helper (spec 003 FR-08.1) on that node to show an amber "!" badge; call `StateHasChanged()`; satisfies FR-11.6; depends on T030
- [X] T032 [US3] Wire `NodeContextMenu` into `WorkflowCanvas.razor`: add a `NodeContextMenuState _contextMenu` field; handle `@oncontextmenu` on each `WorkflowNodeRenderer` tile to set `_contextMenu.IsVisible = true`, `_contextMenu.TargetNodeId = node.Id`, and compute canvas-relative coordinates using `_canvasBounds` (developed in parallel via T033); in the `OnDeleteNode` callback, find the target `WorkflowNodeModel`, collect its edges and the set of neighbour node IDs, push `UndoDeleteNodeCommand`, call `Execute` to remove node and edges, then — identical to T031 — for each former neighbour still in `_diagram.Nodes` with zero remaining connections, invoke the amber-badge validation helper (spec 003 FR-08.1) to show an amber "!" badge; call `StateHasChanged()`; satisfies FR-11.6 for the right-click deletion path; **T032 and T033 are jointly atomic — develop together and merge together**
- [X] T033 [US3] Cache the canvas container's bounding rect in `WorkflowCanvas.razor` via a single `IJSRuntime.InvokeAsync<BoundingRect>("getBoundingClientRect", _canvasContainerRef)` call in `OnAfterRenderAsync` (first render only); store as a `_canvasBounds` field that T032 reads to convert `MouseEventArgs.ClientX/Y` to canvas-local coordinates for `NodeContextMenu` positioning; **T032 and T033 are jointly atomic — develop together and merge together**

**Checkpoint**: Quickstart.md Scenario 4 passes. Node deletion and undo are fully functional.

---

## Phase 6: User Story 4 — Trigger vs Smart Branch Disambiguation (Priority: P2)

**Goal**: A first-time user can correctly identify the "Start / Trigger" node (entry point)
and the Smart Branch / FunctionRoute node (mid-workflow router) using only palette labels
and tooltips — without consulting documentation. Palette search keeps the two concepts
disentangled.

**Independent Test**: Type "start" in palette search — only Trigger in results. Type "branch"
— only routing nodes in results. Hover tooltips contain the right language for each node.
See quickstart.md Scenario 5 for full pass criteria.

### Tests — Write First (RED)

- [X] T034 [P] [US4] Write bUnit test `WorkflowNodePaletteSearchTests` in `tests/DBAIAzure.Tests/WorkflowNodePaletteSearchTests.cs`: render `WorkflowNodePalette` with search input "start" — assert only items with `data-node-type == "Trigger"` are visible and no item with `data-node-type` in `{FunctionRoute, HumanApproval}` is visible; then change input to "branch" — assert Trigger is not visible and at least one `FunctionRoute` item is visible

### Implementation (GREEN)

- [X] T035 [P] [US4] In `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodePalette.razor`, add a keyword tag list alongside each node type entry (e.g. a `string[] SearchTags` per palette entry); Trigger tags: `["start", "trigger", "begin", "entry"]`; FunctionRoute / Smart Branch tags: `["branch", "decide", "route", "condition", "smart", "switch"]`; update the search filter predicate to match against `SearchTags` as well as the plain-language name — if the query matches only FunctionRoute tags, Trigger must not appear in results, and vice versa; depends on T016
- [X] T036 [P] [US4] Set canonical tooltip text for each node type in the palette entry metadata in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodePalette.razor`: Trigger tooltip — "Marks where your workflow begins. Every workflow has exactly one. Connect it to your first step."; FunctionRoute / Smart Branch tooltip — "Asks the AI to read the current data and choose which path to take next. Use this in the middle of a workflow to split paths based on content."; all other node type tooltips remain unchanged

**Checkpoint**: Quickstart.md Scenario 5 passes. User Story 4 is independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Execution layer integration, accessibility, and release readiness.

- [X] T037 [P] Update `CHANGELOG.md` with a new entry under the current date describing: Trigger node type (FR-09), directional connection arrowheads and mid-line accent (FR-10), execution flow animation (FR-10.5), node deletion via Delete key and right-click context menu (FR-11), undo-delete fidelity (FR-11.4), and Trigger vs Smart Branch palette disambiguation (US4)
- [X] T038 Update `src/DBAIAzure.Processes/Pipeline/WorkflowExecutionOrchestrator.cs` to recognise `WorkflowNodeType.Trigger` as the graph entry point: use the Trigger node (if present) as the starting node for topological sort; skip executing it as a step (it has no runnable logic); forward its `GoalPrompt` and `FunctionConfig.initialDataDescription` as initial context into the first downstream step's input
- [X] T039 [P] Update `src/DBAIAzure.Web/Services/WorkflowCodeGenerator.cs` to generate an entry-point region comment from the Trigger node's `GoalPrompt` (e.g. `// Triggered by: <GoalPrompt>`) and emit `initialDataDescription` as an XML doc comment on the generated entry method so code consumers understand the contract
- [X] T040 Verify keyboard accessibility of `NodeContextMenu.razor`: manually confirm Tab moves focus between menu items, Enter activates "Delete node", Escape closes the menu without deletion; confirm focus returns to the previously selected canvas node after the menu closes (WCAG 2.1 AA focus management)
- [X] T041 [P] Run all five quickstart.md validation scenarios in the browser against the running app (`dotnet run --project src/DBAIAzure.Web`) and record pass/fail against each criterion; mark the quickstart.md checklist items as verified; run `dotnet test tests/DBAIAzure.Tests/` and confirm all new tests pass

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately; T001 and T002 can run in parallel
- **Phase 2 (Foundational)**: Depends on Phase 1 — **BLOCKS all user story phases**
- **Phases 3–6 (User Stories)**: All depend on Phase 2 completion; stories are independent of each other and can proceed in parallel if staffed
- **Phase 7 (Polish)**: Depends on Phases 3–6 (or whichever stories are targeted for this sprint)

### User Story Dependencies

- **US1 (P1)**: Depends on Phase 2 only. No dependency on US2, US3, or US4.
- **US2 (P1)**: Depends on Phase 2 only. Independent of US1, US3, US4.
- **US3 (P1)**: Depends on Phase 2 only. Independent of US1, US2, US4.
- **US4 (P2)**: Depends on US1 (T016 — the palette Triggers category must exist for search filter T035 to operate). Otherwise independent.

### Within Each Phase

- Tests (T003/T004, T013–T015, T021–T022, T027–T028, T034) MUST be written and confirmed failing (RED) before their corresponding implementation tasks begin
- Within Phase 2: T008 and T009 are parallel; T010 depends on both; T011 depends on T010; T012 depends on T011
- Within Phase 3: T016, T017, T018 are parallel; T019 depends on T016; T020 and T020a are independent of each other but both depend on `_isTriggerMissing` existing (T020 creates it; T020a consumes it as a `[Parameter]` — implement T020 first)
- Within Phase 4: T023 and T024 are parallel; T025 depends on T023; T026 depends on T025
- Within Phase 5: T029 is parallel with tests; T030 depends on T029; T031 depends on T030; T032 and T033 are jointly atomic (develop together, no ordering between them); T032 depends on T031
- Within Phase 7: T037, T039, T041 are parallel; T038 is independent; T040 depends on T029 (NodeContextMenu must exist)

---

## Parallel Example: Phase 2 (Foundational)

```text
# Run in parallel immediately (different files, no inter-dependencies):
T003 — WorkflowNodeTypeTests.cs
T004 — WorkflowValidatorTests.cs

# After tests are RED, run in parallel:
T005 — WorkflowNodeType.cs
T006 — WorkflowNode.cs
T007 — WorkflowDefinition.cs
T008 — WorkflowValidationException.cs
T009 — IWorkflowValidator.cs

# Sequentially after T008 + T009:
T010 — WorkflowValidator.cs
T011 — WorkflowBuilderService.cs
T012 — WorkflowBuilder.razor
```

## Parallel Example: Phase 3 (US1)

```text
# Run in parallel immediately (different test files):
T013 — WorkflowCanvasTriggerTests.cs (palette category test)
T014 — WorkflowCanvasTriggerTests.cs (second-trigger block test)
T015 — WorkflowNodeRendererTriggerTests.cs

# After tests are RED, run in parallel:
T016 — WorkflowNodePalette.razor
T017 — WorkflowNodeRenderer.razor
T018 — WorkflowNodeConfigPanel.razor
T020 — WorkflowToolbar.razor (independent of T019; creates _isTriggerMissing)
T020a — WorkflowRunOutputPanel.razor (run after T020 so _isTriggerMissing param exists)

# After T016 is GREEN:
T019 — WorkflowCanvas.razor (drop guard + home position + chat context)
```

---

## Implementation Strategy

### MVP First (P1 Stories Only — US1, US2, US3)

1. Complete Phase 1 (2 tasks — minutes)
2. Complete Phase 2 (10 tasks — domain model + tests)
3. Complete Phase 3 (US1 — Trigger Node) → validate Scenario 1
4. Complete Phase 4 (US2 — Directional Links) → validate Scenarios 2–3
5. Complete Phase 5 (US3 — Node Deletion) → validate Scenario 4
6. **STOP and VALIDATE**: run `dotnet test` + browser quickstart Scenarios 1–4
7. Ship or demo — all P1 acceptance criteria met

### Incremental Delivery

1. Phase 1 + Phase 2 → Foundation ready
2. Phase 3 → Trigger node works → Demo/validate
3. Phase 4 → Directional links work → Demo/validate
4. Phase 5 → Node deletion works → Demo/validate
5. Phase 6 (US4) → Disambiguation polished → Demo/validate
6. Phase 7 → Polish, CHANGELOG, execution layer wired, accessibility verified → Ship

### Parallel Team Strategy (3 developers)

After Phase 2 completes:
- **Developer A**: US1 (Phase 3) — palette + renderer + canvas trigger logic
- **Developer B**: US2 (Phase 4) — diagram models + CSS animations + link validation
- **Developer C**: US3 (Phase 5) — NodeContextMenu + undo command + key handler
- All three merge; then one developer completes US4 (Phase 6) while others begin Phase 7

---

## Notes

- `[P]` tasks modify different files and have no dependency on incomplete tasks in the same phase — safe to parallelize
- `[Story]` label maps each task to its user story for traceability against spec.md acceptance scenarios
- Constitution Article V: every implementation task has a corresponding failing test written first
- Constitution Article VII: Z.Blazor.Diagrams native APIs (`LinkMarker.Arrow`, `ValidateLink`, `DiagramCanvas` key events) are used wherever available; custom code is limited to the three documented gaps (animation, context menu, mid-line accent) recorded in research.md
- Commit after each logical group (e.g. after T005–T007 pass tests); do not batch across stories
- Stop at each Checkpoint to run `dotnet test` before proceeding to the next phase
