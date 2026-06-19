# Implementation Plan: Workflow Trigger Node, Directional Links & Node Deletion

**Branch**: `feature/visual-workflow-builder` | **Date**: 2026-06-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/004-workflow-trigger-links-delete/spec.md`

---

## Summary

Three targeted gaps in the Visual Workflow Builder (spec 003) are closed by this plan:

1. **Trigger Node** — A new `WorkflowNodeType.Trigger` enum value, a "Triggers" palette category,
   single-trigger enforcement on the canvas, a Trigger-specific configuration panel layout, and a
   code-generation / execution hard-block when no Trigger is present.

2. **Directional Connections** — Enhanced `WorkflowEdgeModel` with configurable SVG arrowheads
   (12 px+), a mid-line directional accent via CSS `stroke-dashoffset` animation, input-port drag
   rejection with an inline hint, and an execution-phase travelling-dot animation injected as
   Blazor state on active edges.

3. **Node Deletion** — Delete/Backspace key handler for selected canvas nodes, a right-click
   `NodeContextMenu` Blazor component (new file), edge cascade-removal on deletion, and full
   undo integration with the existing 50-step `UndoRedoStack`.

All changes land in `DBAIAzure.Web` (Blazor components) and `DBAIAzure.Core` (domain models).
No new projects. No new storage schema (trigger config fields are stored in existing
`WorkflowNode.GoalPrompt` / `WorkflowNode.FunctionConfig` JSON blob).

---

## Technical Context

**Language/Version**: C# 12 / .NET 8 (Blazor Server)

**Primary Dependencies**:
- Z.Blazor.Diagrams v3.0.4.1 — canvas, node/link models, port direction, keyboard events
- Microsoft.SemanticKernel v1.77.0 — AI chat assistant (no changes in this sprint)
- Tailwind CSS (CDN, utility-first) — component styling

**Storage**: SQLite via EF Core 8 — `WorkflowDefinitionRecord` JSON column holds node list;
  the new Trigger config fields reuse `GoalPrompt` (what starts this workflow?) and
  `FunctionConfig` (initial data description as JSON blob). No migration needed.

**Testing**: xUnit 2.9.0 + bUnit 1.37.7 (Blazor component testing)

**Target Platform**: ASP.NET Core 8, Blazor Server, browser-rendered via SignalR

**Performance Goals**:
- Delete key → node removed within 200 ms (synchronous state mutation, no I/O)
- Palette search → results update within 100 ms (existing debounce; Triggers category inherits)
- Execution flow animation → dot travels source-to-target in < 1 s (CSS transition duration)

**Constraints**:
- Arrowhead visible at 100 % zoom without hover: minimum 12 px wide, 8 px tall SVG marker
- Single-trigger invariant enforced at canvas state level (not only in UI); the domain model
  must not allow a `WorkflowDefinition` with two nodes where both have `NodeType == Trigger`
- Context menu must not use JS interop for positioning — use Blazor CSS `position: absolute`
  relative to the node element (avoids async JS bridge latency on keystrokes)

**Scale/Scope**: Single-user Blazor Server session; canvas holds up to ~50 nodes in MVP scope

---

## Constitution Check

| Article | Gate | Status |
|---------|------|--------|
| I — Prime Directive | Best route chosen (no quick hacks; undo leverages existing stack) | ✓ PASS |
| II — Process Protection | No wildcard kills needed | ✓ N/A |
| III — Branching | Work continues on `feature/visual-workflow-builder` | ✓ PASS |
| IV — Code Quality | PascalCase types/members, `_camelCase` fields, XML docs on all new public API | ✓ PASS |
| V — Testing (3-layer) | Unit tests (WorkflowNodeType factory) + bUnit component tests (palette, canvas, context menu); no I/O in unit layer | ✓ PASS |
| VI — Docs Discipline | CHANGELOG.md updated in PR; no auxiliary summary docs | ✓ PASS |
| VII — Framework-First | See analysis below | ✓ PASS with documented gaps |
| VIII — Release | Not a release sprint | ✓ N/A |
| IX — Secrets | No secrets touched | ✓ N/A |
| X — Verification | Passing xUnit/bUnit tests + observed browser output (not "it compiles") | ✓ PASS |
| XI — Output Restraint | Plan artifacts in `specs/004/`; no ad-hoc status docs | ✓ PASS |

### Article VII — Framework-First Analysis

**Z.Blazor.Diagrams v3 provides natively**:
- `LinkMarker` with `Arrow` type — `TargetMarker = new LinkMarker(…)` supports custom SVG
  path and size → **use this** for 12 px arrowheads (no custom code needed)
- `PortModel.Alignment` (Left = Input, Right = Output) — already enforced in `WorkflowCanvas`
  via the existing port-direction toast guard → **use this** for input-port drag rejection
  (extend the existing guard rather than adding a new mechanism)
- `DiagramCanvas` keyboard event forwarding — `@onkeydown` propagates on the diagram container
  → **use this** for Delete/Backspace key handling (extend existing `HandleKeyDown` in
  `WorkflowCanvas.razor`)
- `Diagram.SelectionChanged` event → **use this** to know which node is selected for deletion

**Documented gaps requiring custom code**:
- **Execution flow animation**: Z.Blazor.Diagrams has no animated link traversal primitive.
  *Custom: CSS `@keyframes` travelling-dot on the SVG `<path>` of active `WorkflowEdgeModel`
  instances via a Blazor `EdgeAnimationState` flag injected as a CSS class.* Justification:
  framework has no equivalent; gap is documented.
- **Right-click context menu**: Z.Blazor.Diagrams does not provide a built-in context menu.
  *Custom: `NodeContextMenu.razor` Blazor component, rendered absolutely over the canvas,
  positioned via `@oncontextmenu` coordinates passed as Blazor state. No JS interop.* Justification:
  gap is documented.
- **Mid-line directional accent**: The library renders a plain Bezier path; no built-in chevron
  accent exists. *Custom: CSS `stroke-dasharray` / `stroke-dashoffset` static pattern on
  `WorkflowEdgeModel` SVG path class to produce a static directional dash accent.*

---

## Project Structure

### Documentation (this feature)

```text
specs/004-workflow-trigger-links-delete/
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── quickstart.md        ← Phase 1 output
├── contracts/
│   ├── WorkflowNodeType.md   ← enum contract extension
│   └── IWorkflowValidator.md ← new validation interface contract
└── tasks.md             ← Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/DBAIAzure.Core/
└── Models/
    ├── WorkflowNodeType.cs          [MODIFY] add Trigger value
    └── WorkflowNode.cs              [MODIFY] factory CreateNew handles Trigger ports + domain invariant

src/DBAIAzure.Web/
├── Components/WorkflowBuilder/
│   ├── WorkflowCanvas.razor         [MODIFY] Delete/Backspace handler; single-trigger guard;
│   │                                         input-port drag rejection hint; context menu wire-up
│   ├── WorkflowDiagramModels.cs     [MODIFY] WorkflowEdgeModel — arrowhead marker + animation flag
│   ├── WorkflowNodePalette.razor    [MODIFY] Triggers category (top); palette search excludes
│   │                                         Trigger from branch/decide searches; Trigger ≠ Smart Branch tooltip
│   ├── WorkflowNodeRenderer.razor   [MODIFY] Trigger type → green accent, no input ports, "Start here" label
│   ├── WorkflowNodeConfigPanel.razor [MODIFY] Trigger branch: "What starts this workflow?" + "Initial data"
│   ├── WorkflowToolbar.razor        [MODIFY] no-trigger amber badge; block Run/Generate when absent
│   └── NodeContextMenu.razor        [NEW]    right-click context menu; "Delete node" option

└── wwwroot/css/
    └── workflow-canvas-animations.css [NEW]  execution-flow dot animation + mid-line accent keyframes

tests/DBAIAzure.Tests/
├── WorkflowNodeTypeTests.cs         [NEW] unit — Trigger factory, single-trigger domain invariant
├── WorkflowCanvasTriggerTests.cs    [NEW] bUnit — palette Triggers category, second-trigger block
├── NodeContextMenuTests.cs          [NEW] bUnit — right-click menu render, delete action
└── WorkflowNodeRendererTriggerTests.cs [NEW] bUnit — green accent, no input ports visible
```

**Structure Decision**: Single web-project structure. All new components are in the existing
`WorkflowBuilder/` component folder. One new CSS file under `wwwroot/css/`. No new projects,
no new namespaces beyond the existing `DBAIAzure.Web.Components.WorkflowBuilder`.

---

## Complexity Tracking

No constitution violations requiring justification. All three custom code items
(execution animation, context menu, mid-line accent) are documented gaps against
Z.Blazor.Diagrams v3 with justifications recorded inline in the Article VII section above.
