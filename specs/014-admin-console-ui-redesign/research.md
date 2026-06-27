# Phase 0 Research: Admin Console UI Redesign

All Technical Context items were resolvable from the existing codebase and the reference screenshots;
there are **no open NEEDS CLARIFICATION** items. The decisions below record the chosen approach,
rationale, and rejected alternatives.

## D1 — App shell as a single Blazor layout

- **Decision**: Build one `AppShell` layout (sidebar + top bar + content + right Assistant rail) and
  set it as `DefaultLayout` in `App.razor`. Reconcile `WorkflowBuilderLayout` so the Builder renders
  **inside** the shell content region (full-width canvas) instead of bypassing the shell.
- **Rationale**: Today only `WorkflowBuilder.razor` uses a non-default layout; every other page
  inherits `MainLayout`. Replacing `MainLayout` with `AppShell` restyles the whole app in one place
  (framework-first — Blazor layout system, Article VII). The reference shows the sidebar + Assistant
  rail on the Builder too, so the Builder must live in the shell.
- **Alternatives rejected**: (a) Keep two divergent layouts — the Builder would lack the sidebar/rail,
  breaking shell persistence (FR-004). (b) A bespoke layout/router — violates Article VII.

## D2 — Navigation IA: one nav model, sections + sub-tabs

- **Decision**: A single `Navigation/NavModel.cs` declares the five sections and their sub-views.
  `SidebarNav` renders sections; `SectionTabs` renders the active section's sub-tabs. Existing page
  routes are preserved (links point at them); sections group them. Proposed mapping:

  | Section | Sub-views (existing routes) |
  |---------|-----------------------------|
  | **Monitor** | Threads/intake list (`/`), Run History (`/runs`); New Ticket (`/new-ticket`) as an action |
  | **Review Queue** | `/review-queue` |
  | **Automation** | Workflow Builder (`/workflow-builder`), Workflow Gallery (`/workflow-gallery`) — Graph folded in |
  | **Configuration** | Connector/AI/Channel settings (`/settings/connectors`) |
  | **Repos & Apps** | Apps (`/apps`), App detail (`/apps/{id}`) |
  | **User Guide** (separated) | `/user-guide` (new) |

- **Rationale**: Preserving routes keeps deep links/bookmarks alive (no orphaned screens, FR-012/SC-003)
  while delivering the grouped IA (FR-010/011). A single nav model keeps sidebar and sub-tabs in sync
  and is trivially unit/E2E-testable.
- **Alternatives rejected**: Re-route everything to new section paths (`/monitor/...`) — larger blast
  radius, more redirects, more E2E churn, no user benefit over grouping existing routes. Exact sub-tab
  placement may be refined in tasks as long as nothing is orphaned (per spec Assumptions).

## D3 — Graph folded into the Builder; Mermaid stays for RunDetail

- **Decision**: Retire `Pages/Graph.razor` and its sidebar entry. The Builder already renders the
  **loaded workflow's** own nodes/edges via Z.Blazor.Diagrams — that is the graph (FR-007). Add
  `GraphRedirect.razor` mapping `/graph` → `/workflow-builder` (FR-009). **Keep** the Mermaid CDN
  include and `window.mermaidRender`: `RunDetail.razor` uses Mermaid for a per-run step graph (a real,
  run-specific diagram — not the retired fixed topology).
- **Rationale**: The fixed intake-pipeline topology had little value next to a real workflow (spec
  clarification). RunDetail's per-run graph is unrelated and must keep working.
- **Alternatives rejected**: Removing Mermaid entirely — would break RunDetail. Keeping Graph.razor as
  a Builder sub-tab — contradicts the clarified decision to retire the fixed diagram.

## D4 — Theming via CSS design tokens + Tailwind runtime config

- **Decision**: Introduce `wwwroot/css/design-tokens.css` defining semantic CSS custom properties on
  `:root` (e.g., `--surface`, `--surface-raised`, `--border`, `--text`, `--text-muted`, `--accent`,
  `--accent-contrast`, status colours) with the dark palette as values. Add a runtime
  `tailwind.config` in `_Host.cshtml` mapping semantic Tailwind colour names (e.g., `bg-surface`,
  `text-accent`) to `var(--…)`. Screens use the semantic classes, not raw `gray-950`/`cyan-400`.
- **Rationale**: Satisfies "dark-first on themeable tokens, light deferred with no per-screen rework"
  (FR-017/SC-005). A future light theme = a second `:root[data-theme="light"]` block flipping the
  variables, with zero screen edits. CDN Tailwind supports inline `tailwind.config`, so no build step
  is introduced (framework-first). 
- **Alternatives rejected**: (a) Adopt a full Tailwind build/PostCSS pipeline — heavier toolchain for
  a CDN-based app, out of proportion. (b) Keep hard-coded Tailwind palette classes — fails SC-005 and
  forces per-screen edits for the future light theme. (c) A component library (MudBlazor) — large
  rewrite, none in use today.

## D5 — Preference persistence (text size + Assistant open/closed)

- **Decision**: New `UiPreferenceService` reads/writes two keys via the existing
  `window.localStorageGet/Set` interop (the same pattern `OnboardingStateService` uses): a text-size
  scale and the Assistant panel open/closed flag. Applied on first interactive render (as the
  onboarding service does), with safe defaults when storage is unavailable.
- **Rationale**: Reuses proven, gracefully-degrading interop (Article VII); no server state, no new
  auth (spec Out of Scope). Unit-testable with a mocked `IJSRuntime`.
- **Alternatives rejected**: Server-side preference store / user accounts — explicitly out of scope.
  CSS-only `prefers-color-scheme` — doesn't cover text size or panel state and theme toggle is deferred.

## D6 — Text-size mechanism

- **Decision**: The text-size control sets a root scale (e.g., a `data-text-size` attribute / CSS
  variable on the shell root driving `rem`-based sizing). Content uses relative units so scaling
  reflows without breaking layout (FR-018). Persisted via D5.
- **Rationale**: Single root lever, no per-component changes; matches the reference's A−/A+ control.
- **Alternatives rejected**: Browser zoom (not app-controllable/persistable); per-component font props
  (unmaintainable).

## D7 — Connection indicator from Blazor circuit state

- **Decision**: Surface connected/disconnected using Blazor's built-in reconnection state (the
  `components-reconnect-modal` already present) plus the configured host; show a green/!-state dot +
  host in the top bar (FR-019). 
- **Rationale**: Blazor already tracks circuit health; reuse it rather than build a heartbeat
  (Article VII). The E2E base already inspects `#components-reconnect-modal`, so tests can assert it.
- **Alternatives rejected**: A custom SignalR heartbeat/`CircuitHandler` ping — reinventing built-in
  behavior.

## D8 — Assistant panel: chrome now, intelligence in 015

- **Decision**: `AssistantPanel.razor` is a shell-level right rail rendering the header
  (title/identity/collapse-expand-close), intro text, suggestion chips, and an input. In the
  **Builder**, it hosts the existing `WorkflowChatPanel` (fully parameterized, no JS coupling — low-risk
  hoist) so current behavior is preserved (FR-015). Elsewhere it is presentation that 015 will wire to
  intelligence. Open/closed persists (D5); content reflows when collapsed (FR-014).
- **Rationale**: Keeps 014 presentation-scoped and unblocks 015 without building AI here. The chat
  panel's parameter/callback surface makes hoisting cheap.
- **Alternatives rejected**: Build console-wide intelligence now — that is feature 015. Leave the panel
  Builder-only — fails the reference shell (FR-013) and US4.

## D9 — Testing approach

- **Decision**: Add Playwright E2E classes (mirroring `AppsPageTests` conventions, `WebAppFixture` @
  :5099) for: shell navigation + active state + cross-nav persistence; `/graph` redirect + absence of
  the standalone Graph; Assistant panel presence/collapse-persist + Builder assistant still works;
  text-size scale+persist + connection indicator; User Guide reachable + coverage. Add xUnit unit
  tests for `UiPreferenceService` (mocked interop). Follow Red→Green (Article V); run via
  `scripts/run-e2e.ps1`.
- **Rationale**: Article V requires a Playwright test per nav tab and key interactive element before
  shippable; existing harness supports it directly.
- **Alternatives rejected**: Manual verification only — violates Article V/X.
