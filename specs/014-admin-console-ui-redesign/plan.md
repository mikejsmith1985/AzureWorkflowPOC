# Implementation Plan: Admin Console Look-and-Feel Redesign + Graph Folded Into the Workflow Builder

**Branch**: `feature/014-admin-console-ui-redesign` | **Date**: 2026-06-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/014-admin-console-ui-redesign/spec.md`

## Summary

Restyle the existing Blazor Server app to the reference "Admin Console / Control Plane" look-and-feel
(gh issue #31): replace the flat top-nav `MainLayout` with a single **app shell** — a persistent left
sidebar (five canonical sections + a separated User Guide + version footer), a global top bar
(text-size control + connection indicator), and a persistent collapsible right-hand **Assistant
panel** (presentation chrome that hoists the existing Workflow Builder chat panel). Re-home every
existing page under the five sections with sub-tabs, fold the standalone Graph into the Workflow
Builder (retire `Graph.razor` + its fixed intake-pipeline Mermaid diagram; the Builder already renders
the loaded workflow's own graph via Z.Blazor.Diagrams; redirect `/graph`), and introduce **CSS
design-token theming** (a polished dark theme today, structured so a light theme is a later no-rework
follow-up). Persist text-size + Assistant open/closed via the existing `localStorage` interop. The
intelligent/agentic Assistant is a separate feature (015) that this shell makes room for.

## Technical Context

**Language/Version**: C# / .NET 8; Razor components (Blazor Server, interactive over SignalR).

**Primary Dependencies**: Blazor Server; Tailwind CSS via CDN (`cdn.tailwindcss.com`, runtime
`tailwind.config`); Z.Blazor.Diagrams 3.0.4.1 (Builder graph canvas — already the loaded-workflow
visualizer); Mermaid 10 (**retained** — `RunDetail.razor` renders a per-run step graph with it);
existing `window.localStorageGet/Set` JS interop. No new NuGet packages.

**Storage**: No database changes. UI preferences (text size, Assistant open/closed) persist
client-side in `localStorage`. No server-side preference store, no new auth (per spec Out of Scope).

**Testing**: xUnit unit tests for new services (millisecond, mocked); Playwright E2E
(`tests/DBAIAzure.E2ETests`, `WebAppFixture` on `http://localhost:5099`, run via
`scripts/run-e2e.ps1`) — one nav/UX test per re-homed destination and per key shell control, per
constitution Article V. TDD red→green.

**Target Platform**: Modern evergreen browsers; viewport widths from a typical laptop (~1280px) down
to a small tablet (~768px).

**Project Type**: Single Blazor Server web application (`src/DBAIAzure.Web`).

**Performance Goals**: Section navigation swaps central content without a full-page reload or visible
shell flash (the shell is layout-stable). No new network round-trips for nav; preferences read once on
first interactive render.

**Constraints**: Dark-first, with all theme colours expressed as **CSS custom properties (design
tokens)** so no screen hard-codes theme colours (SC-005) and a light theme is later additive;
reduced-motion respected (FR-025); all shell controls keyboard-operable with visible focus (FR-024);
no horizontal scroll on the main frame; old `/graph` route must not 404.

**Scale/Scope**: ~12 existing pages re-homed into 5 sidebar sections + User Guide; 1 app shell
(sidebar + top bar + assistant rail + per-section sub-tabs); 1 new lightweight preferences service; 1
new User Guide content page; retire 1 page (`Graph.razor`). No back-office logic touched.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Article | Gate | Status |
|---------|------|--------|
| II — Process protection | No wildcard process kills; target PIDs | ✅ N/A (no process management in this feature) |
| III — Branching | Work on `feature/014-…`, PR to main | ✅ On branch; draft PR #36 open |
| IV — Code quality | Self-documenting names; booleans as predicates; `Async`+CancellationToken; XML docs on public members; <40-line methods; guard clauses; nullable honored | ✅ Applies to new components/services |
| V — Testing (3-layer) | Playwright per nav tab + key interactive element before shippable; unit tests mocked; TDD red→green | ✅ Plan adds E2E per destination + shell controls; unit tests for the preferences service |
| VI — Documentation | Update `CHANGELOG.md`; no auxiliary status docs | ✅ CHANGELOG entry in implementation; specs/ tree is exempt |
| VII — Framework-first | Use the governing framework, don't hand-roll | ✅ See gate detail below |
| IX — Secrets | No secret in source/log | ✅ N/A (feature handles no secrets) |
| X — Verification | Evidence, not "it compiles" | ✅ Playwright assertions + quickstart validation |
| XI — Output restraint | At most one dashboard; no narrated phases; keep scratch out of tree | ✅ No scratch dashboards introduced |

**Article VII — Framework-first detail (PASS):**
- **Layout/routing** → Blazor's `LayoutComponentBase` + `RouteView`/`@layout`; do **not** build a
  bespoke router. The shell is a Blazor layout; sections/sub-tabs are routed Razor pages + a sub-tab
  component reading a nav model.
- **Theming** → CSS custom properties (web-platform feature) + Tailwind's runtime `tailwind.config`
  mapping semantic colours to those variables; do **not** build a bespoke theming engine.
- **Preference persistence** → reuse the existing `localStorage` JS interop already used by
  `OnboardingStateService`; do **not** add a new persistence layer.
- **Builder graph** → Z.Blazor.Diagrams (already present and already renders the loaded workflow);
  no new diagram tech. Mermaid stays only for `RunDetail`'s per-run graph.
- **Connection indicator** → Blazor's built-in circuit/reconnection state (`components-reconnect-modal`
  already present); surface it, don't reimplement reconnection.
- **Assistant intelligence** → out of scope here; deferred to 015 (framework-first SK design lives
  there). This feature only provides the panel chrome and must not block 015.

No violations → **Complexity Tracking is empty.**

## Project Structure

### Documentation (this feature)

```text
specs/014-admin-console-ui-redesign/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (UI-state entities + IA map)
├── quickstart.md        # Phase 1 output (validation guide)
├── contracts/           # Phase 1 output (UI/shell/navigation contracts)
│   ├── app-shell.md
│   ├── navigation-ia.md
│   ├── assistant-panel.md
│   └── theming-tokens.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/DBAIAzure.Web/
├── Shared/
│   ├── AppShell.razor              # NEW default layout: sidebar + topbar + content + assistant rail
│   ├── Shell/
│   │   ├── SidebarNav.razor        # NEW left sidebar (brand, sections, User Guide, version footer)
│   │   ├── TopBar.razor            # NEW top bar (title, text-size control, connection indicator)
│   │   ├── SectionTabs.razor       # NEW per-section sub-tab strip (reads the nav model)
│   │   └── ConnectionIndicator.razor # NEW (surfaces Blazor circuit connected/disconnected + host)
│   ├── AssistantPanel.razor        # NEW shell-level right rail; hosts the existing chat panel in the Builder
│   ├── MainLayout.razor            # RETIRED/replaced by AppShell (or thinned to delegate)
│   └── WorkflowBuilderLayout.razor # RECONCILED: Builder runs inside AppShell content area
├── Navigation/
│   └── NavModel.cs                 # NEW: sections → sub-views (label, icon, route, consequential? no)
├── Services/
│   └── UiPreferenceService.cs      # NEW: text-size + assistant-open state via localStorage interop
├── Pages/
│   ├── UserGuide.razor             # NEW: in-app human-readable User Guide (/user-guide)
│   ├── GraphRedirect.razor         # NEW: /graph → /workflow-builder (no 404)
│   ├── Graph.razor                 # RETIRED (fixed intake-pipeline Mermaid diagram removed)
│   ├── Index/WorkflowBuilder/WorkflowGallery/Apps/AppDetail/RunHistory/
│   │   RunHistoryDetail/RunDetail/ReviewQueue/NewTicket/ConnectorSettings.razor # RE-HOMED + restyled
│   └── _Host.cshtml                # tailwind.config (semantic tokens) added; Mermaid include KEPT
└── wwwroot/css/
    ├── design-tokens.css           # NEW: CSS custom properties (dark theme values)
    └── workflow-canvas-animations.css # existing

tests/DBAIAzure.E2ETests/Tests/
├── ShellNavigationTests.cs         # NEW: sidebar sections, active state, persistence of shell across nav
├── GraphRedirectTests.cs           # NEW: /graph resolves to Builder; no standalone Graph
├── AssistantPanelTests.cs          # NEW: panel present, collapse/expand persists, Builder assistant works
├── TextSizeAndConnectionTests.cs   # NEW: text-size scales+persists; connection indicator states
├── UserGuideTests.cs               # NEW: guide reachable + covers sections
└── (existing per-page tests updated for new routes/selectors)

tests/DBAIAzure.Tests/
└── UiPreferenceServiceTests.cs     # NEW: unit tests (mocked JS interop) for read/write/default
```

**Structure Decision**: Single Blazor Server app. Introduce one **`AppShell`** layout set as
`DefaultLayout` so every page inherits the new shell; reconcile `WorkflowBuilderLayout` so the Builder
renders inside the shell's content area (keeping full-width canvas) rather than bypassing the shell —
this is what lets the Assistant rail and sidebar appear on the Builder, matching the reference. A
small `Navigation/NavModel.cs` is the single source for sidebar sections and their sub-tabs (consumed
by `SidebarNav` and `SectionTabs`). All theme colours move to `design-tokens.css` custom properties
referenced through Tailwind's runtime config. No new projects.

## Complexity Tracking

> No constitution violations — section intentionally empty.
