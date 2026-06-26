# Phase 1 Data Model: Admin Console UI Redesign

This feature introduces **no database entities**. The "data" here is client-side UI state and a
static navigation model. All of it is presentation/state, restored on load; nothing is persisted
server-side (per spec Out of Scope).

## Entity: UiPreference (client-persisted)

A per-browser set of presentation choices, stored via the existing `localStorage` interop.

| Field | Type | Rules / Default |
|-------|------|-----------------|
| `TextSize` | enum { `Small`, `Normal`, `Large` } (or a small int scale) | Default `Normal`. Persisted under key `ui.textSize`. Drives the shell root scale (D6). |
| `IsAssistantOpen` | bool | Default: open on wide viewports, closed on narrow (≤ tablet). Persisted under key `ui.assistantOpen`. |

- **Lifecycle**: read once on first interactive render (mirrors `OnboardingStateService`); written on
  each change. Missing/unreadable storage → fall back to defaults (graceful degradation).
- **No server model.** Theme is fixed dark for this feature (light deferred), so theme is **not** a
  stored preference yet — but the token system (below) is structured so adding it later is additive.

## Entity: NavSection (static navigation model)

Declared once in `Navigation/NavModel.cs`; the single source for the sidebar and sub-tabs.

| Field | Type | Rules |
|-------|------|-------|
| `Key` | string | Stable id (e.g., `monitor`). |
| `Label` | string | Canonical reference name (Monitor, Review Queue, Automation, Configuration, Repos & Apps, User Guide). |
| `Icon` | string | Icon identifier for the sidebar row. |
| `IsSecondary` | bool | `true` for User Guide (visually separated below the primary group). |
| `SubViews` | list<NavSubView> | Ordered; may be a single entry (section maps to one screen). |

### NavSubView

| Field | Type | Rules |
|-------|------|-------|
| `Label` | string | Sub-tab label. |
| `Route` | string | Existing page route (e.g., `/workflow-builder`). Routes are preserved (D2). |
| `MatchPrefix` | string? | Optional route prefix used to compute active state for detail pages (e.g., `/apps` active on `/apps/{id}`). |

- **Active state**: exactly one section and one sub-view are "active" for the current route
  (FR-002/FR-011); computed from the current URI against `Route`/`MatchPrefix`.
- **Invariant**: every pre-redesign route appears in exactly one section (no orphans, FR-012/SC-003),
  except the retired `/graph` which is served by a redirect, not a sub-view.

## Concept: ThemeTokens (CSS custom properties)

Not a C# entity — a documented set of semantic CSS variables in `wwwroot/css/design-tokens.css`,
consumed via Tailwind's runtime config. Enumerated in `contracts/theming-tokens.md`. The dark theme
supplies the values; a future light theme overrides the same names.

## Concept: AssistantPanelState (presentation)

The right-rail state: `IsOpen` (from `UiPreference`), and — in the Workflow Builder only — the hosted
`WorkflowChatPanel`'s existing parameters/session (unchanged from today). Intelligent behaviour is out
of scope here (feature 015).

## State transitions

- **Text size**: `Normal ⇄ Small ⇄ Large` via the top-bar control; each change persists and rescales
  the shell root immediately.
- **Assistant panel**: `Open ⇄ Collapsed/Hidden` via the panel controls; each change persists and the
  content region reflows to reclaim/yield the rail width.
- **Connection indicator**: `Connected ⇄ Disconnected`, driven by Blazor circuit state (not stored).
