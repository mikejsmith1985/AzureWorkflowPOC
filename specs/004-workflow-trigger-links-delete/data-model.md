# Data Model: Workflow Trigger Node, Directional Links & Node Deletion

**Date**: 2026-06-19 | **Plan**: [plan.md](plan.md)

---

## Modified Entities

### `WorkflowNodeType` (enum) — `DBAIAzure.Core/Models/WorkflowNodeType.cs`

**Change**: Add one new enum value.

```
Trigger = 0   ← NEW (insert before existing values; 0 = "none before this")
AgenticReason = 1
FunctionRoute = 2
FunctionTransform = 3
FunctionNotify = 4
FunctionData = 5
HumanApproval = 6
```

**Why numeric ordering matters**: The Triggers palette category must render above all other
categories. Ordering the enum so `Trigger` is the lowest value lets palette-grouping code
sort by `(int)NodeType` without a separate ordering table.

**Validation rule**: A `WorkflowDefinition` may contain at most one node where
`NodeType == WorkflowNodeType.Trigger`. This invariant is enforced in
`WorkflowDefinition.ThrowIfInvalid()` (see below).

---

### `WorkflowNode` (record) — `DBAIAzure.Core/Models/WorkflowNode.cs`

**Change**: Extend the `CreateNew` factory to handle `WorkflowNodeType.Trigger`.

**Trigger node port layout**:
- **Input ports**: none (empty `IReadOnlyList<WorkflowPort>`)
- **Output ports**: one port, `Id = "begin"`, `Label = "Begin"`, `Direction = PortDirection.Output`

**Trigger node default field values**:
- `Label = "Start / Trigger"`
- `GoalPrompt = ""` — populated by user in config panel ("What starts this workflow?")
- `InputLabel = "Trigger"` — used as the port label visible on the canvas
- `OutputLabel = "Begin"` — label of the single output port
- `FunctionConfig = "{\"initialDataDescription\":\"\"}"` — JSON blob; the
  "What information is available at the start?" field maps to `initialDataDescription`
- `IsConfigured = false` — set to `true` when `GoalPrompt` is non-empty

**No storage schema change**: `GoalPrompt` and `FunctionConfig` already exist on
`WorkflowNode`; the Trigger type reuses them with domain-specific field labels. No
EF Core migration is required.

---

### `WorkflowDefinition` (record) — `DBAIAzure.Core/Models/WorkflowDefinition.cs`

**Change**: Add a `ThrowIfInvalid()` method.

**New method contract**:

```
/// <summary>
/// Validates structural invariants of the workflow definition before persistence.
/// Throws <see cref="InvalidOperationException"/> if any invariant is violated,
/// with a plain-language message suitable for display to a non-technical user.
/// </summary>
void ThrowIfInvalid()
```

**Invariants checked**:
1. At most one node with `NodeType == WorkflowNodeType.Trigger`
   - Error message: `"A workflow may contain only one starting trigger. Remove the extra trigger before saving."`

**Called by**: `WorkflowBuilderService.SaveAsync` before passing the definition to
`IWorkflowRepository.SaveAsync`.

---

### `WorkflowEdgeModel` (class) — `DBAIAzure.Web/Components/WorkflowBuilder/WorkflowDiagramModels.cs`

**Change**: Add `IsAnimating` flag and configure arrowhead marker on construction.

**New property**:

```
/// <summary>
/// When true, the execution-flow animation (travelling dot) plays on this edge.
/// Set to true when execution reaches the source node; false when it leaves the target.
/// </summary>
public bool IsAnimating { get; set; }
```

**Constructor change**: Set `TargetMarker = LinkMarker.Arrow(20, 14)` in the constructor
body. `SourceMarker` remains `LinkMarker.None`.

**Rendering note**: The `IsAnimating` flag is read by the canvas render loop (or a custom
Blazor link widget) to conditionally apply the `edge-flow-active` CSS class to the SVG
`<path>` element. The CSS class drives the `animateMotion` animation defined in
`workflow-canvas-animations.css`.

---

### `UndoDeleteNodeCommand` (new record) — `DBAIAzure.Web/Components/WorkflowBuilder/WorkflowCanvas.razor` (nested or co-located)

**Purpose**: Encapsulates a node deletion so it can be pushed onto the existing
`UndoRedoStack` and reversed.

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| `Node` | `WorkflowNodeModel` | The deleted node model (retains all ports and domain data) |
| `Position` | `Point` | Canvas position at time of deletion for exact restoration |
| `AttachedEdges` | `WorkflowEdgeModel[]` | All edge models that were removed alongside the node |

**Methods** (implementing the existing `IUndoCommand` interface or equivalent):
- `Execute(BlazorDiagram diagram)` — removes the node and all `AttachedEdges` from the diagram
- `Undo(BlazorDiagram diagram)` — re-adds the node at `Position`, restores all `AttachedEdges`

---

## New Entities

### `NodeContextMenuState` (record or struct) — co-located in `WorkflowCanvas.razor`

**Purpose**: Transient Blazor render state for the right-click context menu. Not persisted.

| Field | Type | Description |
|-------|------|-------------|
| `IsVisible` | `bool` | Whether the context menu is currently rendered |
| `CanvasX` | `double` | Left offset in pixels from the canvas container's top-left corner |
| `CanvasY` | `double` | Top offset in pixels from the canvas container's top-left corner |
| `TargetNodeId` | `string?` | The `WorkflowNodeModel.Id` of the right-clicked node; null when menu is closed |

**State transitions**:
- `→ Visible`: `@oncontextmenu` fires on a `WorkflowNodeRenderer` tile; `CanvasX`/`CanvasY` set from `MouseEventArgs.ClientX/Y` minus cached container offset
- `→ Hidden`: user clicks outside the menu, presses Escape, or activates a menu action

---

## Validation Rules Summary

| Rule | Where Enforced | Error Response |
|------|----------------|----------------|
| At most one Trigger node per workflow | `WorkflowDefinition.ThrowIfInvalid()` (domain) AND `WorkflowCanvas` drop handler (UI) | Domain: `InvalidOperationException`; UI: amber banner |
| Trigger node has zero input ports | `WorkflowNode.CreateNew` factory | `ArgumentException` if caller passes input ports for Trigger type |
| Connection must be output→input | Existing `ValidateLink` delegate (extended) | Amber inline hint; link creation cancelled |
| No code generation / execution without Trigger | `WorkflowCanvas` Run button handler | Amber badge in toolbar; action blocked |

---

## State Transition: Trigger Node Configuration

```
[Placed on canvas]
    GoalPrompt = ""
    IsConfigured = false
    → Shows amber "!" badge on node tile

[User opens config panel and enters text in "What starts this workflow?"]
    GoalPrompt = "<user text>"
    IsConfigured = true
    → Amber "!" badge removed; node label updates to first 40 chars of GoalPrompt
```

---

## State Transition: Edge Animation During Execution

```
[Workflow execution reaches a node's upstream edge]
    WorkflowEdgeModel.IsAnimating = true
    → CSS class `edge-flow-active` applied → `animateMotion` dot plays

[Execution leaves the target node (node completes or fails)]
    WorkflowEdgeModel.IsAnimating = false
    → CSS class removed → dot disappears
```
