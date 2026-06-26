---
description: "Task list for Admin Console UI redesign + graph folded into the Workflow Builder"
---

# Tasks: Admin Console Look-and-Feel Redesign + Graph Folded Into the Workflow Builder

**Input**: Design documents from `specs/014-admin-console-ui-redesign/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md (all present)

**Tests**: **INCLUDED** — Constitution Article V mandates a Playwright E2E test for every navigation
tab and key interactive element before a feature is shippable, and spec SC-008 requires automated
navigation/UX tests. Follow Red → Green: write the failing test first, then implement.

**Organization**: Tasks are grouped by the six user stories so each can be implemented and verified
independently. Stack (from plan.md): Blazor Server (.NET 8), Tailwind via CDN, Z.Blazor.Diagrams,
existing `localStorage` interop; Playwright E2E via `scripts/run-e2e.ps1` (WebAppFixture @ :5099);
xUnit unit tests. No new NuGet packages, no DB changes.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: US1–US6 (Setup/Foundational/Polish carry no story label)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Scaffolding the redesign needs before any shell work.

- [ ] T001 [P] Create `design-tokens.css` at `src/DBAIAzure.Web/wwwroot/css/design-tokens.css` (empty `:root {}` scaffold) and link it in `src/DBAIAzure.Web/Pages/_Host.cshtml` immediately after the Tailwind CDN script.
- [ ] T002 Add a runtime `tailwind.config` `<script>` block in `src/DBAIAzure.Web/Pages/_Host.cshtml` mapping semantic colour names (`app`, `surface`, `surface-raised`, `border-default`, `default`, `muted`, `accent`, `on-accent`, `status-ok|warn|error`) to `var(--…)` per `contracts/theming-tokens.md`.
- [ ] T003 [P] Create folders `src/DBAIAzure.Web/Shared/Shell/` and `src/DBAIAzure.Web/Navigation/` (add a `.gitkeep` or first file) for the new shell components and nav model.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared building blocks every story renders on. **⚠️ No story work begins until this phase is complete.**

- [ ] T004 [P] Define the dark-theme semantic tokens (all variables in `contracts/theming-tokens.md`, including `--text-scale` and `--focus-ring`) in `src/DBAIAzure.Web/wwwroot/css/design-tokens.css`.
- [ ] T005 [P] Create the navigation model in `src/DBAIAzure.Web/Navigation/NavModel.cs` — `NavSection`/`NavSubView` records plus the five sections + separated User Guide and their sub-view→route map per `contracts/navigation-ia.md` (XML doc comments per Article IV).
- [ ] T006 [P] Write FAILING unit tests for the preferences service in `tests/DBAIAzure.Tests/UiPreferenceServiceTests.cs` (mock `IJSRuntime`: default text size = Normal, default assistant open; read/write of `ui.textSize` and `ui.assistantOpen`; graceful default when storage throws).
- [ ] T007 Implement `UiPreferenceService` in `src/DBAIAzure.Web/Services/UiPreferenceService.cs` using the existing `localStorageGet`/`localStorageSet` interop; register it in `src/DBAIAzure.Web/Program.cs` DI. Make T006 pass.
- [ ] T008 Create the `AppShell.razor` skeleton in `src/DBAIAzure.Web/Shared/AppShell.razor` with named regions (sidebar / top bar / content / right rail) using token classes — not yet wired as default layout.

**Checkpoint**: Tokens, nav model, preferences service, and shell skeleton exist.

---

## Phase 3: User Story 1 — A consistent console shell on every screen (Priority: P1) 🎯 MVP

**Goal**: One persistent shell (sidebar + top bar + collapsible right rail) on every screen, with the active section highlighted and the Builder rendering inside it.

**Independent Test**: Load three destinations; the shell persists, exactly one section is highlighted, the sidebar collapses on a narrow viewport, and the Builder shows the shell.

- [ ] T009 [P] [US1] Write FAILING E2E `tests/DBAIAzure.E2ETests/Tests/ShellNavigationTests.cs` (mirror `AppsPageTests` conventions): sidebar + top bar + rail present across `/`, `/apps`, `/workflow-builder`; one active section; no full-page reload between nav; **assert the shell at three viewport widths — 1280px (laptop), 1024px (small laptop), and 768px (tablet)** — with every destination reachable and no main-frame horizontal scroll at any of them (sidebar collapses to a rail/toggle at the narrow widths); shell controls keyboard-focusable. (C-SHELL-1..6, SC-007)
- [ ] T010 [US1] Implement `src/DBAIAzure.Web/Shared/Shell/SidebarNav.razor` — brand block ("Admin Console / Control Plane"), primary sections from `NavModel` (icon+label), separated User Guide entry, version footer, accent active-state for the current route (FR-001/FR-002).
- [ ] T011 [US1] Implement `src/DBAIAzure.Web/Shared/Shell/TopBar.razor` — section/page title + placeholder slots for the text-size control and connection indicator (filled in US5) (FR-003).
- [ ] T012 [US1] Implement a minimal `src/DBAIAzure.Web/Shared/AssistantPanel.razor` rail placeholder (header + collapsed/expanded container; full behaviour in US4) so the shell has its third region.
- [ ] T013 [US1] Compose `AppShell.razor` (sidebar + top bar + `@Body` content + rail; responsive sidebar collapse; `prefers-reduced-motion` handling; visible focus ring) and set `DefaultLayout="typeof(AppShell)"` in `src/DBAIAzure.Web/App.razor`; move the `OnboardingBanner` + field-tooltip portal out of `MainLayout` into `AppShell` and retire `MainLayout.razor`.
- [ ] T014 [US1] Reconcile `src/DBAIAzure.Web/Shared/WorkflowBuilderLayout.razor` / `Pages/WorkflowBuilder.razor` so the Builder renders inside the `AppShell` content region (full-width canvas) instead of bypassing the shell (C-SHELL-6).
- [ ] T015 [US1] Run `scripts/run-e2e.ps1`; make `ShellNavigationTests` pass (add `data-testid`s as needed).

**Checkpoint**: The new shell is live on every screen — MVP demoable.

---

## Phase 4: User Story 2 — The Graph lives inside the Workflow Builder (Priority: P1)

**Goal**: Retire the standalone Graph + its fixed intake diagram; `/graph` redirects to the Builder, which shows the loaded workflow's own graph. *(Independent of US1 — route/page changes.)*

**Independent Test**: No Graph sidebar entry; `/graph` lands on the Builder (no 404); the Builder shows the loaded workflow's nodes/edges; the fixed topology appears nowhere.

- [ ] T016 [P] [US2] Write FAILING E2E `tests/DBAIAzure.E2ETests/Tests/GraphRedirectTests.cs`: sidebar has no "Graph" entry; visiting `/graph` ends on `/workflow-builder`; loading a saved workflow shows its own nodes/edges; the old fixed intake diagram is absent. (C-NAV-3/4)
- [ ] T017 [US2] Create `src/DBAIAzure.Web/Pages/GraphRedirect.razor` at route `/graph` that navigates to `/workflow-builder` (FR-009).
- [ ] T018 [US2] Delete `src/DBAIAzure.Web/Pages/Graph.razor` and remove its `NavModel`/sidebar reference; confirm the Mermaid CDN include and `window.mermaidRender` in `_Host.cshtml` REMAIN (still used by `Pages/RunDetail.razor`).
- [ ] T019 [US2] Confirm/expose the Builder's loaded-workflow graph view (Z.Blazor.Diagrams already renders it); add an in-Builder graph affordance if not already obvious. Make `GraphRedirectTests` pass.

**Checkpoint**: Graph folded in; old route safe.

---

## Phase 5: User Story 3 — Sections with sub-tabs replace the flat top nav (Priority: P2)

**Goal**: Related screens grouped under sections; sub-tabs within a section; every old screen re-homed (no orphans). *(Depends on US1 shell.)*

**Independent Test**: Each section exposes its sub-views as sub-tabs with correct active state; every pre-redesign route resolves under a section.

- [ ] T020 [P] [US3] Write FAILING E2E `tests/DBAIAzure.E2ETests/Tests/SectionTabsTests.cs`: Automation section shows Workflow Builder + Gallery sub-tabs with active state tracking the route; a route inventory asserts every pre-redesign route renders (no 404/orphan). (C-NAV-1/2)
- [ ] T021 [US3] Implement `src/DBAIAzure.Web/Shared/Shell/SectionTabs.razor` rendering the active section's sub-views from `NavModel`, with active sub-tab styling (FR-011).
- [ ] T022 [US3] Wire `SectionTabs` into the `AppShell` content header and compute active section/sub-view from the current URI (use `MatchPrefix` for detail routes like `/apps/{id}`, `/runs/{id}`, `/run/{id}`).
- [ ] T023 [US3] Audit every pre-redesign route against `NavModel`; update inter-page links to the grouped IA; make `SectionTabsTests` pass (FR-012/SC-003).

**Checkpoint**: Grouped IA complete; nothing orphaned.

---

## Phase 6: User Story 4 — A persistent, collapsible Assistant panel (Priority: P2)

**Goal**: The right rail shows the Assistant chrome everywhere, hosts the existing Builder chat panel in the Builder, collapses/persists. *(Depends on US1.)*

**Independent Test**: Panel chrome present on a non-Builder screen; collapse reflows content and persists across nav+reload; the Builder chat still generates/diffs/saves.

- [ ] T024 [P] [US4] Write FAILING E2E `tests/DBAIAzure.E2ETests/Tests/AssistantPanelTests.cs`: header + intro + chips + input present on a non-Builder screen; collapse → content reflows → reopen; open/closed persists across navigation and reload; in the Builder the existing chat panel still works. (C-AP-1..4)
- [ ] T025 [US4] Build the `AssistantPanel.razor` chrome (header with collapse/expand/close, intro text, suggestion chips, message input + send) per `contracts/assistant-panel.md`; bind open/closed to `UiPreferenceService` (FR-013/FR-014).
- [ ] T026 [US4] Host the existing `Components/WorkflowBuilder/WorkflowChatPanel.razor` inside `AssistantPanel` when on the Builder (pass through its current parameters/callbacks unchanged) and remove the builder-embedded copy (FR-015 / C-AP-4).
- [ ] T027 [US4] Implement content reflow on collapse + persistence; make `AssistantPanelTests` pass.

**Checkpoint**: Assistant rail is shell-wide chrome; Builder behaviour preserved; seam ready for feature 015.

---

## Phase 7: User Story 6 — An in-app User Guide section (Priority: P2)

**Goal**: A User Guide destination documenting every section and key task (also the content feature 015 will ground the AI on). *(Depends on US1.)*

**Independent Test**: User Guide reachable from the sidebar and documents each primary section and key task.

- [ ] T028 [P] [US6] Write FAILING E2E `tests/DBAIAzure.E2ETests/Tests/UserGuideTests.cs`: User Guide reachable from the sidebar; page documents each primary section + key task headings. (SC-009)
- [ ] T029 [US6] Create `src/DBAIAzure.Web/Pages/UserGuide.razor` at `/user-guide` with human-readable documentation covering what the app is and each section/key task; ensure the `NavModel` User Guide entry points to it (FR-016).
- [ ] T030 [US6] Style the guide with the shell/token visual language; make `UserGuideTests` pass.

**Checkpoint**: User Guide live (and ready for feature 015 to ground the AI on).

---

## Phase 8: User Story 5 — Text size and a clear connection indicator (Priority: P3)

**Goal**: Top-bar text-size control (persisted) and a connection/host indicator. *(Depends on US1 top bar.)*

**Independent Test**: Text size scales content without breaking layout and persists across reload; the connection indicator shows connected vs disconnected with the host.

- [ ] T031 [P] [US5] Write FAILING E2E `tests/DBAIAzure.E2ETests/Tests/TextSizeAndConnectionTests.cs`: changing text size rescales content and survives reload; the connection indicator reflects connected state and host. (FR-018/019/020)
- [ ] T032 [US5] Implement the text-size control in `TopBar.razor` bound to `UiPreferenceService`, driving the `--text-scale` root variable; apply the stored value on first interactive render.
- [ ] T033 [US5] Implement `src/DBAIAzure.Web/Shared/Shell/ConnectionIndicator.razor` surfacing Blazor circuit state (the existing `#components-reconnect-modal`) plus the configured host, and place it in `TopBar.razor` (FR-019).
- [ ] T034 [US5] Make `TextSizeAndConnectionTests` pass.

**Checkpoint**: Top-bar controls complete.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T035 [P] Restyle remaining pages to semantic token classes (remove raw `gray-950`/`cyan-400` etc.): `Index`, `RunHistory`, `RunHistoryDetail`, `RunDetail`, `ReviewQueue`, `NewTicket`, `WorkflowGallery`, `Apps`, `AppDetail`, `ConnectorSettings` (FR-021).
- [ ] T036 [P] Apply the consistent "nothing here yet" empty-state treatment across lists/panels (FR-022).
- [ ] T037 Verify SC-005: grep the redesigned `.razor` files for raw palette utilities (`gray-`, `cyan-`, etc.) and confirm none remain outside the token layer.
- [ ] T038 [P] Update existing per-page E2E tests for the new routes/IA/selectors (e.g., `NavigationTests`, `WorkflowBuilderTests`, `ReviewQueueTests`, `RunHistoryTests`, `AppsPageTests`), **and ensure each re-homed screen's existing functional flow is still asserted — not just navigation/selectors — so FR-023 (preserve behaviour & data) is actively verified, not merely accommodated by loosening assertions.**
- [ ] T039 Reduced-motion + keyboard-focus pass across all shell controls (FR-024/FR-025).
- [ ] T040 [P] Code-quality pass (Article IV): ensure every new public component/service (`AppShell`, `SidebarNav`, `TopBar`, `SectionTabs`, `ConnectionIndicator`, `AssistantPanel`, `UiPreferenceService`, `NavModel`) carries an XML doc comment explaining the "why", booleans read as predicates, and methods stay focused (<40 lines, guard clauses).
- [ ] T041 Update `CHANGELOG.md` `[Unreleased]` with the redesign entry (Article VI).
- [ ] T042 Run `scripts/run-e2e.ps1` + `dotnet test tests/DBAIAzure.Tests`; execute `quickstart.md` steps 1–9; fix any fallout. (Final validation — run last.)

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (P1)** → no deps.
- **Foundational (P2)** → after Setup; **blocks all stories**.
- **US1 (P1)** → after Foundational. The shell other stories render in.
- **US2 (P1)** → after Foundational; **independent of US1** (route/page changes) — can run in parallel with US1.
- **US3, US4, US6 (P2)** → after **US1** (render inside the shell).
- **US5 (P3)** → after **US1** (top bar must exist).
- **Polish (P9)** → after all desired stories.

### Story independence notes

- US2 is independent of the shell and can be delivered first or in parallel.
- US3/US4/US5/US6 each depend only on US1's shell, not on each other — they can proceed in parallel once US1 lands.

### Within each story

Failing test → implementation → make green. Components before composition; composition before route-wiring.

## Parallel Opportunities

- **Setup**: T001 ∥ T003.
- **Foundational**: T004 ∥ T005 ∥ T006 (different files); T007 after T006; T008 after T004.
- **After US1 lands**: US3, US4, US5, US6 can be worked in parallel (each touches its own components/tests). US2 can run alongside US1 from the start.
- **Per story**: the `[P]` test task is authored first, in parallel with reading the relevant contract.

## Parallel Example: kickoff after Foundational

```text
# Two P1 tracks at once:
Track A (US1 shell):  T009 (failing E2E) → T010, T011, T012 → T013 → T014 → T015
Track B (US2 graph):  T016 (failing E2E) → T017 → T018 → T019
# Then fan out P2/P3 (US3, US4, US6, US5) onto the shell from Track A.
```

## Implementation Strategy

### MVP first

1. Phase 1 Setup → Phase 2 Foundational.
2. **US1 (shell)** — STOP and validate: shell on every screen, MVP demoable.
3. **US2 (graph merge)** — both P1s done.

### Incremental delivery

Add US3 (grouped IA) → US4 (assistant panel) → US6 (user guide) → US5 (text-size/connection), validating each independently, then Polish (restyle remaining screens to tokens, empty states, a11y, CHANGELOG, quickstart).

## Notes

- `[P]` = different files, no incomplete-task dependency.
- Tests are mandatory here (Article V); write them failing first.
- The intelligent/agentic Assistant and User-Guide grounding are **feature 015** — out of scope; US4 only delivers the panel chrome and must leave the seam open.
- Commit after each task or logical group; update `CHANGELOG.md` (Article VI) as part of Polish.
