# Research: Fix Node Text Editing in Workflow Builder

**Date**: 2026-06-21 | **Plan**: [plan.md](plan.md)

---

## Decision 1 — Root Cause: `OnParametersSet` re-initialises editing fields on every parent re-render

**Decision**: The primary bug is in `WorkflowNodeConfigPanel.razor`. Its `OnParametersSet()`
override unconditionally resets `_goalPrompt`, `_inputLabel`, and `_outputLabel` to the values
from the `Node` parameter every time the parent component re-renders, regardless of whether the
user is actively editing. Fix by tracking which node ID the panel was last initialised for and
skipping the field reset whenever the same node is still open.

**Rationale**: The exact failure path is:

1. User opens the config panel (double-click a node). `OnParametersSet` runs, setting
   `_goalPrompt = Node.GoalPrompt` — correct so far.
2. User begins typing in the Goal textarea. `@oninput` handler (`OnGoalInput`) updates
   `_goalPrompt` to the in-progress text and starts a 200 ms debounce.
3. The debounce fires, calling `OnGoalPreview.InvokeAsync(preview)` on the parent
   (`WorkflowBuilder.razor`). The parent updates the live canvas label preview via
   `_canvas?.UpdateNodeFromConfig(...)` and calls `StateHasChanged()`.
4. This parent re-render passes the same `Node` parameter to the config panel. Blazor fires
   `OnParametersSet()` again. The panel resets `_goalPrompt = Node.GoalPrompt` — which is the
   OLD saved value (the user's edit was never committed to `Node.GoalPrompt`).
5. Blazor re-renders the `<textarea value="@_goalPrompt">` with the old text. The user's typed
   text is lost.

The same cascade happens on `_inputLabel` and `_outputLabel` for non-AgenticReason nodes.

**Fix**: Introduce `_lastInitialisedNodeId` (string?) in the panel. In `OnParametersSet()`,
only re-initialise fields when `Node?.Id != _lastInitialisedNodeId`. Update
`_lastInitialisedNodeId = Node?.Id` after initialisation. On close (`OnCloseAsync`),
reset `_lastInitialisedNodeId = null` so the next node opens cleanly.

**Alternatives considered**:
- `ShouldRender()` returning false while editing: `ShouldRender()` only prevents rendering,
  not parameter processing — `OnParametersSet()` still fires; rejected.
- Converting all inputs to `@bind:event="oninput"`: `@bind` still re-applies the bound
  value from the field on every render unless the field itself is not reset; the reset
  in `OnParametersSet()` remains the root cause; rejected as incomplete alone.
- JS interop to hold the cursor position: treats the symptom (visual reset) not the
  cause (field reset); rejected.

---

## Decision 2 — New Feature: Inline Label Editing on `WorkflowNodeRenderer`

**Decision**: Add a dual-state label region to `WorkflowNodeRenderer.razor`. In display mode
a `<span>` shows the node's `Label` property. A double-click specifically on that span (with
`@ondblclick:stopPropagation="true"` to prevent the node-level double-click handler from also
opening the config panel) toggles `_isEditingLabel = true`, replacing the span with an
`<input type="text">`. On Enter or blur the input is committed via a `LabelCommitted`
EventCallback. On Escape the edit is cancelled. The `<input>` is isolated from Blazor's
reactive binding loop by holding in-progress text in a local `_labelBuffer` field that is
never reset by `OnParametersSet`.

**Rationale**: The spec describes double-click on the "label region" as the activation gesture
for inline editing (FR-12.1), distinct from the config panel which handles Goal, port labels,
and IsConfigured state. The `@ondblclick:stopPropagation` technique is idiomatic Blazor and
prevents event bubbling to the node's existing `@ondblclick="OnDoubleClick"` handler without
requiring changes to Z.Blazor.Diagrams infrastructure.

The local `_labelBuffer` field is the key isolation mechanism: it is set once from
`Node.WorkflowNode.Label` when editing begins and is never touched by `OnParametersSet` or any
diagram refresh thereafter. Because the `<input>` binds to `_labelBuffer` one-way
(`value="@_labelBuffer" @oninput="OnLabelInput"`) and the parent's re-render cannot change
`_labelBuffer`, the mid-edit reset bug cannot occur for the inline label input.

**Alternatives considered**:
- Separate "rename" dialog/popover: extra UI surface for a simple rename; rejected in favour
  of the direct, in-place edit pattern used by all major node-graph tools.
- JS contenteditable on the label span: requires JS interop to read/write the editable
  content; violates the framework-first preference; rejected.

---

## Decision 3 — `RenameLabelAction` on the Existing Undo Stack

**Decision**: Add a `RenameLabelAction : ICanvasAction` inner class to `WorkflowCanvas.razor`.
It stores `nodeId`, `previousLabel`, and `newLabel`. `Do()` and `Undo()` both call a new
`ApplyLabelChange(nodeId, label)` method that looks up the node model, creates a new
`WorkflowNode` record via `with { Label = newLabel }`, assigns it to `nodeModel.WorkflowNode`,
and calls `_diagram.Refresh()`. The action is pushed to the existing 50-step undo stack via
the existing `RecordAction()` method.

**Rationale**: The existing `ICanvasAction` command pattern is the correct extension point
(as confirmed by `UndoDeleteNodeCommand` in spec 004 / research Decision 7). No new undo
infrastructure is needed. The `RecordAction()` / `_undoStack` / `Undo()` plumbing is already
wired to Ctrl+Z via the keyboard shortcuts manager. Adding `RenameLabelAction` to the same
stack ensures label undo behaves identically to node-deletion undo, satisfying FR-12.10.

Commits where `previousLabel == newLabel` are skipped (no-op guard) to avoid polluting the
stack when the user "edits" a label without changing it.

**Alternatives considered**:
- A separate label history stack: creates two independent undo streams (Ctrl+Z behaves
  differently depending on what was last changed); rejected — single unified stack is
  essential for predictable undo.
- Undo only within the input field (browser-native text undo): does not survive a commit;
  once Enter is pressed, browser text undo is cleared; rejected as insufficient per
  FR-12.10.

---

## Decision 4 — Keyboard Accessibility via `tabindex` and `@onkeydown`

**Decision**: Add `tabindex="0"` to the outer `<div>` in `WorkflowNodeRenderer.razor`.
Add `@onkeydown="OnNodeKeyDown"` on that same div with a handler that calls
`StartLabelEdit()` when `e.Key == "Enter"`. This makes every canvas node reachable via
Tab (browser default focus order) and activatable for label editing via Enter, satisfying
FR-12.9 without Z.Blazor.Diagrams changes or JS interop.

The label `<span>` also receives `tabindex="0"` and its own `@onkeydown` handler so a
keyboard user who Tabs directly to the label can press Enter to open the inline input.
When the `<input>` is active, Enter commits and Escape cancels — both are handled by
`OnLabelKeyDown` via `e.Key == "Enter"` / `e.Key == "Escape"`.

**Rationale**: `tabindex="0"` is the lightest-weight accessibility addition possible —
no JS, no ARIA roles beyond what already exists, no library changes. The Z.Blazor.Diagrams
canvas container is a scrollable viewport; adding `tabindex` to node tiles lets the browser
manage Tab order naturally. This satisfies the constitution's preference for framework-first
solutions before custom code.

**Alternatives considered**:
- Z.Blazor.Diagrams built-in keyboard navigation: the library has no built-in Tab-through-nodes
  feature; custom `tabindex` is the documented approach for accessibility on Blazor Diagrams
  components.
- ARIA `role="treeitem"` pattern for the canvas: appropriate for truly hierarchical trees;
  a flat node graph with Tab navigation via `tabindex` is simpler and more discoverable;
  deferred to a future full accessibility audit.
