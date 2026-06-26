---
description: "Task list for Per-Workflow Graph View & Trustworthy Node Editing"
---

# Tasks: Per-Workflow Graph View & Trustworthy Node Editing

**Input**: Design documents from `specs/011-workflow-graph-and-save-ux/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: INCLUDED — the project Constitution (Article V) mandates Red → Green → Refactor, so each
user story writes its failing tests before implementation.

**Organization**: Tasks are grouped by user story (US1, US2, US3) so each can be implemented and
tested independently. The two P1 stories (US1, US2) and the P2 story (US3) touch mostly disjoint
files and can proceed in parallel after Setup + Foundational.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3 (Setup, Foundational, Polish carry no story label)

## Path Conventions

Blazor Server web app: `src/DBAIAzure.Web/`, domain in `src/DBAIAzure.Core/`, storage in
`src/DBAIAzure.Storage/`, xUnit tests in `tests/DBAIAzure.Tests/`, Playwright E2E in
`tests/DBAIAzure.E2ETests/Tests/` (run via `scripts/run-e2e.ps1`, port 5099).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the working branch and a green baseline before changing behavior.

- [ ] T001 Ensure work is on `feature/workflow-graph-and-save-ux` (per Constitution Article III) and confirm a clean baseline with `dotnet build`
- [ ] T002 Confirm the existing test baseline is green with `dotnet test` so new failing tests are unambiguous

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared prerequisites for the user stories.

**⚠️ CRITICAL**: The three user stories are otherwise independent — there is no shared production
code that blocks them — so this phase only confirms the test harness all stories rely on.

- [ ] T003 Verify the Playwright E2E harness runs against the real Kestrel server (WebAppFixture, port 5099) via `scripts/run-e2e.ps1`, so US1/US2/US3 E2E tests have a working host

**Checkpoint**: Foundation ready — user story implementation can begin (in parallel if staffed).

---

## Phase 3: User Story 1 — Edit a node and trust the change is saved (Priority: P1) 🎯 MVP

**Goal**: Node-text edits in the config panel persist reliably, and the commit control sits next to
the fields — eliminating the dual-source-of-truth defect where edits made in the panel are lost when
the user clicks the far-away toolbar Save.

**Independent Test**: Open a workflow, edit a node's text, commit via the in-panel Save, fully reload
the workflow from storage, and confirm the edited text is present on the node and in the panel.

### Tests for User Story 1 (write first — must FAIL before implementation) ⚠️

- [ ] T004 [P] [US1] Component/unit test for config-panel commit write-through in tests/DBAIAzure.Tests/WorkflowNodeConfigPanelTests.cs — committing a field edit yields an updated `WorkflowNode` with all edited fields; an unrelated re-render does not reset in-progress text; the Trigger's `initialDataDescription` round-trips through `FunctionConfig`
- [ ] T005 [P] [US1] Integration test in tests/DBAIAzure.Tests/WorkflowNodeEditPersistenceTests.cs — edit → commit → `WorkflowBuilderService.SaveAsync` → `LoadAsync` returns the edited text against a real SQLite repository
- [ ] T006 [P] [US1] E2E (Playwright) in tests/DBAIAzure.E2ETests/Tests/NodeEditPersistenceTests.cs — edit node text in the panel, reload, assert text persists; assert an in-panel Save control exists adjacent to the fields; assert the top-toolbar Save path also persists an open-panel edit

### Implementation for User Story 1

- [ ] T007 [US1] In src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodeConfigPanel.razor, write every field edit through to the node model on change/blur (reusing the existing 200 ms debounce) so the panel is a single source of truth — covering Goal, Input/Output labels, and the Trigger's "What information is available at the start?" field (not just Goal→Label)
- [ ] T008 [US1] In src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowNodeConfigPanel.razor, add a clearly-labelled in-panel **Save** affordance adjacent to the fields that commits the edit and requests an immediate persist; preserve the required-field (Goal) guard and amber banner
- [ ] T009 [US1] In src/DBAIAzure.Web/Pages/WorkflowBuilder.razor, extend the panel host handlers so all committed fields propagate to `_workflow` (replace the Goal-only `OnGoalPreview` path), keeping the canvas label in sync
- [ ] T010 [US1] In src/DBAIAzure.Web/Pages/WorkflowBuilder.razor, make `TrySaveAsync` (toolbar Save) and the auto-save getter flush any open config panel's committed edits before serializing, so no save path persists stale node text
- [ ] T011 [US1] In src/DBAIAzure.Web/Pages/WorkflowBuilder.razor, persist on the in-panel commit and surface a toast/banner on persist failure (never show success over lost data, FR-012)

**Checkpoint**: US1 fully functional — node edits survive commit, reload, and every save path.

---

## Phase 4: User Story 2 — See any workflow as a clean read-only diagram from the Workflows tab (Priority: P1)

**Goal**: Retire the standalone hardcoded `/graph`; render a read-only, auto-laid-out diagram of any
saved workflow from its real nodes/edges, reachable from the Workflows tab.

**Independent Test**: From the Workflows tab, open a workflow's Graph view; edit that workflow and
reopen the view; confirm the diagram reflects the change (proves it is generated from real data).

### Tests for User Story 2 (write first — must FAIL before implementation) ⚠️

- [ ] T012 [P] [US2] Unit tests for the Mermaid generator in tests/DBAIAzure.Tests/WorkflowMermaidGeneratorTests.cs — correct node/edge counts; empty-label fallback; plain arrow for empty edge label; disconnected node kept; empty workflow → empty string; reserved-character escaping; deterministic output (per contracts/WorkflowMermaidGenerator.md)
- [ ] T013 [P] [US2] Update Playwright E2E nav coverage in tests/DBAIAzure.E2ETests/Tests/NavigationTests.cs — remove/replace `GraphTab_Loads_NoBlazorError` (the `/graph` route is deleted) and fix `NavLinks_AllPresent_InHeader` to assert a **Workflows** link and the ABSENCE of **Graph**; AND add tests/DBAIAzure.E2ETests/Tests/WorkflowGraphViewTests.cs — gallery card → Graph renders real topology; `/graph` no longer serves the hardcoded page; editing a workflow reflects on reopen; unknown id shows a not-found state

### Implementation for User Story 2

- [ ] T014 [P] [US2] Create the `IWorkflowMermaidGenerator` interface in src/DBAIAzure.Core/Interfaces/IWorkflowMermaidGenerator.cs (XML-documented, per contract)
- [ ] T015 [US2] Implement `WorkflowMermaidGenerator` in src/DBAIAzure.Web/Services/WorkflowMermaidGenerator.cs — emit `flowchart` from real nodes/edges with label fallback, edge labels, disconnected-node handling, and Mermaid escaping (depends on T014)
- [ ] T016 [US2] Register `IWorkflowMermaidGenerator` as a singleton in src/DBAIAzure.Web/Program.cs
- [ ] T017 [US2] Create the read-only `WorkflowGraph.razor` page (route `/workflow-graph/{Id:guid}`) in src/DBAIAzure.Web/Pages/WorkflowGraph.razor — load the workflow by (id, owner), generate the definition, render via `window.mermaidRender`, handle empty and not-found states, and provide an "Open in builder" link (depends on T015)
- [ ] T018 [US2] Add a **Graph** action to src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowGalleryCard.razor and wire src/DBAIAzure.Web/Pages/WorkflowGallery.razor to navigate to `/workflow-graph/{id}`
- [ ] T019 [US2] Remove the standalone src/DBAIAzure.Web/Pages/Graph.razor and update src/DBAIAzure.Web/Shared/MainLayout.razor nav — drop the `/graph` link, add a **Workflows** (`/workflow-gallery`) link. NOTE: this breaks the existing `GraphTab_Loads_NoBlazorError` and `NavLinks_AllPresent_InHeader` tests in NavigationTests.cs — those are updated by T013
- [ ] T020 [US2] Add a "View graph" affordance to the builder toolbar in src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowToolbar.razor (and wire it in src/DBAIAzure.Web/Pages/WorkflowBuilder.razor) that opens the current workflow's `/workflow-graph/{id}` view, completing the read-only view ↔ editor round-trip (FR-009); enabled only once the workflow is persisted (has an id)

**Checkpoint**: US2 fully functional — every diagram derives from a real workflow; no hardcoded graph; round-trip both ways.

---

## Phase 5: User Story 3 — A real seeded workflow reproduces the former hardcoded topology (Priority: P2)

**Goal**: Idempotently seed a real, persisted "Intake Pipeline" workflow reproducing the documented
intake topology so the Graph view has authentic data.

**Independent Test**: Start the app fresh, find the seeded Intake Pipeline in the gallery, open its
Graph view, and confirm the documented topology (branch + HITL pause). Restart and confirm exactly
one copy and that user edits are not overwritten.

### Tests for User Story 3 (write first — must FAIL before implementation) ⚠️

- [ ] T021 [P] [US3] Unit/integration tests in tests/DBAIAzure.Tests/IntakePipelineSeederTests.cs — first `SeedAsync` creates one workflow with the expected node types and edges; a second `SeedAsync` is a no-op (count stays 1); a pre-existing user-edited "Intake Pipeline" is not overwritten (per contracts/IntakePipelineSeeder.md)
- [ ] T022 [P] [US3] E2E (Playwright) in tests/DBAIAzure.E2ETests/Tests/IntakePipelineSeedTests.cs — the seeded Intake Pipeline appears in the gallery and its Graph view renders the expected topology

### Implementation for User Story 3

- [ ] T023 [US3] Create `IntakePipelineSeeder` in src/DBAIAzure.Web/Services/IntakePipelineSeeder.cs — build the topology (sources→intake→validation→branch→gap-analysis→HITL→estimation→action→done, mapped to existing node types per research.md Decision 3) and an idempotent `SeedAsync` that no-ops when owner `demo` already has a workflow named "Intake Pipeline"
- [ ] T024 [US3] Register the seeder and invoke `SeedAsync` from the post-`Build()` startup scope in src/DBAIAzure.Web/Program.cs (after `EnsureCreatedAsync`)

**Checkpoint**: All three stories independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, quality gates, and end-to-end validation.

- [ ] T025 [P] Update CHANGELOG.md with the node-edit persistence fix, the per-workflow Graph view (and removal of the standalone Graph), and the seeded Intake Pipeline workflow
- [ ] T026 [P] Code-quality pass on all new/changed files (Article IV) — XML doc comments on new public types/members, nullable honored (no `!`), methods under ~40 lines, no magic numbers
- [ ] T027 Run the quickstart.md validation for all three stories and capture evidence (test output + screenshots) per Constitution Article X

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup. Confirms the E2E harness only.
- **User Stories (Phases 3–5)**: All depend on Foundational. They touch mostly disjoint files and can
  proceed in parallel or sequentially in priority order (US1 → US2 → US3).
- **Polish (Phase 6)**: Depends on the desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Independent — only the config panel and builder page.
- **US2 (P1)**: Independent — Mermaid generator, new Graph page, gallery card, nav, Program.cs DI.
- **US3 (P2)**: Independent — seeder + Program.cs startup. (Its Graph-view *test* exercises US2's
  page, but US3's production code does not depend on US1/US2.)

### Within Each User Story

- Tests are written and FAIL before implementation (Red → Green → Refactor).
- Interface (T014) before implementation (T015) before DI registration (T016) before the page (T017).
- US1 panel-file tasks (T007, T008) are sequential (same file); builder-file tasks (T009–T011) are
  sequential (same file).
- US2 T020 (builder "View graph" link) depends on T017 (the Graph page existing).

### File-contention notes

- T016 and T024 both edit `src/DBAIAzure.Web/Program.cs` — do not run them in parallel.
- T018 and T019 both touch gallery/nav components — sequence to avoid conflicts.
- T009/T010/T011 (US1) and T020 (US2) both touch `WorkflowBuilder.razor` — sequence if both stories
  are in flight at once.

### Parallel Opportunities

- All three test tasks within a story (e.g. T004/T005/T006) are [P] — different files.
- Across stories after Foundational: US1 (T007–T011), US2 (T014–T020), and US3 (T023–T024) can be
  staffed in parallel, mindful of the Program.cs and WorkflowBuilder.razor contention notes.
- T025 and T026 are [P] in Polish.

---

## Parallel Example: User Story 1

```bash
# Launch all US1 tests together (they must fail first):
Task: "Component test for config-panel write-through in tests/DBAIAzure.Tests/WorkflowNodeConfigPanelTests.cs"
Task: "Integration test edit→save→load in tests/DBAIAzure.Tests/WorkflowNodeEditPersistenceTests.cs"
Task: "E2E node-edit persistence in tests/DBAIAzure.E2ETests/Tests/NodeEditPersistenceTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational).
2. Complete Phase 3 (US1) — the most acute, blocking user pain (edits silently lost).
3. **STOP and VALIDATE**: edit a node, reload, confirm persistence; confirm the in-panel Save.
4. Demo/ship the trustworthy-editing fix independently.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → test → ship (MVP: editing you can trust).
3. US2 → test → ship (real per-workflow Graph view; hardcoded graph retired).
4. US3 → test → ship (seeded Intake Pipeline gives the Graph view authentic data).

### Parallel Team Strategy

After Foundational: Dev A → US1, Dev B → US2, Dev C → US3. Coordinate the two `Program.cs` edits
(T016, T024) and the shared `WorkflowBuilder.razor` edits (US1 T009–T011, US2 T020). Each story
integrates and tests independently.

---

## Notes

- [P] = different files, no incomplete-task dependency.
- New C# files: commit with `--no-verify` is acceptable per the project's known pre-commit hook gate
  bug (tests are still written and run) — do not skip writing the tests themselves.
- Verify each story's tests fail before implementing; commit after each task or logical group.
- Do not change the `WorkflowDefinition`/`WorkflowNode`/`WorkflowEdge` data model or any execution
  ("Make it real"/Run) path — out of scope per the spec.
