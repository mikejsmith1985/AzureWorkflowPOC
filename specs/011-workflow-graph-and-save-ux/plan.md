# Implementation Plan: Per-Workflow Graph View & Trustworthy Node Editing

**Branch**: `feature/workflow-graph-and-save-ux` | **Date**: 2026-06-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/011-workflow-graph-and-save-ux/spec.md`

## Summary

Three connected changes to the Visual Workflow Builder and its surrounding navigation:

1. **Trustworthy node editing (US1)** — Make node-text edits in the right-side config panel
   actually persist, and put the commit control next to the fields. Root cause (confirmed in
   code): the config panel keeps edits in panel-local fields (`_goalPrompt`,
   `_initialDataDescription`, `_inputLabel`, `_outputLabel`) that are flushed into the workflow
   model **only** when the user clicks the panel's separate **"Done"** button. The visible
   top-toolbar **Save** (and the 60-second auto-save) serialize `_workflow`, which does **not**
   contain the panel's uncommitted edits — so a user who edits in the panel and then clicks the
   far-away toolbar Save silently loses the change. The live-preview only mirrors the Goal text into
   the canvas *Label*, and the Trigger's "What information is available at the start?" field gets no
   propagation at all. Fix: make the panel a single source of truth by writing each field edit
   through to the node model on change/blur (debounced), give the panel its own clearly-labelled
   in-panel Save affordance adjacent to the fields, and have any explicit Save flush the open panel
   first.

2. **Per-workflow Graph view, real data (US2)** — Retire the standalone, hardcoded `/graph` page
   and resurrect the diagram as a **read-only per-workflow Graph view** generated from a saved
   workflow's actual nodes and edges. Reuse the already-loaded Mermaid pipeline (`window.mermaidRender`)
   and add a small server-side generator that converts a `WorkflowDefinition` into a Mermaid
   `flowchart` (the documented gap — no existing component does this). Reach it from each gallery
   card; surface the Workflows gallery in the primary nav (it is currently unlinked).

3. **Seeded Intake Pipeline workflow (US3)** — Idempotently seed a real, persisted "Intake Pipeline"
   `WorkflowDefinition` (owner `demo`) at startup that reproduces the topology the old hardcoded
   graph documented — sources → intake → validation → branch (gap-analysis / estimation) → human
   pause → action → done — so the per-workflow Graph view has authentic data and the documented
   topology is preserved.

## Technical Context

**Language/Version**: C# 12 / .NET 8

**Primary Dependencies**: Blazor Server, Z.Blazor.Diagrams (builder canvas), Mermaid.js 10 (already
loaded via `_Host.cshtml`, invoked through `window.mermaidRender`), Entity Framework Core (SQLite),
Semantic Kernel Process Framework (execution side — untouched by this feature).

**Storage**: SQLite via `PipelineDbContext`; workflows persist as JSON blobs in the
`WorkflowDefinitions` table through `IWorkflowRepository` (`SqliteWorkflowRepository`), owner-scoped,
unique on `(OwnerId, Name)`.

**Testing**: xUnit unit tests (mocked); integration tests against a real SQLite repository;
Playwright (headless Chromium) E2E via `scripts/run-e2e.ps1` against the real Kestrel server.

**Target Platform**: Blazor Server web app (`DBAIAzure.Web`), desktop browser.

**Project Type**: Web application (single Blazor Server project + Core/Storage class libraries).

**Performance Goals**: Graph view renders within a normal page interaction (sub-second for typical
workflows of a handful of nodes); node-edit commit feels instant (no perceptible lag on keystroke,
existing 200 ms debounce reused for write-through).

**Constraints**: No change to the `WorkflowDefinition` / `WorkflowNode` / `WorkflowEdge` data model;
no change to workflow execution ("Make it real"/Run); read-only Graph view (no editing); seeding must
be idempotent and must not clobber a user-edited copy of the seeded workflow.

**Scale/Scope**: Single-tenant demo (owner `demo`); a handful of saved workflows; one new generator,
one new read-only page, one startup seeder, one nav change, and the config-panel persistence fix.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Article I (Prime Directive — BEST not fastest)**: PASS. The persistence fix removes the
  dual-source-of-truth defect rather than papering over it; the Graph view is repurposed (not
  duplicated) by reusing Mermaid and real workflow data.
- **Article IV (Code Quality)**: PASS (gate for implementation). New types carry XML docs; nullable
  honored; methods small with guard clauses; no magic numbers (node-type/label fallbacks named).
- **Article V (Testing — three-layer)**: PASS by plan. Unit: Mermaid generator (topology/labels/
  fallbacks/empty), seeder idempotency. Integration: seeded workflow persists and reloads via the
  real repository; committed node text survives a real save+reload. E2E (Playwright): edit node text
  → reload → text persists; gallery card → Graph view renders real topology; standalone `/graph`
  gone; nav exposes Workflows. Red→Green→Refactor: the US1 persistence failure gets a failing test
  first.
- **Article VI (Documentation Discipline)**: PASS. `CHANGELOG.md` updated in the PR; no ad-hoc status
  docs; only the `specs/011-*` pipeline artifacts are added.
- **Article VII (Framework-First Gate)**: PASS with one justified custom component.
  - Diagram rendering → **reuse Mermaid.js** (`window.mermaidRender`), the existing governing
    visualizer; do not introduce a new diagram engine.
  - Persistence → **reuse `IWorkflowRepository` / `WorkflowBuilderService`**; do not build a new store.
  - Node-edit write-through → **reuse Blazor data-binding + the existing 200 ms debounce**; do not
    build a bespoke change pipeline.
  - **Documented gap (custom, justified):** nothing converts a `WorkflowDefinition` into a Mermaid
    `flowchart`. `WorkflowTopologySerializer` emits LLM prose; `WorkflowThumbnailGenerator` emits a
    static mini-SVG. Neither is a labelled, auto-laid-out flowchart. A small
    `WorkflowMermaidGenerator` fills that gap and is the only new abstraction.
- **Article X (Verification & Proof)**: PASS by plan — every acceptance path is proven by a passing
  test or an observed Playwright round-trip, not by "it compiles".
- **Article XI (Output Restraint)**: PASS — no new dashboards; generated artifacts stay out of the
  committed tree.

**Result: PASS — no violations to track in Complexity Tracking.**

## Project Structure

### Documentation (this feature)

```text
specs/011-workflow-graph-and-save-ux/
├── plan.md              # This file
├── research.md          # Phase 0 output — root-cause + design decisions
├── data-model.md        # Phase 1 output — entities touched (no schema change)
├── quickstart.md        # Phase 1 output — runnable validation guide
├── contracts/           # Phase 1 output — interface/behavior contracts
│   ├── WorkflowMermaidGenerator.md
│   ├── NodeConfigPersistence.md
│   └── IntakePipelineSeeder.md
└── checklists/
    └── requirements.md  # (from /speckit-specify)
```

### Source Code (repository root)

```text
src/
├── DBAIAzure.Core/
│   ├── Models/                       # WorkflowDefinition / WorkflowNode / WorkflowEdge (UNCHANGED)
│   └── Interfaces/
│       └── IWorkflowMermaidGenerator.cs        # NEW — workflow → Mermaid flowchart contract
├── DBAIAzure.Web/
│   ├── Services/
│   │   ├── WorkflowMermaidGenerator.cs         # NEW — implements the generator (documented gap)
│   │   └── IntakePipelineSeeder.cs             # NEW — idempotent startup seed of "Intake Pipeline"
│   ├── Components/WorkflowBuilder/
│   │   └── WorkflowNodeConfigPanel.razor       # CHANGED — write-through + in-panel Save (US1)
│   ├── Pages/
│   │   ├── WorkflowBuilder.razor               # CHANGED — flush open panel on Save; live-propagate all fields (US1); wire "View graph" link (US2)
│   │   ├── WorkflowGallery.razor               # CHANGED — per-card "Graph" action (US2)
│   │   ├── WorkflowGraph.razor                 # NEW — read-only per-workflow Graph view (US2)
│   │   └── Graph.razor                         # REMOVED — hardcoded standalone graph (US2)
│   ├── Components/WorkflowBuilder/
│   │   ├── WorkflowGalleryCard.razor           # CHANGED — expose OnViewGraphRequested action (US2)
│   │   └── WorkflowToolbar.razor               # CHANGED — add "View graph" affordance (US2, FR-009)
│   ├── Shared/
│   │   └── MainLayout.razor                    # CHANGED — drop /graph link, add Workflows link (US2)
│   └── Program.cs                              # CHANGED — register generator + run seeder at startup (US2/US3)

tests/
└── DBAIAzure.Tests/
    ├── WorkflowMermaidGeneratorTests.cs        # NEW — unit (topology/labels/fallbacks/empty/disconnected)
    ├── IntakePipelineSeederTests.cs            # NEW — unit/integration (idempotency, topology)
    ├── WorkflowNodeConfigPanelTests.cs         # NEW/EXTENDED — commit write-through persistence
    └── WorkflowNodeEditPersistenceTests.cs     # NEW — integration: edit → save → load round-trip
tests/
└── DBAIAzure.E2ETests/Tests/ (Playwright, port 5099)
    ├── NodeEditPersistenceTests.cs             # NEW — edit → reload → persists
    ├── WorkflowGraphViewTests.cs               # NEW — gallery → graph renders; editing reflects; not-found
    ├── IntakePipelineSeedTests.cs              # NEW — seeded workflow in gallery + graph topology
    └── NavigationTests.cs                      # CHANGED — drop /graph test; assert Workflows link, no Graph
```

**Structure Decision**: Single Blazor Server web app with Core/Storage libraries (existing layout).
The one new domain-facing abstraction (`IWorkflowMermaidGenerator`) lives in `DBAIAzure.Core/Interfaces`
with its implementation in `DBAIAzure.Web/Services` alongside the existing `WorkflowTopologySerializer`
and `WorkflowBuilderService`, matching current conventions. The read-only Graph page joins the other
`Pages/` routes; the seeder runs from the existing post-`Build()` startup scope in `Program.cs`.

## Complexity Tracking

> No Constitution Check violations — table intentionally empty.
