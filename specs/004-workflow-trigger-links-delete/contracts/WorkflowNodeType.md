# Contract: WorkflowNodeType Enum Extension

**File**: `src/DBAIAzure.Core/Models/WorkflowNodeType.cs`
**Type**: Domain enum (C# `enum`)
**Consumer projects**: `DBAIAzure.Web`, `DBAIAzure.Processes`, `DBAIAzure.Storage`

---

## Purpose

`WorkflowNodeType` classifies each canvas node and determines which execution step class
handles it at runtime. Adding `Trigger` as the first value (0) gives the execution
orchestrator a way to identify the graph's entry point before resolving the execution order.

---

## Extended Enum Definition

```csharp
/// <summary>
/// Classifies a workflow node's runtime execution strategy.
/// The numeric value also controls palette category sort order (lower = higher in palette).
/// </summary>
public enum WorkflowNodeType
{
    /// <summary>
    /// Marks the single entry point of a workflow.
    /// Has zero input ports; one or more output ports.
    /// Must appear exactly once per workflow definition.
    /// </summary>
    Trigger = 0,

    /// <summary>AI-powered reasoning step (existing).</summary>
    AgenticReason = 1,

    /// <summary>Deterministic branching / routing step (existing).</summary>
    FunctionRoute = 2,

    /// <summary>Data transformation step (existing).</summary>
    FunctionTransform = 3,

    /// <summary>Side-effect / notification step (existing).</summary>
    FunctionNotify = 4,

    /// <summary>Data-access step (existing).</summary>
    FunctionData = 5,

    /// <summary>Human-in-the-loop approval gate (existing).</summary>
    HumanApproval = 6,
}
```

---

## Constraints

1. **Exactly one** `Trigger` node per `WorkflowDefinition`. Enforced by
   `WorkflowDefinition.ThrowIfInvalid()` and the canvas drop guard.
2. **Zero input ports, one+ output ports**: the `WorkflowNode.CreateNew` factory must not
   create input ports for `Trigger` nodes; callers must not pass them.
3. **Serialisation**: existing SQLite JSON column stores `NodeType` as its numeric (`int`)
   value. Adding `Trigger = 0` is additive; no existing stored rows have `NodeType = 0`
   (all existing rows use values 1–6), so no migration or data backfill is needed.

---

## Impact on Downstream Consumers

| Consumer | Change Required |
|----------|----------------|
| `WorkflowExecutionOrchestrator` | Recognise `Trigger = 0` as the graph start node; skip executing it as a step (it has no executable logic — it only defines the entry point and initial context) |
| `WorkflowCodeGenerator` | Generate an entry-point comment / method signature from the Trigger node's `GoalPrompt` and `FunctionConfig.initialDataDescription` |
| `WorkflowNodePalette.razor` | Group `Trigger` under the "Triggers" category; render at top of palette |
| `WorkflowNodeRenderer.razor` | Apply green accent styling; suppress input-port rendering |
| `WorkflowNodeConfigPanel.razor` | Render "What starts this workflow?" and "Initial data" fields instead of Goal/InputLabel/OutputLabel |
