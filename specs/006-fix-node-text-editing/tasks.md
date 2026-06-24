# Tasks: Fix Node Text Editing in Workflow Builder

**Input**: Design documents from `specs/006-fix-node-text-editing/`

**Prerequisites**: plan.md ✓ | spec.md ✓ | research.md ✓ | data-model.md ✓ | contracts/ ✓ | quickstart.md ✓

**Tests**: Included — Article V of the project constitution requires TDD (Red → Green → Refactor).
Write each test task and confirm it fails before implementing the corresponding production code.

**Organization**: Tasks are grouped by user story. US1 (core rename) is the 🎯 MVP — US2 and
US3 extend it with minimal additional implementation. All user story phases depend on Phase 2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no blocking dependencies on incomplete tasks)
- **[Story]**: Maps to user stories US1–US3 from spec.md
- Exact file paths are included in every task description

---

## Phase 1: Setup

**Purpose**: Establish a clean green baseline before any code changes.

- [X] T001 On branch `fix/node-text-editing`, run `dotnet build` from the repo root and confirm zero errors; this is the green baseline all subsequent tasks must preserve after every change

**Checkpoint**: `dotnet build` exits 0 before proceeding to Phase 2.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the new data structure and undo action that every user story depends on.
Complete Phase 2 before beginning any user story phase.

**⚠️ CRITICAL**: Write the test task (T002) first and confirm it is RED before implementing T003–T004.

### Test — Write First, Confirm Failure Before Implementing

- [X] T002 [P] Write bUnit/unit test class `RenameLabelActionTests` in `tests/DBAIAzure.Tests/WorkflowNodeLabelEditTests.cs` (create new file): write three test methods — (a) `Do_AppliesNewLabel`: create a `WorkflowNodeModel` with `WorkflowNode.Label == "Original"`, create a `RenameLabelAction` with `previousLabel = "Original"` and `newLabel = "Updated"`, call `Do()`, assert `nodeModel.WorkflowNode.Label == "Updated"`; (b) `Undo_RestoresPreviousLabel`: after `Do()`, call `Undo()`, assert `nodeModel.WorkflowNode.Label == "Original"`; (c) `OnLabelCommitted_NoOp_WhenLabelsAreEqual`: call `OnLabelCommitted(new LabelCommitArgs(nodeId, "Same", "Same"))` on a `WorkflowCanvas` and assert the undo stack count did not increase; confirm all three fail (RED)

### Implementation — Run After Tests Are Red

- [X] T003 [P] Add `LabelCommitArgs` to `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowDiagramModels.cs`: insert `public readonly record struct LabelCommitArgs(string NodeId, string PreviousLabel, string NewLabel);` with an XML doc comment reading "Payload raised by WorkflowNodeRenderer when the user commits an inline label edit; carries before/after label values so the canvas can update the node model and record an undoable rename action"
- [X] T004 Add `LabelCommitted` event to `WorkflowNodeModel` and wire `RenameLabelAction`, `ApplyLabelChange`, and `OnLabelCommitted` in `WorkflowCanvas.razor`: (a) in `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowDiagramModels.cs`, add to the `WorkflowNodeModel` class: `/// <summary>Raised by WorkflowNodeRenderer when the user commits an inline label edit. previousLabel is the value when editing began; newLabel is the committed text (may be empty string).</summary> public event Action<string, string>? LabelCommitted; public void RaiseLabelCommitted(string previousLabel, string newLabel) => LabelCommitted?.Invoke(previousLabel, newLabel);` (b) in `WorkflowCanvas.razor`, add `private sealed class RenameLabelAction : ICanvasAction` inner class with fields `WorkflowCanvas _canvas`, `string _nodeId`, `string _previousLabel`, `string _newLabel`; `Do()` calls `_canvas.ApplyLabelChange(_nodeId, _newLabel)`; `Undo()` calls `_canvas.ApplyLabelChange(_nodeId, _previousLabel)`; (c) add `public void ApplyLabelChange(string nodeId, string label)` — finds the matching `WorkflowNodeModel` in `_diagram.Nodes.OfType<WorkflowNodeModel>()`, sets `nodeModel.WorkflowNode = nodeModel.WorkflowNode with { Label = label }`, calls `_diagram.Refresh()` and `NotifyWorkflowChanged()`; (d) add `private void OnLabelCommitted(LabelCommitArgs args)` — if `args.PreviousLabel == args.NewLabel` return early; otherwise `var action = new RenameLabelAction(this, args.NodeId, args.PreviousLabel, args.NewLabel); action.Do(); RecordAction(action);`; (e) locate the existing `nodeModel.DoubleClicked +=` and `nodeModel.ContextMenuRequested +=` subscription lines in the canvas and follow the same pattern to add in every node-addition path (initial load from `WorkflowDefinition` and palette additions): `nodeModel.LabelCommitted += (prev, next) => OnLabelCommitted(new LabelCommitArgs(nodeModel.WorkflowNode.Id, prev, next));`; depends on T003

**Checkpoint**: `dotnet test --filter "FullyQualifiedName~RenameLabelAction"` passes (GREEN). Phase 3+ can now begin.

---

## Phase 3: User Story 1 — User Renames a Node (Priority: P1) 🎯 MVP

**Goal**: A user can double-click a node's label on the canvas, type a new name, press Enter
(or click away), and see the new name persist. Simultaneously, the config panel's Goal and
port-label inputs stop resetting mid-keystroke.

**Independent Test**: Place any node. Double-click its label text. Clear it. Type "Custom Name."
Press Enter. Click elsewhere on the canvas. The node must show "Custom Name" — not the
type-default. Reopen the label for editing; the input must show "Custom Name."
See quickstart.md Scenarios 1–3 for full pass criteria.

### Tests — Write First (RED)

- [X] T005 [P] [US1] Add test class `WorkflowNodeConfigPanelResetGuardTests` to a new file `tests/DBAIAzure.Tests/WorkflowNodeConfigPanelResetGuardTests.cs`: render `WorkflowNodeConfigPanel` with `Node` having `Id = "node1"`, `GoalPrompt = "original goal"`, `IsOpen = true`; simulate `@oninput` on the Goal textarea to set the value to `"typed text"`; then trigger a parameter update that re-passes the SAME `Node` object (simulating a parent re-render); assert the component's internal `_goalPrompt` field (or the rendered textarea value) still equals `"typed text"` — not `"original goal"`; confirm RED
- [X] T006 [P] [US1] Add test class `WorkflowNodeLabelInlineEditTests` to `tests/DBAIAzure.Tests/WorkflowNodeLabelEditTests.cs`: (a) `DoubleClick_ActivatesLabelInput`: render `WorkflowNodeRenderer` with `Node.WorkflowNode.Label == "AI Agent"`; simulate a `dblclick` on the label span element; assert an `<input type="text">` is now present and its value equals `"AI Agent"` — no `<input>` should exist before the double-click; (b) `CommitLabel_RaisesNodeLabelCommittedEvent`: after activating edit mode, subscribe a handler to `Node.LabelCommitted`; change the input value to `"Custom Name"` via `@oninput`; trigger blur on the input; assert the `Node.LabelCommitted` event fired with `previousLabel == "AI Agent"` and `newLabel == "Custom Name"`; (c) `EscapeKey_CancelsEdit`: activate edit, change value to `"Discard me"`, press Escape key; assert `_isEditingLabel` is false and `Node.LabelCommitted` was NOT raised; (d) `DoubleClickOnLabel_DoesNotRaiseNodeDoubleClicked`: subscribe a handler to `Node.DoubleClicked`; simulate `dblclick` on the label span element; assert `Node.DoubleClicked` was NOT raised — the `@ondblclick:stopPropagation` guard on the label container must prevent the node-level handler from firing; confirm all RED
- [X] T007 [P] [US1] Create `tests/DBAIAzure.E2ETests/WorkflowNodeLabelEditTests.cs` with Playwright test class `WorkflowNodeLabelEditTests` (inherit `PageTest`): implement E2E test stubs for all five scenarios — Scenario 1 (`RenameNode_DoubleClickAndType_PersistsLabel`), Scenario 2 (`EscapeKey_CancelsLabelEdit`), Scenario 3 (`ConfigPanelGoalInput_DoesNotResetMidKeystroke`), Scenario 4 (`LabelUndo_CtrlZ_RestoresPreviousLabel`: rename node to "Step A", rename to "Step B", press Ctrl+Z, assert node header shows "Step A"; press Ctrl+Z again, assert "Triage Incoming Ticket"), and Scenario 5 (`EmptyLabel_ShowsPlaceholder`: double-click label, clear text, press Enter, assert node header is non-empty; double-click again and assert the input field is empty — the placeholder must not be pre-filled); run once via `.\scripts\run-e2e.ps1` to confirm all five RED before implementation

### Implementation (GREEN)

- [X] T008 [P] [US1] Fix `WorkflowNodeConfigPanel.razor` — add reset guard: insert `private string? _lastInitialisedNodeId;` field in the `@code` block; in `OnParametersSet()`, wrap the block that assigns `_goalPrompt`, `_inputLabel`, `_outputLabel`, `_showRequiredBanner`, and `_initialDataDescription` inside `if (Node?.Id != _lastInitialisedNodeId)` and add `_lastInitialisedNodeId = Node?.Id;` as the last statement inside that block; in `OnCloseAsync()`, add `_lastInitialisedNodeId = null;` as the first line; no other changes to the file; confirm T005 is GREEN
- [X] T009 [US1] Update `WorkflowNodeRenderer.razor` — add inline label editing: (a) Add fields to the `@code` block: `private bool _isEditingLabel; private string _labelBuffer = string.Empty; private string _previousLabelAtEditStart = string.Empty; private ElementReference _labelInputRef;` — no `[Parameter] EventCallback` is added; the renderer signals the canvas via `Node.RaiseLabelCommitted()`, following the same pattern as the existing `Node.RaiseDoubleClicked()` (b) In the `<div class="flex flex-col min-w-0">` label container, replace `<span class="text-white text-sm font-semibold truncate max-w-[110px]">@Node.WorkflowNode.Label</span>` with a conditional block wrapped in `<div @ondblclick:stopPropagation="true">`: when `_isEditingLabel`, render `<input type="text" @ref="_labelInputRef" value="@_labelBuffer" @oninput="OnLabelInput" @onkeydown="OnLabelKeyDown" @onblur="CommitLabel" @onclick:stopPropagation="true" @ondblclick:stopPropagation="true" class="text-white text-sm font-semibold bg-transparent border-b border-white/40 outline-none w-full max-w-[110px]" aria-label="Edit node label" />`; when not editing, render `<span class="text-white text-sm font-semibold truncate max-w-[110px] cursor-text" @ondblclick:stopPropagation="true" @ondblclick="StartLabelEdit" title="Double-click to rename">@DisplayLabel()</span>` (c) Add methods: `private void StartLabelEdit() { _previousLabelAtEditStart = Node.WorkflowNode.Label; _labelBuffer = _previousLabelAtEditStart; _isEditingLabel = true; InvokeAsync(async () => { await Task.Yield(); try { await _labelInputRef.FocusAsync(); } catch { } }); }` `private void OnLabelInput(ChangeEventArgs e) => _labelBuffer = e.Value?.ToString() ?? string.Empty;` `private void OnLabelKeyDown(KeyboardEventArgs e) { if (e.Key == "Enter") CommitLabel(); else if (e.Key == "Escape") CancelLabel(); }` `private void CommitLabel() { if (!_isEditingLabel) return; _isEditingLabel = false; Node.RaiseLabelCommitted(_previousLabelAtEditStart, _labelBuffer); }` — the `!_isEditingLabel` early-return prevents the Enter-keydown + immediate-blur double-fire race `private void CancelLabel() { _isEditingLabel = false; }` `private string DisplayLabel() => string.IsNullOrEmpty(Node.WorkflowNode.Label) ? GetFallbackLabel() : Node.WorkflowNode.Label;` `private string GetFallbackLabel() => Node.WorkflowNode.NodeType switch { WorkflowNodeType.Trigger => "Start / Trigger", WorkflowNodeType.AgenticReason => "AI Agent", WorkflowNodeType.HumanApproval => "Ask a Person", WorkflowNodeType.FunctionRoute => "Smart Branch", WorkflowNodeType.FunctionTransform => "Transform", WorkflowNodeType.FunctionNotify => "Notify", WorkflowNodeType.FunctionData => "Save / Load", _ => "Step" };`; depends on T003 and T004 (T004 adds `RaiseLabelCommitted` to `WorkflowNodeModel`); confirm T006 GREEN
- [X] T010 [US1] Verify `LabelCommitted` event subscription in `WorkflowCanvas.razor`: confirm that T004 step (e) correctly subscribed `nodeModel.LabelCommitted += ...` in every node-addition code path — search the canvas for all locations where nodes are added to `_diagram` (initial load and palette additions) and verify each site has the `LabelCommitted` subscription alongside the existing `DoubleClicked` and `ContextMenuRequested` subscriptions; depends on T004 and T009; run `dotnet build` to confirm zero errors; run `dotnet test --filter "FullyQualifiedName~RenameLabelAction"` to confirm label action tests GREEN; confirm T007 E2E Scenarios 1–3 are GREEN (Scenarios 4–5 remain RED until T013–T016 complete)

**Checkpoint**: Quickstart.md Scenarios 1–3 pass. User Story 1 is independently functional. `dotnet test` shows T005–T006 GREEN.

---

## Phase 4: User Story 2 — User Corrects a Typo (Priority: P1)

**Goal**: Re-activating a node label whose value was previously set to a custom value must show
that custom value in the edit input — never the type-default. This exercises FR-12.6.

**Independent Test**: Rename a node, commit, then double-click the label again. The input must
contain the previously typed text. Change it and commit. The node must show the update.

### Test — Write First (RED)

- [X] T011 [P] [US2] Add to `tests/DBAIAzure.Tests/WorkflowNodeLabelEditTests.cs`: test method `ReEdit_ShowsCommittedValue_NotTypeDefault`: create a `WorkflowNodeModel` where `WorkflowNode.Label == "My Step"` (simulating a previously committed custom label); render `WorkflowNodeRenderer`; double-click the label; assert the edit input contains `"My Step"` — not the type-default name (e.g. not `"AI Agent"`); change to `"My Corrected Step"`; commit; assert `LabelCommitted` raised with `PreviousLabel == "My Step"` and `NewLabel == "My Corrected Step"`; confirm RED

### Verification (No New Implementation Required)

- [X] T012 [US2] Confirm T011 is GREEN after T009 and T010 are complete — FR-12.6 is satisfied by `_labelBuffer = Node.WorkflowNode.Label` in `StartLabelEdit()`, which reads the current committed label (not the type-default); run `dotnet test --filter "FullyQualifiedName~WorkflowNodeLabelEditTests"` and confirm GREEN; no code changes needed

**Checkpoint**: T011 GREEN. User Story 2 verified by the US1 implementation.

---

## Phase 5: User Story 3 — User Leaves a Node Label Empty (Priority: P2)

**Goal**: When a user commits an empty label, the node never shows a blank rectangle. It
displays either the type-default name or a visually distinct "Untitled node" placeholder.
The edit input must be empty (not pre-filled with the placeholder) when re-activated.

**Independent Test**: Clear a node's label completely and commit. The canvas node must show a
non-blank label. Double-click that label again; the input field must be empty.
See quickstart.md Scenario 5 for full pass criteria.

### Test — Write First (RED)

- [X] T013 [P] [US3] Add to `tests/DBAIAzure.Tests/WorkflowNodeLabelEditTests.cs`: test method `EmptyLabel_DisplaysFallback_NotBlank`: create a `WorkflowNodeModel` with `WorkflowNode.Label == string.Empty`; render `WorkflowNodeRenderer` (not in edit mode); assert the displayed text in the label span is non-empty (contains a type-default name or "Untitled"); then double-click to activate edit; assert the input value is `string.Empty` — not the fallback text; confirm RED

### Implementation (GREEN)

- [X] T014 [US3] `DisplayLabel()` is already implemented in T009 — verify T013 is GREEN after T009 completes; if `DisplayLabel()` returns an empty string in any branch, correct it so it always returns a non-empty string when `Node.WorkflowNode.Label` is empty (fallback to `GetFallbackLabel()` result); `GetFallbackLabel()` must never return empty string; no Blazor markup changes required beyond what T009 adds; run `dotnet test --filter "FullyQualifiedName~WorkflowNodeLabelEditTests"` and confirm GREEN

**Checkpoint**: T013 GREEN. Quickstart.md Scenario 5 passes. User Story 3 verified.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Keyboard accessibility, undo stack verification, E2E regression gate, and CHANGELOG.

- [X] T015 [P] Add keyboard accessibility to `WorkflowNodeRenderer.razor`: (a) add `tabindex="0"` attribute to the outer `<div class="workflow-node ...">` container so every node is reachable via Tab; (b) add `@onkeydown="OnNodeKeyDown"` on that same div with handler `private void OnNodeKeyDown(KeyboardEventArgs e) { if ((e.Key == "Enter" || e.Key == "F2") && !_isEditingLabel) StartLabelEdit(); }`; (c) add `tabindex="0"` to the display `<span>` label so keyboard users who Tab to the label specifically can also press Enter; (d) add bUnit test method `KeyboardEnter_ActivatesLabelEdit` to `WorkflowNodeLabelEditTests.cs`: render `WorkflowNodeRenderer`, simulate `keydown` with `Key == "Enter"` on the outer div, assert `_isEditingLabel == true`; confirm RED → implement → GREEN
- [X] T016 [P] Add undo verification test to `tests/DBAIAzure.Tests/WorkflowNodeLabelEditTests.cs`: test method `Undo_RestoresLabelInCommitOrder`: render a `WorkflowCanvas`; call `OnLabelCommitted(new LabelCommitArgs(nodeId, "Original", "First"))` then `OnLabelCommitted(new LabelCommitArgs(nodeId, "First", "Second"))`; assert the node label is `"Second"`; call `Undo()` — assert `"First"`; call `Undo()` again — assert `"Original"`; call `Undo()` a third time — assert still `"Original"` (undo stack exhausted for label changes without further structural actions); run `dotnet test` and confirm GREEN
- [X] T017 [P] Add Playwright E2E tests to `tests/DBAIAzure.E2ETests/WorkflowNodeLabelEditTests.cs` for Scenarios 6–8 from `quickstart.md`: Scenario 6 (`KeyboardOnly_CanEditLabel` — Tab to node, Enter to activate, type, Enter to commit, assert label updated); Scenario 7 (`AllNodeTypes_LabelsEditable` — parameterised test iterating every node type from the palette); Scenario 8 (`ExistingInteractions_NotRegressed` — port drag, node drag, context menu, and Ctrl+Z node deletion all function after label editing is added)
- [ ] T018 Run the full Playwright E2E suite via `.\scripts\run-e2e.ps1`; confirm all pre-existing tests pass; confirm all new tests in `WorkflowNodeLabelEditTests.cs` (Scenarios 1–8) pass; record any failures before marking complete
- [X] T019 [P] Update `CHANGELOG.md` — add a new entry under `2026-06-21` with the following two items: (1) **Bug fix**: "Config panel inputs (Goal, Input label, Output label) no longer reset to their saved values mid-keystroke when the canvas live-previews the node label. Root cause: `OnParametersSet` in `WorkflowNodeConfigPanel.razor` now skips re-initialisation while the same node is still open (FR-12.2)." (2) **Feature**: "Inline node label editing on the canvas — double-click a node's name to rename it in place; Enter commits, Escape cancels; Ctrl+Z undoes committed renames; Tab + Enter enables keyboard-only editing (FR-12.1–12.10)."

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — run immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — **BLOCKS all user story phases**
- **Phase 3 (US1)**: Depends on Phase 2 — this is the core implementation phase; US2 and US3 cannot pass until US1 implementation is complete
- **Phase 4 (US2)**: Depends on Phase 3 (T009 and T010 must be GREEN)
- **Phase 5 (US3)**: Depends on Phase 3 (T009 must be GREEN; `DisplayLabel()` must exist)
- **Phase 6 (Polish)**: Depends on Phases 3–5

### User Story Dependencies

- **US1 (P1)**: Depends on Phase 2 only. No dependency on US2 or US3.
- **US2 (P1)**: Depends on US1 implementation (T009, T010). T011 test can be written in parallel with US1 implementation.
- **US3 (P2)**: Depends on US1 implementation (T009). T013 test can be written in parallel with US1 implementation.

### Within Phase 2

- T002 (test) must be written and confirmed RED before T003 and T004 begin
- T003 and T004 are sequential: T003 first (struct), T004 second (action + handler that uses the struct)

### Within Phase 3

- T005, T006, T007 (tests) must be written and confirmed RED before T008–T010 begin
- T005, T006, T007 are parallel (different files)
- T008 is independent of T009 and T010 (different file)
- T009 depends on T003 (needs `LabelCommitArgs` type) and T004 (needs `RaiseLabelCommitted` on `WorkflowNodeModel`)
- T010 depends on T004 (needs `OnLabelCommitted` handler and `LabelCommitted` event on `WorkflowNodeModel`) and T009 (needs `Node.RaiseLabelCommitted()` call to exist in the renderer)

### Within Phase 6

- T015, T016, T017, T019 are parallel (different files / independent concerns)
- T018 depends on T015, T016, T017 (all new tests must exist before the full suite run)

---

## Parallel Execution Examples

### Phase 2 (Foundational)

```text
# Write test first (single task):
T002 — WorkflowNodeLabelEditTests.cs (RenameLabelAction unit tests)
# Confirm RED

# Then run in sequence:
T003 — WorkflowDiagramModels.cs (LabelCommitArgs struct)
T004 — WorkflowCanvas.razor (RenameLabelAction + ApplyLabelChange + OnLabelCommitted)
```

### Phase 3 (US1)

```text
# Write tests first (run in parallel — different files):
T005 — WorkflowNodeConfigPanelResetGuardTests.cs
T006 — WorkflowNodeLabelEditTests.cs (inline edit tests)
T007 — DBAIAzure.E2ETests/WorkflowNodeLabelEditTests.cs (E2E stubs)
# Confirm all RED

# Then implement (T008 and T009 can run in parallel — different files):
T008 — WorkflowNodeConfigPanel.razor (reset guard — bug fix, no dependencies)
T009 — WorkflowNodeRenderer.razor (inline edit — depends on T003)

# After T009:
T010 — WorkflowCanvas.razor (verify LabelCommitted event subscription — depends on T004 + T009)
```

### Phase 4 + 5 (US2 + US3 — after Phase 3)

```text
# Run in parallel (different test methods in the same file):
T011 — WorkflowNodeLabelEditTests.cs (US2 re-edit test) → T012 verify GREEN
T013 — WorkflowNodeLabelEditTests.cs (US3 empty-label test) → T014 verify GREEN
```

### Phase 6 (Polish — after Phases 3–5)

```text
# Run in parallel (independent concerns):
T015 — WorkflowNodeRenderer.razor (keyboard accessibility)
T016 — WorkflowNodeLabelEditTests.cs (undo verification test)
T017 — DBAIAzure.E2ETests/WorkflowNodeLabelEditTests.cs (Scenarios 6–8)
T019 — CHANGELOG.md

# After T015 + T016 + T017:
T018 — Run full Playwright E2E suite (.\scripts\run-e2e.ps1)
```

---

## Implementation Strategy

### MVP First (US1 Only — Scenarios 1–3)

1. Phase 1 (1 task — minutes)
2. Phase 2 (3 tasks — new struct + undo action)
3. Phase 3 Tests (T005–T007 RED)
4. Phase 3 Implementation (T008–T010 GREEN)
5. **STOP AND VALIDATE**: Run Playwright Scenarios 1–3 and `dotnet test` — all must pass
6. **Ship if acceptable**: The core bug is fixed and inline editing works

### Full Delivery (all 3 user stories + polish)

1. MVP scope above
2. Phase 4 (T011–T012) — re-edit verification
3. Phase 5 (T013–T014) — empty label placeholder
4. Phase 6 (T015–T019) — keyboard access, undo test, E2E full run, CHANGELOG

### Parallel Developer Strategy (2 developers)

After Phase 2 and Phase 3 tests are RED:
- **Developer A**: T008 — `WorkflowNodeConfigPanel.razor` bug fix (independent of everything else in Phase 3)
- **Developer B**: T009 — `WorkflowNodeRenderer.razor` inline editing (core new feature)
- Both merge; then T010 wires them together in `WorkflowCanvas.razor`

---

## Notes

- `[P]` tasks modify different files and have no dependency on incomplete tasks in the same phase — safe to parallelize
- `[Story]` label maps each task to its user story for traceability against spec.md acceptance scenarios
- Constitution Article V: every implementation task has a corresponding failing test written first
- Constitution Article VII: Blazor Server native primitives (`@ondblclick:stopPropagation`, `value` + `@oninput` one-way binding, `ElementReference.FocusAsync`, `tabindex`, `ICanvasAction` extension) are used throughout; no new NuGet packages, no JS interop beyond the single `FocusAsync` call Blazor provides
- The two inner-class additions (`RenameLabelAction` in T004) follow the same pattern as the existing `AddNodeAction`, `AddEdgeAction`, and `UndoDeleteNodeCommand` inner classes already in `WorkflowCanvas.razor`
- Run `dotnet build` after every task that touches `.cs` or `.razor` files to catch compilation errors early
- The `@ondblclick:stopPropagation="true"` on the label container is load-bearing: without it, a label double-click also fires `Node.RaiseDoubleClicked()` and opens the config panel simultaneously — T006 test (c) verifies the panel does not open on a label double-click
