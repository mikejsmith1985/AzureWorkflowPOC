# Tasks: Workflow Builder UX Master Review

**Input**: Design documents from `specs/005-workflow-ux-redesign/`

**Prerequisites**: plan.md ✓ | spec.md ✓ | research.md ✓ | data-model.md ✓ | contracts/ ✓ | quickstart.md ✓

**Constitution**: TDD Red→Green→Refactor; xUnit + bUnit; no wildcard process kills.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete sibling tasks)
- **[Story]**: Which user story this task belongs to (US1–US9)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the one new NuGet dependency, create all new interface/type files, and
register services in DI. No user-story work begins until this phase is complete.

- [X] T001 Add DiffPlex v1.7.2 NuGet package to `src/DBAIAzure.Core/DBAIAzure.Core.csproj`
- [X] T002 [P] Create `DiffLineType` enum, `DiffLine` record, and `DiffResult` record in `src/DBAIAzure.Core/Models/DiffModels.cs`
- [X] T003 [P] Create `IWorkflowThumbnailGenerator` interface stub in `src/DBAIAzure.Core/Interfaces/IWorkflowThumbnailGenerator.cs`
- [X] T004 [P] Create `IWorkflowCodeDiffService` interface stub in `src/DBAIAzure.Core/Interfaces/IWorkflowCodeDiffService.cs`
- [X] T005 [P] Create `WorkflowThumbnailGenerator` class stub in `src/DBAIAzure.Core/Services/WorkflowThumbnailGenerator.cs`
- [X] T006 [P] Create `WorkflowCodeDiffService` class stub in `src/DBAIAzure.Core/Services/WorkflowCodeDiffService.cs`
- [X] T007 [P] Create empty Blazor component stubs: `WorkflowEntryChoiceModal.razor`, `WorkflowUnsavedChangesModal.razor`, `WorkflowKeyboardShortcutsPanel.razor` in `src/DBAIAzure.Web/Components/WorkflowBuilder/`
- [X] T008 Register `IWorkflowThumbnailGenerator → WorkflowThumbnailGenerator` and `IWorkflowCodeDiffService → WorkflowCodeDiffService` as singletons in `src/DBAIAzure.Web/Program.cs`

**Checkpoint**: `dotnet build` passes. All new types compile. No user-story logic yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Implement the two new services that multiple user stories depend on.
These MUST be complete before US6, US7, and US8 can be implemented.

**⚠️ CRITICAL**: US6 (chat diff), US7 (post-run feedback), and US8 (gallery thumbnails)
all depend on services implemented here.

- [X] T009 [P] Write failing unit test `WorkflowThumbnailGeneratorTests` in `tests/DBAIAzure.Tests/WorkflowThumbnailGeneratorTests.cs` — test: 2-node SVG contains two `<rect>` elements; empty workflow returns null
- [X] T010 [P] Write failing unit test `WorkflowCodeDiffServiceTests` in `tests/DBAIAzure.Tests/WorkflowCodeDiffServiceTests.cs` — test: added line appears with `DiffLineType.Added`; identical inputs → `HasChanges = false`; 3 context lines appear around each hunk
- [X] T011 Implement `WorkflowThumbnailGenerator.GenerateSvg` in `src/DBAIAzure.Core/Services/WorkflowThumbnailGenerator.cs` — read `Nodes` positions, normalise to 200×100 viewBox, emit colour-coded `<rect>` per node and `<line>` per edge; catch all exceptions and return `null` (makes T009 pass)
- [X] T012 Implement `WorkflowCodeDiffService.ComputeDiff` in `src/DBAIAzure.Core/Services/WorkflowCodeDiffService.cs` using `DiffPlex.DiffBuilder.InlineDiffBuilder.Diff`; apply ±3 context-line windowing; return `DiffResult` (makes T010 pass)
- [X] T013 Update `WorkflowBuilderService.SaveAsync` in `src/DBAIAzure.Web/Services/WorkflowBuilderService.cs` — after persisting, call `IWorkflowThumbnailGenerator.GenerateSvg(workflow)` and if non-null re-save with `workflow with { ThumbnailSvg = svg }` (silent on null)

**Checkpoint**: `dotnet test` passes on T009 and T010. Thumbnail generation and diff logic are proven by tests.

---

## Phase 3: User Story 1 — First-Run Entry Choice & Welcome Overlay (Priority: P1) 🎯 MVP

**Goal**: A zero-workflow user is greeted with a clear two-option choice; an empty canvas
shows a welcome guide that disappears on first node placement.

**Independent Test** (quickstart.md Scenarios 1 & 2): Navigate to `/workflow-builder` with
zero saved workflows → entry screen shown → "Try the example" → Trigger present, Run enabled.

### TDD Test (write first — must fail before any implementation task below)

- [X] T021 [US1] Write bUnit test `WorkflowEntryChoiceModalTests` in `tests/DBAIAzure.Tests/WorkflowEntryChoiceModalTests.cs` — test: modal renders when `IsOpen = true`; "Start from scratch" button fires `OnScratchChosen`; "Try the example" fires `OnExampleChosen`; modal does NOT render when `IsOpen = false` (covers FR-01.3 negative case)

### Implementation for User Story 1

- [X] T014 [US1] Fix `BuildExampleWorkflow()` in `src/DBAIAzure.Web/Pages/WorkflowBuilder.razor` to include a fully configured `WorkflowNodeType.Trigger` node (emerald, "Start here", no amber badge) connected to the existing Summarise and Approve nodes; verify Run button pre-condition is met
- [X] T015 [P] [US1] Add `_savedWorkflowCount` field to `WorkflowBuilder.razor`; populate it in `OnInitializedAsync` via `BuilderService.ListByOwnerAsync(DemoOwnerId)` (count only); show entry choice modal when `Id` is null/empty AND `_savedWorkflowCount == 0`
- [X] T016 [US1] Implement `WorkflowEntryChoiceModal.razor` in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowEntryChoiceModal.razor` — centred overlay with "Start from scratch" and "Try the example" buttons; `IsOpen` parameter; `OnScratchChosen` and `OnExampleChosen` EventCallbacks
- [X] T017 [US1] Wire `WorkflowEntryChoiceModal` into `WorkflowBuilder.razor`: `OnScratchChosen` → open empty canvas; `OnExampleChosen` → call `BuildExampleWorkflow()` and set `_workflow`; `IsOpen` → `_savedWorkflowCount == 0 && Id is null` (when `Id` is non-null the modal is never shown, satisfying FR-01.3)
- [X] T018 [US1] Add `_hasEverHadNode` bool to `WorkflowCanvas.razor`; set to `true` on first node added (never reset); add welcome overlay markup in `WorkflowCanvas.razor` — visible when `_diagram.Nodes.Count == 0 && !_hasEverHadNode`; overlay disappears immediately (no animation) when first node is placed via the existing `OnNodeAdded` handler
- [X] T019 [US1] Using `_hasEverHadNode`, differentiate the two empty-canvas states in `WorkflowCanvas.razor`: when `!_hasEverHadNode`, show the full welcome illustration + instruction text; once `_hasEverHadNode` is true and `_diagram.Nodes.Count == 0`, show only the minimal "canvas is empty — drag a step to continue" text label (no illustration)
- [X] T020 [US1] Add pulsing CSS glow to the Triggers category header in `WorkflowNodePalette.razor` when `_diagram.Nodes.Count == 0` (pass `IsCanvasEmpty` parameter from `WorkflowCanvas` via `WorkflowBuilder`); remove glow as soon as any node is placed

**Checkpoint**: First-time user flow verified end-to-end per quickstart.md Scenarios 1 & 2.

---

## Phase 4: User Story 2 — Node Configuration Discoverability (Priority: P1)

**Goal**: Every unconfigured node shows a visible "Set up" label; single-click reveals the
double-click instruction; config panel opens with focus; Goal input syncs live to node label;
panel button says "Done."

**Independent Test** (quickstart.md Scenario 3): Place an unconfigured node → see "Set up →"
label → single-click → see tooltip → double-click → panel opens focused → type goal → node
label updates live → click Done → badge gone.

### TDD Test (write first — must fail before any implementation task below)

- [X] T029 [US2] Write bUnit test `WorkflowNodeRendererAffordanceTests` in `tests/DBAIAzure.Tests/WorkflowNodeRendererAffordanceTests.cs` — test: "Set up →" label visible when `IsConfigured = false`; label absent when `IsConfigured = true`

### Implementation for User Story 2

- [X] T022 [P] [US2] Add "Set up →" text label to `WorkflowNodeRenderer.razor` — visible (not a tooltip) only when `!Node.WorkflowNode.IsConfigured`; positioned beneath the amber "!" badge; hidden when node is configured
- [X] T023 [US2] Wire Z.Blazor.Diagrams `_diagram.SelectionChanged` event in `WorkflowCanvas.razor`; on selection of a single `WorkflowNodeModel`, raise `OnNodeSingleClicked` EventCallback carrying the node's Id; clear the selection-tooltip state when selection is cleared
- [X] T024 [US2] Add `_singleClickTooltipNodeId string?` field to `WorkflowCanvas.razor` (set on `OnNodeSingleClicked`) and `_tooltipCooldownNodeIds HashSet<string>` parameter passed down to each `WorkflowNodeRenderer`; in `WorkflowNodeRenderer.razor`, show "Double-click to configure this step" callout for 2 seconds when this node's Id matches `_singleClickTooltipNodeId`; after display, add node Id to `_tooltipCooldownNodeIds` (suppresses reappearance for 60 seconds)
- [X] T025 [US2] Add `@ref` focus call in `WorkflowNodeConfigPanel.razor.OnAfterRenderAsync(firstRender: true)` so the first visible form field (`<textarea>` for Goal, `<input>` for others) receives focus when the panel opens
- [X] T026 [US2] Add debounced live label sync in `WorkflowNodeConfigPanel.razor`: on `@oninput` of the Goal field for `AgenticReason` nodes, raise a new `OnGoalPreview(string goal)` EventCallback; debounce to 200 ms; `WorkflowCanvas.UpdateNodeFromConfig` called with a partial update to sync `Label` without closing the panel
- [X] T027 [US2] Rename the "Save" button to "Done" in `WorkflowNodeConfigPanel.razor` (label change only; behaviour is unchanged — panel applies changes on click and raises `NodeUpdated`)
- [X] T028 [US2] Add `OnConfigCommitted` EventCallback to `WorkflowNodeConfigPanel.razor` that fires immediately after `NodeUpdated`; wired in `WorkflowBuilder.razor` to set `_hasUnsavedChanges = true`

**Checkpoint**: Verified per quickstart.md Scenario 3.

---

## Phase 5: User Story 3 — Run Button Disabled Reason (Priority: P1)

**Goal**: The Run button always shows a plain-language explanation when it is disabled;
the explanation disappears and the button animates to green when it becomes enabled.

**Independent Test** (quickstart.md Scenario 4): Two unconfigured nodes, no Trigger → "Needs a
trigger to start" visible. Add Trigger, no configuration → "Set up all steps first." Configure
all → reason text gone, button fades to green.

- [X] T030 [US3] Add always-visible disabled-reason text block immediately adjacent to the Run/Stop button group in `WorkflowToolbar.razor`; render "Needs a trigger to start" when `IsTriggerMissing`; render "Set up all steps first" when `!CanRun && !IsTriggerMissing`; hide the block entirely when `CanRun && !IsTriggerMissing`
- [X] T031 [P] [US3] Add `.run-btn-ready` CSS rule to `src/DBAIAzure.Web/wwwroot/css/workflow-canvas-animations.css`: `transition: background-color 300ms ease-in-out, color 300ms ease-in-out;` applied to the Run button element so it smoothly animates from grey to green when enabled
- [X] T032 [US3] Apply `.run-btn-ready` CSS class to the Run button element in `WorkflowToolbar.razor` so the 300 ms transition fires when `CanRun && !IsTriggerMissing` becomes true

**Checkpoint**: Verified per quickstart.md Scenario 4.

---

## Phase 6: User Story 4 — Inline Workflow Name Editing (Priority: P1)

**Goal**: Clicking the workflow name in the toolbar opens an inline input; Enter/blur commits;
blank name reverts; page title updates; new untitled workflows show an amber-highlighted editable
name on load.

**Independent Test** (quickstart.md Scenario 5): Open a new workflow → amber name visible →
click it → input opens, name selected → type new name → press Enter → label shows new name,
tab title updated → clear name → reverts with tooltip.

### TDD Test (write first — must fail before any implementation task below)

- [X] T038 [US4] Write bUnit test `WorkflowToolbarNameEditTests` in `tests/DBAIAzure.Tests/WorkflowToolbarNameEditTests.cs` — test: name span is clickable; clicking opens input with name selected; Enter fires `OnNameChanged`; blank commit reverts to previous value

### Implementation for User Story 4

- [X] T033 [US4] Add `_isEditingName`, `_editingNameValue`, `_showBlankNameTooltip` private fields to `WorkflowToolbar.razor`
- [X] T034 [US4] Replace the static `<span>` workflow name display in `WorkflowToolbar.razor` with a conditional: when `_isEditingName` is false, render a clickable `<span>` with `@onclick="StartNameEdit"` and amber styling when name is "Untitled Workflow" or empty; when `_isEditingName` is true, render `<input>` with `@bind`, `@onkeydown`, and `@onblur` handlers
- [X] T035 [US4] Implement `StartNameEdit`, `CommitNameEdit`, and `CancelNameEdit` methods in `WorkflowToolbar.razor`; `CommitNameEdit` validates for non-empty and raises `OnNameChanged` EventCallback; blank input reverts to previous value and shows 1-second `_showBlankNameTooltip`
- [X] T036 [US4] Add `OnNameChanged EventCallback<string>` parameter to `WorkflowToolbar.razor`; in `WorkflowBuilder.razor` handle the callback by updating `_workflow = _workflow with { Name = newName }` and setting `_hasUnsavedChanges = true`
- [X] T037 [US4] Bind `<PageTitle>` in `WorkflowBuilder.razor` to `@(_workflow?.Name ?? "Workflow Builder") — DBAIAzure` so the browser tab title updates reactively when the name changes

**Checkpoint**: Verified per quickstart.md Scenario 5.

---

## Phase 7: User Story 5 — Unsaved Changes Navigation Guard (Priority: P1)

**Goal**: Any committed change sets a dirty flag; navigating away when dirty shows a
confirmation; "Stay" cancels navigation; "Leave" proceeds; clean state has no confirmation.

**Independent Test** (quickstart.md Scenario 6): Add a node (do not save) → navigate to
gallery → confirmation appears → click Stay → still on builder → repeat, click Leave → gallery
loads → make no changes → navigate → no confirmation.

### TDD Test (write first — must fail before any implementation task below)

- [X] T039 [US5] Write bUnit test `WorkflowUnsavedChangesModalTests` in `tests/DBAIAzure.Tests/WorkflowUnsavedChangesModalTests.cs` — test: modal renders when `IsOpen = true`; "Stay and save" fires `OnStayRequested`; "Leave without saving" fires `OnLeaveRequested`

### Implementation for User Story 5

- [X] T040 [US5] Add `_hasUnsavedChanges`, `_pendingNavigationUri`, `_isUnsavedChangesModalOpen` fields to `WorkflowBuilder.razor`; also add `ElementReference _saveButtonRef` (bound to the Save button via `@ref` in `WorkflowToolbar.razor`) for use in T045
- [X] T041 [US5] Set `_hasUnsavedChanges = true` in `WorkflowBuilder.razor` on: `OnWorkflowChanged` (canvas topology change), `OnNameChanged` (workflow name committed via toolbar), and `OnConfigCommitted` from T028 (Done clicked in config panel); do NOT set it on config panel keystrokes, on `OnAutoSaved`, or on `OnSaveAsync` completion — those clear the flag (see T042)
- [X] T042 [US5] Clear `_hasUnsavedChanges = false` in `WorkflowBuilder.razor` inside `OnSaveAsync` (after successful manual save) and inside `OnAutoSaved` (after auto-save fires successfully); these are the ONLY two events that clear the dirty flag
- [X] T043 [US5] Replace the stub `OnLocationChanging` in `WorkflowBuilder.razor` with real logic: if `_hasUnsavedChanges` is true, call `context.PreventNavigation()`, store `context.TargetLocation` in `_pendingNavigationUri`, set `_isUnsavedChangesModalOpen = true`, call `StateHasChanged()`; if false, return without blocking
- [X] T044 [US5] Implement `WorkflowUnsavedChangesModal.razor` in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowUnsavedChangesModal.razor` — modal overlay with "You have unsaved changes." message; "Stay and save" button fires `OnStayRequested`; "Leave without saving" button fires `OnLeaveRequested`; `IsOpen` parameter controls visibility
- [X] T045 [US5] Wire `WorkflowUnsavedChangesModal` into `WorkflowBuilder.razor`: `OnStayRequested` → close modal (`_isUnsavedChangesModalOpen = false`) then call `await _saveButtonRef.FocusAsync()` to place keyboard focus on the Save button per FR-06.2; `OnLeaveRequested` → set `_hasUnsavedChanges = false`, close modal, then call `Nav.NavigateTo(_pendingNavigationUri!)`; add `@ref="_saveButtonRef"` to the Save button element in `WorkflowToolbar.razor`

**Checkpoint**: Verified per quickstart.md Scenario 6. No workflows will be silently lost once this phase is complete — the navigation guard is active.

---

## Phase 8: User Story 6 — Chat Panel Change Indicator (Priority: P2)

**Goal**: After code is generated, any canvas change shows an orange dot on the Chat button;
opening chat shows the assistant's "update?" prompt; "Update code" triggers regeneration with
a compact diff view and "Show full code" toggle.

**Independent Test** (quickstart.md Scenario 7): Generate code → close chat → add node →
orange dot appears within 500 ms → open chat → dot gone, "update?" message visible → click
"Update code" → compact diff shown with + / - / context lines → "Show full code" expands.

- [X] T047 [US6] Add `_hasCanvasChangedSinceCodeGen` bool to `WorkflowBuilder.razor`; set to `true` in `OnWorkflowChanged` when `_workflow?.GeneratedCode is not null`; set to `false` when `OnChatToggleClicked` opens the chat panel
- [X] T048 [P] [US6] Add `HasCanvasChangedSinceCodeGen bool` parameter to `WorkflowToolbar.razor`; render a small orange dot (`<span>`) overlaid on the Chat button when `HasCanvasChangedSinceCodeGen` is true; hide dot when false
- [X] T049 [US6] Pass `HasCanvasChangedSinceCodeGen="@_hasCanvasChangedSinceCodeGen"` from `WorkflowBuilder.razor` to `WorkflowToolbar`
- [X] T050 [US6] Add `_previousGeneratedCode string?` field to `WorkflowChatPanel.razor`; when code is generated, snapshot the outgoing code into `_previousGeneratedCode` before overwriting with new code
- [X] T051 [US6] In `WorkflowChatPanel.razor`, when `_showWorkflowChangedBanner` is true (already set by existing `NotifyWorkflowChanged()`), add an inline "Update code" `<button>` inside the banner that calls a new `RegenerateWithDiffAsync` method
- [X] T052 [US6] Implement `RegenerateWithDiffAsync` in `WorkflowChatPanel.razor`: call the existing code generation flow; when new code arrives, call `IWorkflowCodeDiffService.ComputeDiff(_previousGeneratedCode, newCode)`; if `HasChanges` is true, render the `DiffResult` as a diff block; otherwise render the full new code normally
- [X] T053 [US6] Render diff block in `WorkflowChatPanel.razor`: iterate `DiffResult.Lines`; apply CSS class `.diff-add` (green) for `Added`, `.diff-remove` (red) for `Removed`, `.diff-context` (grey) for `Unchanged`; prefix each line with `+`, `-`, or ` ` respectively; render a "Show full code ↓" link below the block
- [X] T054 [P] [US6] Add `.diff-add`, `.diff-remove`, `.diff-context` CSS rules to `src/DBAIAzure.Web/wwwroot/css/workflow-canvas-animations.css`
- [X] T055 [US6] Add `_showFullCodeForDiff bool` toggle to `WorkflowChatPanel.razor`; "Show full code ↓" link toggles between compact diff and full syntax-highlighted code block; link label changes to "Show diff ↑" when full code is visible

**Checkpoint**: Verified per quickstart.md Scenario 7. `IWorkflowCodeDiffService` (T012) must be complete before T052.

---

## Phase 9: User Story 7 — Post-Run Feedback Pre-Population (Priority: P2)

**Goal**: "Did this do what you expected?" button opens chat with a pre-populated,
ready-to-submit message that names the node, its status, goal excerpt, and output excerpt.

**Independent Test** (quickstart.md Scenario 8): Run a workflow → click feedback button on
any node badge → chat opens with pre-populated template text → submit unchanged → assistant
gives concrete improvement suggestion.

- [X] T056 [US7] Add `PrePopulateFeedback(NodeExecutionState nodeState, WorkflowNode node)` public method to `WorkflowChatPanel.razor`; builds the template message: "The '[node.Label]' step [succeeded/failed]. Its goal was: [goal excerpt ≤80 chars]. It produced: [output excerpt ≤80 chars]. Did this do what you expected? If not, describe what you wanted instead."; stores in `_feedbackPrePopulatedMessage` field
- [X] T057 [US7] Update `OnNodeFeedbackRequested` in `WorkflowBuilder.razor` to: (1) retrieve the `WorkflowNode` matching the `NodeExecutionState.NodeId` from `_workflow.Nodes`; (2) call `_chatPanel?.PrePopulateFeedback(nodeState, node)`; (3) open the chat panel (`_isChatOpen = true`)
- [X] T058 [US7] In `WorkflowChatPanel.razor`, on open (when `IsOpen` transitions from false to true), if `_feedbackPrePopulatedMessage` is not null, set the message input value to `_feedbackPrePopulatedMessage` and clear `_feedbackPrePopulatedMessage`; the text is editable before send

**Checkpoint**: Verified per quickstart.md Scenario 8.

---

## Phase 10: User Story 8 — Gallery Improvements (Priority: P2)

**Goal**: Gallery cards show SVG thumbnails; an always-visible search filters by name;
zero-result state is clear; node-type summary replaces raw step count.

**Independent Test** (quickstart.md Scenario 9): Save a workflow → gallery shows coloured SVG
thumbnail (not "No preview") → search input always visible → type partial name → cards filter
→ zero-match state shown → card footer shows type summary.

*Prerequisite*: T013 (thumbnail save integration) and T011 (generator implementation) must be complete.

- [X] T059 [US8] Add `_searchText string` field and `FilteredWorkflows` computed property to `WorkflowGallery.razor`; `FilteredWorkflows` returns `_workflows` filtered by case-insensitive name containment when `_searchText` is non-empty, else returns all
- [X] T060 [US8] Add always-visible search `<input>` above the workflow grid in `WorkflowGallery.razor` — rendered whenever `_workflows.Count > 0`; `@oninput` updates `_searchText` with 100 ms debounce (reuse the debounce pattern from `WorkflowNodePalette.razor`); `aria-label="Search your workflows"`
- [X] T061 [US8] Replace `_workflows` loop in `WorkflowGallery.razor` with `FilteredWorkflows` loop; add zero-result state: when `_searchText` is non-empty and `FilteredWorkflows` is empty, render "No workflows match '@_searchText'" with a "Clear search" button that sets `_searchText = string.Empty`
- [X] T062 [P] [US8] Update `WorkflowGalleryCard.razor` footer to show a node-type summary string in place of "X step(s)": compute counts of each `WorkflowNodeType` in `Workflow.Nodes` and format as "1 trigger, 2 AI steps, 1 approval" (omitting types with zero count); fall back to "N steps" when `Workflow.Nodes` is empty

**Checkpoint**: Verified per quickstart.md Scenario 9.

---

## Phase 11: User Story 9 — Keyboard Shortcuts Panel (Priority: P3)

**Goal**: A "?" button in the toolbar reveals a floating panel listing all keyboard shortcuts;
Escape and outside-click close it.

**Independent Test** (quickstart.md Scenario 10): Click "?" → shortcuts panel shows Ctrl+Z,
Ctrl+Y, Delete/Backspace, Save entries → press Escape → panel closes.

- [X] T063 [P] [US9] Implement `WorkflowKeyboardShortcutsPanel.razor` in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowKeyboardShortcutsPanel.razor` — floating `<div>` with `IsOpen` parameter; lists: "Undo (Ctrl+Z)", "Redo (Ctrl+Y)", "Delete selected (Delete / Backspace)", "Save (Ctrl+S)"; each entry as `<kbd>` shortcut + description; `OnCloseRequested EventCallback`
- [X] T064 [US9] Add `IsShortcutsPanelOpen bool` and `OnShortcutsToggleClicked EventCallback` parameters to `WorkflowToolbar.razor`; add a "?" icon button at the far-right end of the toolbar (after Run/Stop) that invokes `OnShortcutsToggleClicked`; render `<WorkflowKeyboardShortcutsPanel>` inline within the toolbar
- [X] T065 [US9] Add `_isShortcutsPanelOpen bool` to `WorkflowBuilder.razor`; wire `OnShortcutsToggleClicked` to toggle `_isShortcutsPanelOpen`; pass `_isShortcutsPanelOpen` to toolbar; wire panel `OnCloseRequested` to set false
- [X] T066 [US9] Add Escape key dismiss to `WorkflowKeyboardShortcutsPanel.razor` via a `@onkeydown` handler on the panel root element; add outside-click dismiss via an invisible backdrop `<div>` (same pattern as the canvas context menu in `WorkflowCanvas.razor`)

**Checkpoint**: Verified per quickstart.md Scenario 10.

---

## Phase 12: Polish & Cross-Cutting Concerns

- [X] T067 Update `CHANGELOG.md` with a new entry documenting all 10 UX improvements (entry choice, welcome overlay, node affordance, run reason, inline rename, nav guard, chat dot, feedback pre-population, gallery thumbnails+search, keyboard shortcuts panel)
- [ ] T068 Run all 10 quickstart.md validation scenarios in the browser and confirm each passes; fix any regressions before marking complete

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 completion — **BLOCKS** US6 (T052), US8 (T059–T062)
- **Phase 3 (US1)**: Depends on Phase 1 only (no service dependency)
- **Phase 4 (US2)**: Depends on Phase 1 only
- **Phase 5 (US3)**: Depends on Phase 1 only
- **Phase 6 (US4)**: Depends on Phase 1 only
- **Phase 7 (US5)**: Depends on T028 (US2 OnConfigCommitted event) — implement after Phase 4
- **Phase 8 (US6)**: Depends on Phase 2 (T012 WorkflowCodeDiffService must be complete before T052)
- **Phase 9 (US7)**: Depends on Phase 4 (T028 OnConfigCommitted provides node data shape)
- **Phase 10 (US8)**: Depends on Phase 2 (T011, T013 for thumbnail generation)
- **Phase 11 (US9)**: No prerequisite phases
- **Phase 12 (Polish)**: All preceding phases complete

### User Story Dependencies

| Story | Phase | Depends on |
|-------|-------|-----------|
| US1 (entry + welcome) | 3 | Phase 1 only |
| US2 (config discovery) | 4 | Phase 1 only |
| US3 (run reason) | 5 | Phase 1 only |
| US4 (inline rename) | 6 | Phase 1 only |
| US5 (nav guard) | 7 | US2 complete (T028 OnConfigCommitted) |
| US6 (chat dot + diff) | 8 | Phase 2 complete (T012) |
| US7 (feedback pre-pop) | 9 | US2 partial (T028 node data shape) |
| US8 (gallery) | 10 | Phase 2 complete (T011, T013) |
| US9 (shortcuts) | 11 | Phase 1 only |

### Within Each Phase

TDD order: write failing test → implement → confirm test passes.
Config panel changes before state management (T027 before T041).
Service implementations before consumers (T011 before T052, T013 before T059).

---

## Parallel Opportunities

### Phase 1 (all can run in parallel after T001)
```
T002 + T003 + T004 + T005 + T006 + T007   ← all different files
```

### Phase 2 (tests parallel; implementations sequential after tests)
```
T009 + T010   ← parallel (different test files)
↓
T011 + T012   ← parallel after respective tests
↓
T013          ← after T011
```

### Phases 3–6 (all can start simultaneously after Phase 1)
```
Phase 3 (US1)  ╗
Phase 4 (US2)  ╠═ all independent; different component files
Phase 5 (US3)  ║
Phase 6 (US4)  ╝
```

### Within Phase 3 (US1)
```
T014 + T015   ← parallel (different concerns in WorkflowBuilder.razor)
T018 + T019   ← parallel (different logic branches in WorkflowCanvas.razor)
T021          ← [P] test, can run alongside implementation
```

### Within Phase 8 (US6)
```
T048 + T054   ← parallel (toolbar parameter vs CSS file)
```

---

## Implementation Strategy

### MVP First (US1–US5 only, all P1)

1. Complete **Phase 1** (Setup) — 30 min
2. Complete **Phase 2** (Foundational) — required for later stories but services can be stubs for P1 stories
3. Complete **Phase 3** (US1) → validate Scenario 1 & 2
4. Complete **Phase 4** (US2) → validate Scenario 3
5. Complete **Phase 5** (US3) → validate Scenario 4
6. Complete **Phase 6** (US4) → validate Scenario 5
7. Complete **Phase 7** (US5) → validate Scenario 6
8. **STOP and DEMO**: All P1 UX improvements are live

### Full Delivery (all P1 + P2 + P3)

Continue from MVP:

9. Complete **Phase 2** fully (if not already done)
10. Complete **Phase 8** (US6) → validate Scenario 7
11. Complete **Phase 9** (US7) → validate Scenario 8
12. Complete **Phase 10** (US8) → validate Scenario 9
13. Complete **Phase 11** (US9) → validate Scenario 10
14. Complete **Phase 12** (Polish) → CHANGELOG + full quickstart run

---

## Notes

- `[P]` tasks = different files, no dependencies on incomplete siblings in same phase
- `[Story]` label maps each task to its user story for traceability
- TDD: write failing test first, then implement until test passes
- Commit after each checkpoint (or logical group within a phase)
- `dotnet test` must pass before marking any phase complete
- The context menu backdrop pattern in `WorkflowCanvas.razor` is the reference for outside-click dismiss (T066)
- The `WorkflowNodePalette.razor` debounce pattern is the reference for gallery search debounce (T060)
