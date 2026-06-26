# Contract: App Shell

The shell is the consistent frame around every screen. It is a Blazor layout (`AppShell.razor`) set as
`DefaultLayout`. This contract defines what the shell guarantees; it does not prescribe markup.

## Regions (always present, layout-stable)

| Region | Contents | Guarantee |
|--------|----------|-----------|
| **Left sidebar** | Brand block "Admin Console / Control Plane"; primary section list (Monitor, Review Queue, Automation, Configuration, Repos & Apps) each with icon+label; separated **User Guide**; footer version label | Persists across navigation; exactly one active section highlighted (FR-001/FR-002) |
| **Top bar** | Page/section title; text-size control; connection/status indicator (host) | Persists across navigation (FR-003) |
| **Content region** | The routed page (and, within a section, its `SectionTabs`) | Only this region changes on navigation (FR-004) |
| **Right Assistant rail** | `AssistantPanel` (see `assistant-panel.md`) | Persists; collapsible; state restored from preference (FR-013/FR-014) |

## Behavioral contract

- **C-SHELL-1**: Navigating between any two destinations MUST NOT reload the page or flash the shell;
  sidebar, top bar, and rail remain mounted.
- **C-SHELL-2**: The active section (and active sub-tab, if any) MUST reflect the current route, and no
  more than one of each is active at a time.
- **C-SHELL-3**: At viewport widths below the sidebar's minimum, the sidebar MUST collapse to an icon
  rail or a toggle that still reaches every destination (FR-005); the main frame MUST NOT show a
  horizontal scrollbar.
- **C-SHELL-4**: All shell controls (section links, sub-tabs, text-size control, assistant controls)
  MUST be keyboard-focusable with a visible focus ring (FR-024).
- **C-SHELL-5**: Transitions/animations MUST be suppressed under `prefers-reduced-motion` (FR-025).
- **C-SHELL-6**: The Workflow Builder MUST render inside the content region of this shell (not bypass
  it), so the sidebar and rail are present there too.

## E2E acceptance (Playwright)

- Load three destinations; assert sidebar + top bar + rail present each time and the correct section
  is highlighted.
- Shrink viewport; assert all destinations still reachable and no main-frame horizontal scroll.
- Tab through shell controls; assert focus visibility.
