# Quickstart Validation Guide: Workflow Trigger Node, Directional Links & Node Deletion

**Date**: 2026-06-19 | **Plan**: [plan.md](plan.md)

This guide describes how to validate that the three new capabilities work correctly
end-to-end once implementation is complete.

---

## Prerequisites

1. `dotnet build AzureWorkflowPOC.sln` completes with zero errors and zero warnings.
2. SQLite database is initialised (`dotnet run --project src/DBAIAzure.Web` seeds the DB
   on first run).
3. A valid Anthropic API key is available via user secrets or vault inject (required for
   chat-assistant features; not required for canvas-only validation scenarios).

---

## Scenario 1 — Trigger Node: placement and single-trigger enforcement

**Purpose**: Verify FR-09.1 through FR-09.5.

### Steps

1. Open the workflow builder at `http://localhost:5000/workflow-builder` (new, empty canvas).
2. Observe the node palette. The **Triggers** category must appear at the top of the palette
   list, above "AI Steps."
3. Hover over the "Start / Trigger" entry in the palette. The tooltip must read:
   `"Marks where your workflow begins. Every workflow has exactly one. Connect it to your first step."`
4. Drag the "Start / Trigger" node onto the canvas.
   - Expected: node renders with a green accent, a play/flag icon, and **no input ports visible
     on the left side**. A subtle "Start here" label appears below the node.
5. Double-click the Trigger node to open its configuration panel.
   - Expected: panel shows exactly two fields: **"What starts this workflow?"** and
     **"What information is available at the start?"**. No "Goal" / model / temperature fields.
6. Attempt to drag a second "Start / Trigger" node from the palette onto the canvas.
   - Expected: the drag is cancelled, the node returns to the palette, and an amber banner
     appears reading `"Every workflow has exactly one starting trigger — this workflow already has one."`
7. Click the Run button in the toolbar on a canvas with **no** Trigger node (clear the canvas
   first and place only a non-trigger node with no connections).
   - Expected: Run is blocked; an amber badge in the toolbar reads `"Add a starting trigger to run this workflow."`

### Pass criteria

- [x] "Triggers" category is topmost in the palette
- [x] Trigger node renders with green accent and no input ports
- [x] Second Trigger placement blocked with amber banner
- [x] Run blocked with amber badge when no Trigger present

---

## Scenario 2 — Directional Connections: arrowhead and mid-line accent

**Purpose**: Verify FR-10.1 through FR-10.4.

### Steps

1. Place a "Start / Trigger" node and an "AI Reasoning" agentic node on the canvas.
2. Drag from the Trigger's **"Begin"** output port to the agentic node's input port.
3. Zoom the canvas to 100 % (default).
   - Expected: the connection arrow has a **filled arrowhead** at the agentic node end.
     At 100 % zoom the arrowhead must be visibly wide (target: ≥ 12 px) without hovering.
   - Expected: a directional mid-line accent (chevron or dash pattern) is visible along the
     connection line, oriented left-to-right (source → target).
4. Zoom out to 50 %. Both the arrowhead and the mid-line accent must remain legible.
5. Zoom in to 150 %. Both must scale proportionally — no clipping or distortion.
6. Attempt to **drag from the agentic node's input port** (left side) toward the Trigger node.
   - Expected: the drag is either cancelled or auto-corrected, and an inline amber hint appears:
     `"Connections start from the right side (output) of a node."` No backwards connection
     is silently created.

### Pass criteria

- [x] Arrowhead visible at 100 % zoom without hovering
- [x] Mid-line directional accent visible on every connection
- [x] Arrowhead and accent scale at 50 % and 150 % zoom
- [x] Input-port drag is rejected with inline hint; no backwards edge created

---

## Scenario 3 — Execution Flow Animation

**Purpose**: Verify FR-10.5.

### Steps

1. Build a three-node workflow: Trigger → AI Reasoning node → FunctionNotify node.
2. Configure the Trigger node with a goal (e.g., "A new support ticket arrives").
3. Click Run. Enter a test scenario in plain language when prompted.
4. Observe the canvas during execution.
   - Expected: when execution moves from the Trigger to the AI Reasoning node, the connecting
     arrow shows a **travelling dot** (or pulsing glow) moving from left to right. The
     animation must complete its travel in under 1 second.
   - Expected: when the AI Reasoning node completes, the animation on its outgoing edge
     to the FunctionNotify node begins.
   - Expected: after the run completes, **all animations stop** — no dots continue travelling
     on idle connections.

### Pass criteria

- [x] Travelling dot visible on active connections during execution
- [x] Animation travels source-to-target (not the reverse)
- [x] Dot disappears after run completes or is stopped

---

## Scenario 4 — Node Deletion

**Purpose**: Verify FR-11.1 through FR-11.6.

### Steps

1. Place three nodes on the canvas: Trigger → AI Reasoning → FunctionNotify.
   Connect all three with edges.
2. Click the AI Reasoning node to select it (single-click).
3. Press the **Delete** key.
   - Expected: the AI Reasoning node and **both connections** (Trigger→AI, AI→Notify) are
     removed instantly. No confirmation dialog appears.
   - Expected: the Trigger node and FunctionNotify node remain in their original positions
     and retain their configuration.
   - Expected: both the Trigger and FunctionNotify nodes now show amber "!" badges (no connections).
4. Press **Ctrl+Z** (undo).
   - Expected: the AI Reasoning node reappears at its exact previous canvas position.
     Both connections (Trigger→AI, AI→Notify) are restored. All three nodes are connected.
     Amber badges disappear from Trigger and FunctionNotify.
5. Right-click the AI Reasoning node.
   - Expected: a context menu appears with a **"Delete node"** option styled in red or with
     a trash icon. No other destructive actions listed.
6. Click **"Delete node"** from the context menu.
   - Expected: same result as Step 3 — node and both edges removed instantly.
7. Test the Trigger node specifically: select it and press Delete.
   - Expected: the Trigger node is deleted (no special protection). The canvas can now accept
     a new Trigger node from the palette.

### Pass criteria

- [x] Delete key removes node + all edges instantly
- [x] Adjacent nodes retain positions and configuration after deletion
- [x] Adjacent nodes gain amber badges when they become islands
- [x] Ctrl+Z restores node, position, and all former edges
- [x] Right-click context menu appears with "Delete node" option
- [x] Trigger node can be deleted (no special protection against its own deletion)

---

## Scenario 5 — Trigger vs Smart Branch Disambiguation

**Purpose**: Verify User Story 4 acceptance scenarios.

### Steps

1. Open the palette. Type `"start"` in the search box.
   - Expected: "Start / Trigger" appears as the top result. "Smart Branch" (or any branch
     node) does **not** appear.
2. Clear the search. Type `"branch"`.
   - Expected: branching/routing nodes appear. "Start / Trigger" does not appear.
3. Hover over "Start / Trigger" — tooltip text must describe entry-point semantics only.
4. Hover over a "Smart Branch" or "FunctionRoute" node — tooltip must describe routing/decision
   semantics only. No mention of starting or triggering.

### Pass criteria

- [x] "start" search returns Trigger; no branch nodes in results
- [x] "branch" search returns routing nodes; no Trigger in results
- [x] Tooltips cleanly distinguish starting (Trigger) from routing (Smart Branch)

---

## Automated Test Commands

```shell
# Run all tests (unit + bUnit component tests)
dotnet test tests/DBAIAzure.Tests/DBAIAzure.Tests.csproj --logger "console;verbosity=normal"

# Run only the new tests for this feature
dotnet test tests/DBAIAzure.Tests/ --filter "FullyQualifiedName~WorkflowNodeType|FullyQualifiedName~CanvasTrigger|FullyQualifiedName~NodeContextMenu|FullyQualifiedName~NodeRendererTrigger"
```

**Expected**: all tests pass; zero failures; zero skipped.

---

## References

- Data model: [data-model.md](data-model.md)
- Contracts: [contracts/WorkflowNodeType.md](contracts/WorkflowNodeType.md),
  [contracts/IWorkflowValidator.md](contracts/IWorkflowValidator.md)
- Spec: [spec.md](spec.md)
