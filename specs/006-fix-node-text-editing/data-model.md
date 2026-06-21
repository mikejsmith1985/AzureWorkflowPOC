# Data Model: Fix Node Text Editing

**Date**: 2026-06-21 | **Plan**: [plan.md](plan.md)

---

## Overview

This fix introduces no new persisted entities and no database migration. All changes are to
Blazor component state and the in-memory canvas action model. The `WorkflowNode` immutable
record is unchanged — its existing `Label` property is the storage target for renamed labels.

---

## Existing Entity: `WorkflowNode` (unchanged)

```
WorkflowNode (sealed record)
├── Id              : string          — 8-char hex identifier; never changes
├── Label           : string          — User-visible node name (MAX 50 chars implied by existing UI truncation)
├── NodeType        : WorkflowNodeType
├── GoalPrompt      : string?
├── InputLabel      : string?
├── OutputLabel     : string?
├── PositionX       : double
├── PositionY       : double
├── IsConfigured    : bool
├── InputPorts      : IReadOnlyList<WorkflowPort>
└── OutputPorts     : IReadOnlyList<WorkflowPort>
```

**Label semantics** (clarified by this fix):
- `Label` holds the user-assigned display name for the node.
- For AgenticReason nodes: when saved via the config panel, Label is set to the Goal text
  (existing behaviour). When renamed via inline editing, Label is updated independently and
  the Goal is left unchanged. This means after an inline rename, `Label` and `GoalPrompt`
  may differ — this is intentional: the user gave the step a custom name.
- For all other node types: `Label` is the sole user-visible name and is set by inline editing.
- An empty committed `Label` (`""`) is stored as-is; the renderer displays the type-default
  name or "Untitled node" placeholder at render time (never stored).

---

## New Inner Class: `RenameLabelAction` (in `WorkflowCanvas.razor`)

This is an addition to the in-memory command pattern. Not persisted.

```
RenameLabelAction : ICanvasAction
├── _canvas        : WorkflowCanvas   — back-reference for Do/Undo dispatch
├── _nodeId        : string           — WorkflowNode.Id of the renamed node
├── _previousLabel : string           — Label value before the edit was committed
└── _newLabel      : string           — Label value after the edit was committed

Methods:
  Do()   → _canvas.ApplyLabelChange(_nodeId, _newLabel)
  Undo() → _canvas.ApplyLabelChange(_nodeId, _previousLabel)
```

**Invariants**:
- `_previousLabel != _newLabel` — a no-op rename is never recorded on the stack.
- The action holds label strings only, not the full `WorkflowNode` record, to minimise
  memory per stack entry.
- Undo depth remains 50 steps (shared with existing add/delete/move actions).

---

## New Component State: `WorkflowNodeRenderer` (in-memory, not persisted)

These fields exist only for the lifetime of the renderer component instance.

```
WorkflowNodeRenderer (Blazor component)
├── _isEditingLabel : bool    — true while the label <input> is active
├── _labelBuffer    : string  — in-progress label text; isolated from reactive re-render
└── _labelInputRef  : ElementReference — used to call FocusAsync() when edit mode starts
```

**Binding strategy** (the fix):
- Display mode: `<span>@Node.WorkflowNode.Label</span>` — one-way, read from the model.
- Edit mode:    `<input value="@_labelBuffer" @oninput="OnLabelInput">` — `value` is
  one-way from `_labelBuffer`; `@oninput` updates `_labelBuffer` only. Because `_labelBuffer`
  is a local field never touched by `OnParametersSet`, no parent re-render can overwrite
  what the user is typing.

---

## Modified Component State: `WorkflowNodeConfigPanel` (in-memory, not persisted)

One new field added to suppress the re-initialisation bug.

```
WorkflowNodeConfigPanel (Blazor component) — existing fields unchanged, plus:
└── _lastInitialisedNodeId : string?  — Id of the node whose values are in local fields;
                                        null after close; checked in OnParametersSet to
                                        gate whether field reset should run
```

**Reset guard logic**:
```
OnParametersSet():
  if Node?.Id == _lastInitialisedNodeId → return early (preserve in-progress edits)
  else → initialise all fields from Node + set _lastInitialisedNodeId = Node?.Id
OnCloseAsync():
  _lastInitialisedNodeId = null
```

---

## New Event: `LabelCommitted` (on `WorkflowNodeModel`)

```csharp
// WorkflowDiagramModels.cs — added to WorkflowNodeModel alongside DoubleClicked
public event Action<string, string>? LabelCommitted;
public void RaiseLabelCommitted(string previousLabel, string newLabel)
    => LabelCommitted?.Invoke(previousLabel, newLabel);
```

The renderer is registered via `_diagram.RegisterComponent<WorkflowNodeModel, WorkflowNodeRenderer>()`
and never appears directly in canvas razor markup; EventCallback `[Parameter]` values cannot be
injected from the canvas to registered components. The C# event on the model is the correct
channel, consistent with the existing `DoubleClicked` event on `WorkflowNodeModel`.

```
LabelCommitArgs (readonly record struct — declared in WorkflowDiagramModels.cs)
├── NodeId        : string  — WorkflowNode.Id
├── PreviousLabel : string  — label value when editing began (_previousLabelAtEditStart)
└── NewLabel      : string  — committed label value (may be empty string)
```

`LabelCommitArgs` is constructed in the canvas subscription lambda to provide a named, typed
payload to `OnLabelCommitted()`, which calls `ApplyLabelChange()` and `RecordAction()`.
