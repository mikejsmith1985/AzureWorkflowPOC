# Research: Workflow Trigger Node, Directional Links & Node Deletion

**Date**: 2026-06-19 | **Plan**: [plan.md](plan.md)

---

## Decision 1 — Z.Blazor.Diagrams v3 Link Marker API for Custom Arrowheads

**Decision**: Use `LinkMarker.Arrow(width, height)` static factory on `WorkflowEdgeModel`.
Set `TargetMarker = LinkMarker.Arrow(20, 14)` (renders a 20×14 px arrowhead at the target
end) and `SourceMarker = LinkMarker.None` (no source arrowhead). These are set once when
`WorkflowEdgeModel` is constructed.

**Rationale**: Z.Blazor.Diagrams v3 exposes `DefaultLinkModel.SourceMarker` and
`DefaultLinkModel.TargetMarker` as `LinkMarker` properties. The `LinkMarker.Arrow(width,
height)` static method generates a filled SVG polygon scaled to the given pixel dimensions.
At 100 % zoom a 20×14 marker is clearly visible; the SVG coordinate system scales
proportionally as the diagram zooms, so the arrowhead remains legible from 25 %–200 %
without additional code.

**Alternatives considered**:
- Custom SVG `<defs>`/`<marker>` injected into the DiagramCanvas host: rejected — requires
  Blazor JS interop to inject into the SVG DOM, adding async latency and a JS dependency.
- CSS `clip-path` triangle overlay: rejected — not composable with Z.Blazor.Diagrams'
  SVG rendering pipeline; breaks when the canvas zooms/pans.

---

## Decision 2 — Mid-Line Directional Accent

**Decision**: Apply a CSS `stroke-dasharray` static pattern to the `<path>` element rendered
for every `WorkflowEdgeModel`. Use a short dash (8 px) followed by a long gap (16 px) with
a small chevron character (U+203A, ›) rendered via an SVG `<textPath>` anchored at 50 %
along the path. The chevron is rotated to follow the path tangent using `textPath rotate="auto"`.

**Rationale**: The `stroke-dasharray` dash pattern is a CSS-only solution with zero JS
overhead. A mid-line `<textPath>` chevron follows the Bezier curve tangent automatically
using SVG's built-in `rotate="auto"` attribute, requiring no trigonometry in C#. This
approach works at all zoom levels because SVG user-space units scale with the viewport
transform the diagram library applies.

**Alternatives considered**:
- Animated `stroke-dashoffset` for a moving accent: deferred to execution-animation only
  (Decision 4) — a static accent is sufficient for direction legibility; animation at rest
  would distract from execution-state animations.
- Second arrowhead at mid-point: clutters short connections; rejected.

---

## Decision 3 — Input-Port Drag Rejection

**Decision**: Extend the existing `HandleLinkCreating` guard in `WorkflowCanvas.razor`.
Z.Blazor.Diagrams fires `Diagram.Links.Added` with source/target set at drag-end; however,
the port's `Alignment` property (`Left` = Input) is readable at drag-start via
`Diagram.PointerDown` on `PortModel`. When `PortModel.Alignment == PortAlignment.Left`
(input side), intercept and cancel the pending link, then set a Blazor state flag that
renders an inline amber hint banner for 3 seconds: "Connections start from the right side
(output) of a node."

**Rationale**: The library already cancels output→output and input→input connections in its
built-in `ValidateLink` delegate. Extending this delegate with a direction check keeps all
port validation in one place (single responsibility) and avoids duplicating the toast
infrastructure.

**Alternatives considered**:
- Auto-swap to nearest output port: requires finding the nearest output port geometrically,
  which involves diagram coordinate math that is brittle at non-100 % zoom — rejected.
- Silent rejection with no hint: rejected — spec FR-10.4 explicitly requires an inline tip.

---

## Decision 4 — Execution Flow Animation (Travelling Dot)

**Decision**: Represent execution flow with a CSS `@keyframes` animation named
`edge-flow-dot` applied to a `<circle>` SVG element rendered inside each active
`WorkflowEdgeModel` via a custom Blazor `LinkWidget`. The `<circle>` follows the
`<path>` using `animateMotion` (SMIL), not `stroke-dashoffset`, because `animateMotion`
naturally follows the path curve direction without coordinate math.

The animation duration is fixed at 0.8 s (`animation-duration: 0.8s`) with
`animation-iteration-count: infinite` while `IsAnimating == true` on the edge model.
When `IsAnimating` is set to `false` (run stops), the element is removed from the DOM by
a Blazor conditional render.

**Rationale**: `animateMotion` with `mpath` referencing the existing `<path>` element
is the most accurate way to travel a dot along a curved Bezier without computing parametric
curve positions in C#. Browser support is universal (all modern browsers). Duration of
0.8 s is well under the 1 s spec target and fast enough to read as "active" on connections
up to ~600 px in the viewport.

**Alternatives considered**:
- `stroke-dashoffset` animation on the link path itself: would require the dash length to
  match the path's `pathLength`, which changes when the user repositions nodes — recalculating
  on every layout change is expensive; rejected.
- JS requestAnimationFrame loop computing `getPointAtLength`: JS interop in a tight loop
  adds latency on every frame and violates the constitution's preference to minimise JS;
  rejected.

---

## Decision 5 — Right-Click Context Menu (NodeContextMenu.razor)

**Decision**: Implement `NodeContextMenu.razor` as a Blazor component that renders an
absolute-positioned `<div>` over the canvas. Position is stored as `(double X, double Y)`
in Blazor state, set when `@oncontextmenu` fires on a `WorkflowNodeRenderer` tile.
The component is conditionally rendered via `@if (isContextMenuVisible)` in `WorkflowCanvas.razor`.
Clicking "Delete node" calls `DeleteSelectedNodeAsync` on the canvas; clicking anywhere else
or pressing Escape dismisses the menu.

The context menu is keyboard-accessible: Tab moves between menu items; Enter activates; Escape
closes. This satisfies spec 003's WCAG 2.1 AA requirement for non-canvas surfaces.

**Rationale**: No JS interop needed — Blazor `@oncontextmenu` fires with `MouseEventArgs`
that include `ClientX`/`ClientY` coordinates, which are translated to canvas-relative
coordinates by subtracting the canvas container's bounding rect (read once on first render
via a minimal `IJSRuntime.InvokeAsync` call for `getBoundingClientRect`). This is the only
JS call in this feature and is cached; it does not run on every right-click.

**Alternatives considered**:
- Browser `ContextMenu` API with JS: requires passing the menu items to JS and receiving the
  selection back — overly complex, harder to style with Tailwind; rejected.
- Inline delete button always visible on the node tile: clutters the node visual; spec
  requires right-click or keyboard Delete, not a persistent button; rejected.

---

## Decision 6 — Single-Trigger Invariant Enforcement Location

**Decision**: Enforce the one-trigger-per-canvas rule at two layers:

1. **Domain layer** (`WorkflowDefinition.cs`): A `ThrowIfInvalid()` method checks
   `Nodes.Count(n => n.NodeType == WorkflowNodeType.Trigger) > 1` and throws
   `InvalidOperationException` with a plain-language message. Called by
   `WorkflowBuilderService.SaveAsync` before persisting.

2. **Canvas layer** (`WorkflowCanvas.razor`): Before adding a new node from the palette
   (`AddNodeToCanvas`), check `_diagram.Nodes.OfType<WorkflowNodeModel>()` for an existing
   Trigger. If found, cancel the drop and show an amber banner. This gives immediate feedback
   without a round-trip to the service.

**Rationale**: The domain layer check ensures the invariant is never violated in storage even
if the UI guard is bypassed (e.g., a future batch import). The canvas layer check gives the
user immediate visual feedback without waiting for a save cycle. Dual enforcement follows
the "validate at every boundary" principle.

**Alternatives considered**:
- UI-only guard: insufficient — the domain model must be self-consistent regardless of the
  UI path that created it; rejected.
- Database constraint: SQLite JSON column cannot enforce this constraint natively; rejected.

---

## Decision 7 — Node Deletion Undo Integration

**Decision**: Wrap node deletion in a new `UndoDeleteNodeCommand` record that is pushed onto
the existing `UndoRedoStack` in `WorkflowCanvas.razor`. The record stores the deleted
`WorkflowNodeModel`, its canvas position (`Point`), all its ports (`WorkflowPortModel[]`),
and all its formerly attached `WorkflowEdgeModel[]`. `Undo()` calls
`_diagram.Nodes.Add(node)`, re-attaches ports, and re-adds all edges; `Redo()` calls
`_diagram.Nodes.Remove(node)` and removes edges.

The existing undo stack has a 50-step depth — no change to the stack implementation is
needed, only a new command record type.

**Rationale**: The existing `UndoRedoStack` command pattern (already used for add-node,
move-node, delete-edge operations) is the correct extension point. Storing the complete
edge list on the command record ensures full restoration fidelity, satisfying spec
FR-11.4's requirement that "all former connections" are restored.

**Alternatives considered**:
- Immutable state snapshots (store entire diagram state on every action): memory-intensive
  at 50 steps × potentially large diagrams; rejected in favour of the command pattern
  already in use.
