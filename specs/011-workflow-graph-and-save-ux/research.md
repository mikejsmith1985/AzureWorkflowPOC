# Phase 0 Research: Per-Workflow Graph View & Trustworthy Node Editing

This feature operates entirely on an existing, well-understood codebase, so "research" here is
root-cause analysis and design-decision capture rather than external technology evaluation. No
`NEEDS CLARIFICATION` markers remained from the spec (two pivotal decisions were resolved during
`/speckit-specify`).

---

## Decision 1 — Why edited node text "isn't being saved" (US1 root cause)

**Finding (from code):** The node config panel
(`src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodeConfigPanel.razor`) holds all edits in
panel-local fields — `_goalPrompt`, `_initialDataDescription`, `_inputLabel`, `_outputLabel` — bound
with `value=@…` + `@oninput` (deliberately *not* `@bind`, to isolate typing). These locals are
flushed into the workflow model **only** inside `OnSaveAsync()` (the panel's **"Done"** button),
which fires `NodeUpdated` → `WorkflowBuilder.OnNodeUpdated` →
`WorkflowCanvas.UpdateNodeFromConfig` → `NotifyWorkflowChanged` → `_workflow` is rebuilt.

The visible top-toolbar **Save** button calls `WorkflowBuilder.TrySaveAsync()` →
`WorkflowBuilderService.SaveAsync(_workflow)`. The 60-second auto-save reads the same `_workflow`
via a getter closure and additionally **skips** when the content signature is unchanged
(`ComputeContentSignature`). Critically, `_workflow` does **not** include the panel's uncommitted
local edits until "Done" is pressed. The 200 ms live-preview (`OnGoalPreview`) only mirrors the Goal
text into the canvas node's **Label** (and only when non-empty); it never writes `GoalPrompt`, and
the Trigger's "What information is available at the start?" field is never propagated at all.

**Failure mode that matches the user's report exactly:** the user edits text in the right-side
panel, then reaches for **Save** in the top toolbar (the natural "save" affordance, far from the
panel) **without** clicking the panel's "Done". Save persists `_workflow`, which still holds the old
text; the auto-save signature is unchanged so it also won't capture the edit. Net effect: "the save
button is nowhere near the place where I'm making the update, **and** the new text still isn't being
saved" — two complaints, one root cause: **the panel is a second, un-flushed source of truth, and
the only control that commits it ("Done") is not the control the user reaches for.**

**Decision:** Eliminate the dual source of truth.
1. The panel writes each field edit **through to the node model** on change/blur (reusing the
   existing 200 ms debounce), so `_workflow` always reflects what is on screen — for **all** fields,
   including the Trigger's initial-data field, not just Goal→Label.
2. The panel gets its **own clearly-labelled Save affordance adjacent to the fields** (the commit
   the user reaches for is where the edit happens). It both commits and triggers a real persist so
   the user gets immediate durable confirmation rather than waiting up to 60 s for auto-save.
3. As a belt-and-braces guarantee, an explicit **toolbar Save flushes any open panel first** before
   serializing, so no path can persist stale node text.
4. If a persist fails, the user is told (toast), never shown success over lost data (FR-012).

**Rationale:** A single source of truth is the BEST fix (Article I); it is achieved with Blazor
data-binding and the existing debounce (Article VII framework-first — no new machinery), and it
directly satisfies FR-001/FR-002/FR-003/FR-004/FR-012.

**Alternatives considered:**
- *Keep "Done" as the only commit, just move the button* — rejected: leaves the dual source of truth
  and the auto-save blind spot; the text-loss bug would persist whenever the user uses any other
  save path.
- *Auto-commit on panel close only* — rejected: closing via the ✕ currently discards; making close
  commit is surprising and still leaves toolbar Save able to race ahead of an open edit.
- *`@bind` the fields straight to the node record* — rejected: the panel deliberately isolates
  typing to avoid the canvas snapping the value back mid-keystroke (the spec 006 class of bug);
  write-through-on-change with the existing debounce preserves that isolation while keeping the
  model current.

---

## Decision 2 — Per-workflow Graph view from real data (US2)

**Finding (from code):** `src/DBAIAzure.Web/Pages/Graph.razor` builds a 100% hardcoded Mermaid
string (`BuildFullTopology`) describing the backend `IntakePipeline` and renders it via the existing
`window.mermaidRender(containerId, definition)` JS helper (defined in `Pages/_Host.cshtml`, Mermaid
10 loaded from CDN). It is linked in `Shared/MainLayout.razor` (`/graph`). The Workflows gallery
(`Pages/WorkflowGallery.razor`, `/workflow-gallery`) already lists saved workflows per owner but is
**not** linked in the primary nav.

**Decision:** Remove `Graph.razor` and its nav link; add a **Workflows** nav link. Add a new
read-only `WorkflowGraph.razor` page (e.g. route `/workflow-graph/{Id:guid}`) that loads the
workflow via `IWorkflowRepository`/`WorkflowBuilderService`, generates a Mermaid `flowchart` from its
real nodes and edges with a new `IWorkflowMermaidGenerator`, and renders it with the existing
`window.mermaidRender`. Each gallery card gains a **Graph** action linking to that page. The page is
read-only and offers an "Open in builder" link (round-trip per FR-009).

**Rationale:** Reuses the governing Mermaid visualizer and the existing persistence (Article VII);
makes every diagram reflect real, current data (FR-007); preserves the valuable read-only,
auto-laid-out artifact the user wanted to keep, folded into the Workflows tab exactly as agreed.

**Mermaid generation specifics (the documented gap):**
- Emit `flowchart LR`. Each node becomes `n<id>["<label>"]` where `<label>` is the node's `Label`
  with the same fallback the canvas uses for an unnamed node (never a blank box, per FR-011), and is
  Mermaid-escaped (quotes/newlines/reserved chars). Node shape may vary by `WorkflowNodeType`
  (e.g. stadium for Trigger, rhombus for `FunctionRoute` branch) for readability, but that is
  cosmetic.
- Each `WorkflowEdge` becomes `n<src> -->|"<edgeLabel>"| n<tgt>`, edge label escaped; an empty edge
  label renders as a plain arrow.
- Disconnected nodes are still emitted as standalone nodes (FR-011) — never dropped.
- Empty workflow → the page shows a clear empty-state message instead of invoking the renderer.
- The generator is pure/deterministic (no I/O), so it is unit-testable in milliseconds (Article V).

**Alternatives considered:**
- *Reuse `WorkflowThumbnailGenerator` SVG* — rejected: it produces a tiny static preview thumbnail,
  not a labelled, legible, auto-laid-out topology; wrong altitude for a full Graph view.
- *Reuse `WorkflowTopologySerializer`* — rejected: it emits LLM prose, not a diagram. (It does share
  the node-by-id/edge-label pattern; the new generator mirrors that logic but targets Mermaid. A
  later refactor could share a lookup helper; not required now.)
- *Render the diagram inside the builder canvas read-only* — rejected: the canvas is an editing
  surface (ports, drag handles); a clean Mermaid diagram is the better read-only artifact and is
  what the user asked to resurrect.

---

## Decision 3 — Seeding the real "Intake Pipeline" workflow (US3)

**Finding (from code):** `Program.cs` already runs a post-`Build()` startup scope that calls
`db.Database.EnsureCreatedAsync()` and idempotent `CREATE TABLE IF NOT EXISTS` statements. Example
workflows are currently built in-memory only (`WorkflowBuilder.BuildExampleWorkflow`) and are not
persisted unless the user saves. `IWorkflowRepository` enforces `(OwnerId, Name)` uniqueness.

**Decision:** Add an idempotent `IntakePipelineSeeder` invoked from the existing startup scope.
On startup it checks whether owner `demo` already has a workflow named "Intake Pipeline"
(via `ListByOwnerAsync`); if absent, it builds and saves a `WorkflowDefinition` reproducing the
former hardcoded topology, then returns. If present, it does nothing — so a user who later edits the
seeded workflow is never overwritten (FR-010 + the "idempotent re-seed" assumption).

**Topology mapping (hardcoded graph → real node types):**
| Graph node            | Seeded node type            | Notes                                            |
|-----------------------|-----------------------------|--------------------------------------------------|
| SNow / Manual sources | `Trigger`                   | One trigger "Ticket received (ServiceNow/Manual)"|
| Intake                | `AgenticReason`             | Normalise ticket fields                          |
| Validation            | `AgenticReason`             | Evaluate Definition-of-Ready; re-run after HITL  |
| (Validation branch)   | `FunctionRoute`             | ReadyPath / NotReadyPath / Blocked routing       |
| GapAnalysis           | `AgenticReason`             | Generate clarifying questions                    |
| HitlPause / PO        | `HumanApproval`             | The human-in-the-loop pause                      |
| Estimation            | `AgenticReason`             | Assign story points                              |
| Action / Done         | `FunctionNotify` (terminal) | Create the ADO/Jira issue                        |

Edges reproduce the documented routing, including the **validation branch** and the **PO →
Validation** re-entry, and are labelled with the original event names (TicketReceived,
IntakeComplete, ReadyPath, NotReadyPath, QuestionsReady, HumanResponded, EstimationComplete). The
goal is **structural fidelity**, not a pixel-identical layout (spec assumption).

**Rationale:** Gives US2 authentic data and preserves the institutional knowledge the hardcoded
graph captured, using the existing repository and startup hook (Article VII). Idempotency protects
user edits and makes restarts safe.

**Alternatives considered:**
- *Seed via EF migration / SQL insert* — rejected: bypasses validation and the domain model; the
  repository path is the supported, testable route.
- *Generate the seed lazily on first gallery view* — rejected: startup seeding is simpler, runs once,
  and keeps the gallery page free of seeding concerns.
- *Reuse the in-memory `BuildExampleWorkflow`* — rejected: that is the 4-node support-request demo,
  a different topology; the Intake Pipeline is its own seeded workflow.

---

## Cross-cutting notes

- **No data-model change.** `WorkflowDefinition` / `WorkflowNode` / `WorkflowEdge` are sufficient;
  this feature only reads them (Graph view, seeding) and fixes how the panel writes them (US1).
- **Execution untouched.** Nothing here changes "Make it real", Run, readiness, or the SK process.
- **Testing reuse.** The existing `WorkflowBuilderServiceTests` / `WorkflowThumbnailGeneratorTests`
  patterns and the Playwright `WebAppFixture` (port 5099) are the templates for the new tests.
