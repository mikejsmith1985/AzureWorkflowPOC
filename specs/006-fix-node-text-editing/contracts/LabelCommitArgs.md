# Contract: `LabelCommitArgs`

**File**: `src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowDiagramModels.cs`
**Kind**: C# record struct (value type; no allocation overhead per callback invocation)

---

## Purpose

Carries the before/after label values from `WorkflowNodeRenderer` up to `WorkflowCanvas`
when the user commits an inline label edit. Enables `WorkflowCanvas` to create a
`RenameLabelAction` for the undo stack and apply the change to the diagram model.

---

## Definition

```csharp
/// <summary>
/// Payload raised by WorkflowNodeRenderer when the user commits an inline label edit.
/// Carries the node identifier and the before/after label values so the canvas can
/// update the node model and record an undoable action.
/// </summary>
public readonly record struct LabelCommitArgs(
    string NodeId,
    string PreviousLabel,
    string NewLabel);
```

---

## Constraints

| Property | Type | Rules |
|----------|------|-------|
| `NodeId` | `string` | Non-empty; matches `WorkflowNode.Id` of the edited node |
| `PreviousLabel` | `string` | The `WorkflowNode.Label` value when edit mode was entered |
| `NewLabel` | `string` | The committed text; may be empty string (empty-label scenario) |

- When `PreviousLabel == NewLabel`, the canvas handler is a no-op (no undo entry recorded,
  no diagram refresh triggered).
- `NewLabel` is never null; it is `string.Empty` when the user cleared the field.

---

## Wiring

The renderer is registered via `_diagram.RegisterComponent<WorkflowNodeModel, WorkflowNodeRenderer>()`
and is never instantiated directly in canvas razor markup. EventCallback parameters cannot be
wired from the canvas to registered components; instead the signal flows through a C# event on
the node model, matching the existing `DoubleClicked` and `ContextMenuRequested` pattern.

```
WorkflowNodeRenderer
  → private void CommitLabel()
      ↓ calls Node.RaiseLabelCommitted(previousLabel, newLabel)
WorkflowNodeModel (WorkflowDiagramModels.cs)
  → public event Action<string, string>? LabelCommitted
      ↓ fires subscription lambda registered in WorkflowCanvas
WorkflowCanvas
  → subscription: nodeModel.LabelCommitted += (prev, next) =>
        OnLabelCommitted(new LabelCommitArgs(nodeModel.WorkflowNode.Id, prev, next))
  → OnLabelCommitted(LabelCommitArgs args)
      ↓ if args.PreviousLabel == args.NewLabel → return (no-op)
      ↓ var action = new RenameLabelAction(this, args.NodeId, args.PreviousLabel, args.NewLabel)
      ↓ action.Do()
      ↓ RecordAction(action)
```

The subscription is registered in WorkflowCanvas alongside the existing `DoubleClicked` and
`ContextMenuRequested` subscriptions — in every code path where a node is added to `_diagram`.
