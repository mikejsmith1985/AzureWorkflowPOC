# Feature Specification: Admin Console Look-and-Feel Redesign + Graph Folded Into the Workflow Builder

**Feature Branch**: `feature/014-admin-console-ui-redesign`

**Created**: 2026-06-26

**Status**: Draft

**Input**: User description: "please review the screenshots of the UI in gh issue #31. This gives the overall look and feel that I would like the UI to have which is relatively different than what we have now. It doesn't have to be an exact copy of that UI but a more closely related UX would be ideal. In addition we had agreed to move the graph tab into the same space as the Workflow builder."

---

> **Reference look-and-feel.** GitHub issue #31 contains 14 screenshots of the reference "Admin
> Console / Control Plane" application. They are the **visual and interaction target** for this
> redesign. The intent is a *closely related* experience, **not a pixel-perfect clone**: adopt the
> reference's overall shell (a persistent left sidebar, a global top bar, a persistent collapsible
> right-hand Assistant panel), its visual language (dark-first theme on themeable tokens, card
> surfaces, pill chips, a single accent colour, small uppercase section labels), and its
> section-with-sub-tabs information architecture. This feature is a **presentation, theming, and
> navigation reorganization** of the product's existing capabilities.

> **Scope guard — the AI is a separate feature.** The reference's *intelligent* console-wide Assistant
> — one that answers anything about the app and performs actions on the user's behalf — is specified
> separately in **`specs/015-ai-assistant-console/`** and builds on this redesign. **This** spec (014)
> delivers the Assistant **panel as look-and-feel chrome** (and keeps the existing Workflow Builder
> assistant working inside it) plus a human-readable **User Guide** section; the agentic, console-wide
> intelligence and the AI grounding on the guide are owned by 015. Reference screens for functional
> surfaces this product does not have today (a dedicated Agents roster, a runtime Policy editor, an
> Observability/Metrics backend) are **out of scope**.

---

## Clarifications

### Session 2026-06-26

- Q: How faithful to the reference should this be? → A: **Closely related, not exact.** Adopt the
  shell, visual language, and section/sub-tab IA; do not reproduce reference screens for features
  that don't exist in this product.
- Q: What is the one firm structural change beyond the restyle? → A: **The standalone Graph tab moves
  into the Workflow Builder space.** Graph stops being its own top-level destination.
- Q: How far should the navigation be reorganized? → A: **Fully adopt the reference's section
  grouping.** Re-home every existing screen under the five sidebar sections — **Monitor**,
  **Review Queue**, **Automation**, **Configuration**, **Repos & Apps** — with related views as
  sub-tabs inside each. No net-new feature sections are added.
- Q: When the Graph moves into the Builder, what does the graph view show? → A: **The currently
  loaded workflow's own nodes/edges.** The previous standalone read-only diagram of the *fixed
  intake pipeline* is **retired** — a topology that need not match any real user workflow has little
  value next to the workflow being edited. The Builder canvas already visualizes the real workflow.
- Q: Adopt the reference's branding and section labels, or keep the current product vocabulary? → A:
  **Adopt the reference's vocabulary.** Brand the shell **"Admin Console / Control Plane"** and use
  the reference section names — **Monitor**, **Review Queue**, **Automation**, **Configuration**,
  **Repos & Apps**, and a separated **User Guide**. Current labels (e.g., "Threads", "Settings") are
  replaced by these canonical terms across the console.
- Q: Ship a light theme + toggle now, or dark-first? → A: **Dark-first.** Deliver one polished dark
  theme built on **themeable design tokens** so a light theme is a later follow-up with no per-screen
  rework. A top-bar light/dark **toggle and a full light theme are out of scope** for this feature.
- Q: What does the console-wide Assistant do, and is it part of this feature? → A: **The intelligent
  Assistant is split into its own spec (015).** It is a real, AI-first, **agentic** assistant that
  answers anything about the app (grounded in the User Guide) and can do anything the user can do —
  but that capability is specified and built in **`specs/015-ai-assistant-console/`**, which depends
  on this redesign. In **014**, the Assistant **panel** ships as presentation/chrome that keeps the
  existing Workflow Builder assistant working; the in-app **User Guide** ships as human-readable
  documentation (015 later makes that same guide the AI's knowledge base).

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A consistent console shell on every screen (Priority: P1)

As an operator, on any screen I see one consistent frame: a left sidebar that brands the product and
lists the primary destinations, a top bar with global controls and a connection indicator, and a
right-hand Assistant panel I can collapse. Navigating between destinations keeps this frame in place;
only the central content changes.

**Why this priority**: The shell is the foundation of the new look-and-feel; every other story is
viewed inside it.

**Independent Test**: Load the app, confirm the sidebar, top bar, and Assistant panel are present and
persistent across at least three destinations, and that the active destination is highlighted.

**Acceptance Scenarios**:

1. **Given** the app is open on any destination, **When** the page renders, **Then** a persistent
   left sidebar shows the "Admin Console / Control Plane" brand, a grouped list of primary
   destinations with icons, a visually separated User Guide entry, and a footer version label.
2. **Given** I am on a destination, **When** I look at the sidebar, **Then** the current destination
   is highlighted with the accent treatment and no other item is.
3. **Given** I navigate from one destination to another, **When** the content swaps, **Then** the
   sidebar, top bar, and Assistant panel remain in place without a full-page flash.
4. **Given** a narrow viewport, **When** the sidebar cannot fit, **Then** it collapses to an icon rail
   or a toggle without hiding any destination.

---

### User Story 2 — The Graph lives inside the Workflow Builder (Priority: P1)

As a workflow author, I no longer go to a separate "Graph" destination. The graph of the workflow I am
actually working on is available **within the Workflow Builder surface**. The old standalone diagram
of the fixed intake pipeline — which need not correspond to any real workflow — is retired.

**Why this priority**: This is the one firm structural change the user committed to, independent of the
restyle, and it removes a redundant top-level destination.

**Independent Test**: Confirm there is no standalone Graph destination, that the Builder visualizes the
currently loaded workflow's nodes/edges, and that the old fixed intake-pipeline diagram is gone.

**Acceptance Scenarios**:

1. **Given** the redesigned sidebar, **When** I read the primary destinations, **Then** there is no
   separate top-level "Graph" item.
2. **Given** I have a workflow loaded in the Workflow Builder surface, **When** I view its graph,
   **Then** I see that workflow's own nodes and edges (not a fixed, unrelated pipeline diagram).
3. **Given** an existing bookmark or link to the old Graph location, **When** I open it, **Then** I am
   taken to the Workflow Builder surface (no dead end), not a 404.
4. **Given** the fixed intake-pipeline topology diagram that previously had its own page, **When** I
   look anywhere in the redesigned console, **Then** it no longer appears (it is retired).

---

### User Story 3 — Sections with sub-tabs replace the flat top nav (Priority: P2)

As an operator, related screens are grouped under a small number of sidebar sections, and within a
section I use sub-tabs to move between related views — instead of a long flat row of top-level tabs.

**Why this priority**: The grouped IA is a defining part of the reference UX and reduces top-level
clutter, but the product is usable with the shell (US1) even before the grouping is complete.

**Independent Test**: Confirm each existing screen is reachable under a sidebar section, that sections
expose their sub-views as sub-tabs, and that no pre-redesign screen is orphaned.

**Acceptance Scenarios**:

1. **Given** the sidebar, **When** I read it, **Then** primary destinations are the short grouped set
   (Monitor / Review Queue / Automation / Configuration / Repos & Apps), each with an icon and label.
2. **Given** I open a section that contains multiple related views, **When** it renders, **Then**
   those views appear as sub-tabs within the section's content area, with the active sub-tab
   highlighted.
3. **Given** the set of screens that existed before the redesign, **When** I audit the new IA,
   **Then** each one is reachable (re-homed, not removed) unless explicitly listed as out of scope.

---

### User Story 4 — A persistent, collapsible Assistant panel (presentation) (Priority: P2)

As a user, a right-hand Assistant panel is present across the console as part of the new look-and-feel.
It shows the header, intro text, suggestion chips, and an input. I can collapse, expand, or hide it,
and my choice persists. Where an assistant already works today (the Workflow Builder), it keeps
working inside this panel. (The console-wide *intelligent* behaviour is delivered by spec 015.)

**Why this priority**: The Assistant panel is a prominent part of the reference look-and-feel and a
consistent entry point, but the console is fully usable with it collapsed, and its intelligence is a
separate feature.

**Independent Test**: Confirm the panel renders on multiple destinations with its header controls,
intro text, chips, and input; confirm collapse/expand works and persists; confirm the existing Builder
assistant still works inside the panel.

**Acceptance Scenarios**:

1. **Given** any destination, **When** the page renders, **Then** the Assistant panel is present on the
   right with a header (title, identity, collapse/expand/close controls), intro text, suggestion chips,
   and a message input with a send affordance.
2. **Given** the Assistant panel is open, **When** I collapse or hide it, **Then** the central content
   reflows to use the freed space and the panel can be re-opened.
3. **Given** I set the panel's open/closed state, **When** I navigate or reload, **Then** the panel
   returns to my last chosen state.
4. **Given** the Workflow Builder's existing assistant capability, **When** I use the Assistant there,
   **Then** its existing behaviour is preserved within the new panel chrome.

---

### User Story 5 — Text size and a clear connection indicator (Priority: P3)

As a user, the top bar lets me adjust text size for readability and see at a glance whether the console
is connected and to which host. My text-size choice persists. (A light/dark theme toggle is a deferred
follow-up; this feature ships a single polished dark theme.)

**Why this priority**: These controls round out the reference look-and-feel and aid accessibility, but
they are enhancements on top of the shell.

**Independent Test**: Change text size and confirm content scales without breaking layout; confirm the
connection indicator reflects connected/disconnected; confirm the text-size choice survives a reload.

**Acceptance Scenarios**:

1. **Given** the top bar text-size control, **When** I increase or decrease it, **Then** content text
   scales accordingly without breaking layout.
2. **Given** the connection indicator, **When** the console is connected, **Then** it shows a positive
   state and the host; **When** disconnected, **Then** it shows a clearly distinct negative state.
3. **Given** I set a text size, **When** I reload, **Then** my choice is restored.
4. **Given** the shipped dark theme, **When** I view any screen, **Then** its colours come from shared
   design tokens (so a future light theme requires no per-screen rework).

---

### User Story 6 — An in-app User Guide section (Priority: P2)

As a user, the console has a **User Guide** destination with human-readable documentation of what the
application is, each section/screen, and how to accomplish each key task. (Spec 015 later makes this
same guide the Assistant's knowledge base; this story covers the human-facing documentation only.)

**Why this priority**: It completes the reference IA (User Guide is a sidebar item) and is the content
that spec 015 will ground the AI on, so authoring it here unblocks that feature.

**Independent Test**: Open the User Guide and confirm it covers every primary section and key task in
readable form.

**Acceptance Scenarios**:

1. **Given** the sidebar, **When** I open **User Guide**, **Then** I see documentation covering what
   the app is, each primary section, and how to perform its key tasks.
2. **Given** a primary section or key task exists in the app, **When** I check the guide, **Then** that
   section/task is documented (no major capability left undocumented).
3. **Given** the User Guide, **When** it is rendered, **Then** it uses the same shell and visual
   language as the rest of the console.

### Edge Cases

- **Empty states**: Lists and panels (recent activity, runs, channels, pending review) must show a
  reference-style "nothing here yet" message rather than a blank area.
- **Deep links / old routes**: Pre-redesign URLs (especially the old Graph route) must resolve to the
  new location, not 404.
- **Assistant collapsed by default on small screens**: On narrow viewports the Assistant panel should
  not crowd out content; it may start collapsed.
- **Token coverage**: Status colours, chips, and accent text must be expressed through shared design
  tokens (not one-off hard-coded values), so the deferred light theme can be added later without
  revisiting each screen.
- **Long labels / overflow**: Sidebar labels, sub-tab rows, and chip groups must wrap or truncate
  gracefully without horizontal scrollbars on the main frame.
- **Reduced motion**: Section fade-ins and any transitions must respect a reduced-motion preference.
- **Keyboard and focus**: Sidebar items, sub-tabs, text-size control, and the Assistant input must be
  reachable and operable by keyboard with a visible focus state.

## Requirements *(mandatory)*

### Functional Requirements

**Console shell**

- **FR-001**: The console MUST present a persistent left sidebar branded **"Admin Console / Control
  Plane"**, containing a grouped list of primary destinations using the canonical section names
  (**Monitor**, **Review Queue**, **Automation**, **Configuration**, **Repos & Apps**) — each with an
  icon and label — a visually separated secondary **User Guide** entry, and a footer version label.
- **FR-002**: The sidebar MUST highlight the active destination using the accent treatment and MUST
  show only one active destination at a time.
- **FR-003**: The console MUST present a persistent top bar containing the workspace/page title, a
  text-size control, and a connection/status indicator showing the current host. (A theme toggle is a
  deferred follow-up and is not part of this top bar yet.)
- **FR-004**: The shell (sidebar, top bar, Assistant panel) MUST remain in place while navigating
  between destinations; only the central content region changes.
- **FR-005**: On viewports too narrow for the full sidebar, the sidebar MUST degrade to a collapsed
  rail or toggle that still exposes every destination.

**Graph → Workflow Builder consolidation**

- **FR-006**: The standalone Graph destination MUST be removed from the primary navigation, and the
  fixed intake-pipeline topology diagram it showed MUST be retired (removed from the product).
- **FR-007**: The Workflow Builder surface MUST visualize the **currently loaded workflow's** own nodes
  and edges as its graph; it MUST NOT show a fixed pipeline diagram unrelated to the loaded workflow.
- **FR-008**: Any graph/overview presentation within the Builder MUST stay in sync with the currently
  loaded/selected workflow.
- **FR-009**: Requests to the previous Graph route MUST resolve to the Workflow Builder surface (no
  broken link / no 404).

**Grouped information architecture**

- **FR-010**: Primary destinations MUST be organized into the small grouped set of sidebar sections
  rather than a flat list of every screen.
- **FR-011**: Within a section that has multiple related views, those views MUST be presented as
  sub-tabs with a clearly indicated active sub-tab.
- **FR-012**: Every screen that existed before the redesign MUST remain reachable in the new IA unless
  explicitly listed as out of scope; no existing screen may become orphaned.

**Assistant panel (presentation only)**

- **FR-013**: A right-hand Assistant panel MUST be available across the console, presenting a header
  (title, identity, collapse/expand/close controls), intro text, suggestion chips, and a message input
  with a send affordance.
- **FR-014**: The Assistant panel MUST be collapsible/hideable, the central content MUST reflow when it
  is collapsed, and its open/closed state MUST persist across navigation and reload.
- **FR-015**: The existing Workflow Builder assistant MUST continue to function inside the new panel
  chrome. The console-wide *intelligent/agentic* behaviour is **out of scope here** and is delivered by
  spec 015; this feature MUST NOT block 015 (e.g., the panel must be able to host that capability).

**In-app User Guide (human documentation)**

- **FR-016**: The console MUST include a **User Guide** destination providing human-readable
  documentation of what the application is, each primary section/screen, and how to perform its key
  tasks, covering every primary section and key task (no major capability left undocumented).

**Theme, text size, connection indicator**

- **FR-017**: The console MUST ship one polished **dark** theme whose colours, surfaces, and status
  treatments are defined through **shared design tokens** (not hard-coded per screen), so a future
  light theme can be added without per-screen rework. A light theme and a light/dark toggle are **out
  of scope** for this feature.
- **FR-018**: The console MUST provide a text-size control that scales content text without breaking
  layout.
- **FR-019**: The console MUST show a connection/status indicator that visibly distinguishes connected
  from disconnected and names the current host.
- **FR-020**: The text-size selection MUST persist across reloads.

**Visual language**

- **FR-021**: The redesigned UI MUST apply the reference visual language consistently: card surfaces
  with subtle borders and rounded corners, pill-shaped chips for filters/tags, a single accent colour
  for active/primary affordances, status dots/badges for state, and small uppercase section labels.
- **FR-022**: Empty states across the console MUST use a consistent, friendly "nothing here yet"
  treatment rather than blank regions.
- **FR-023**: The redesign MUST preserve the behaviour and data of every existing screen; it changes
  presentation, theming, and navigation only, not feature logic.

**Accessibility & robustness**

- **FR-024**: All interactive shell elements (sidebar items, sub-tabs, text-size control, Assistant
  input and controls) MUST be keyboard-operable with a visible focus indicator.
- **FR-025**: Motion (fade-ins, transitions) MUST respect a reduced-motion preference.

### Key Entities *(include if feature involves data)*

- **UI Preference**: A per-user, client-persisted set of presentation choices — text size and
  Assistant-panel open/closed state (theme is fixed dark for now). No server data model; persisted on
  the client and restored on load.
- **Navigation Section**: A sidebar grouping that owns one or more views; has an icon, a label, an
  active state, and an ordered set of sub-views (sub-tabs). A presentation/routing concept, not stored
  data.
- **Assistant Panel State**: The presentation state of the right-hand panel (open/closed, plus the
  existing Builder assistant's session where applicable). Open/closed is persisted; intelligent
  behaviour is out of scope here (spec 015).
- **User Guide**: The in-app, human-readable documentation of the application (what it is, each
  section/screen, how to perform each key task). Authored content rendered as a destination; spec 015
  later adopts it as the Assistant's single knowledge source.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From any destination, a user can identify and reach every other primary destination from
  the sidebar in a single click, with the active destination always visibly indicated.
- **SC-002**: The standalone Graph destination no longer exists, the fixed intake-pipeline diagram is
  gone from the product, and the Workflow Builder surface instead shows the loaded workflow's own graph.
- **SC-003**: Every screen that existed before the redesign is reachable after it (zero orphaned
  screens), verified against the pre-redesign navigation inventory.
- **SC-004**: A first-time viewer, shown the redesigned console next to the reference screenshots,
  judges them "clearly the same family of UI" — concretely, the shell (left sidebar + top bar + right
  Assistant panel), the accent-driven active states, and the card/chip visual language are all present.
- **SC-005**: The dark theme is applied consistently across every screen, and theme colours are sourced
  from shared design tokens — verified by the absence of hard-coded theme colours on individual screens.
- **SC-006**: Text-size and Assistant open/closed choices persist across a reload 100% of the time.
- **SC-007**: The shell remains usable (every destination reachable, no horizontal scroll on the main
  frame, no content hidden) across a representative range of viewport widths from a typical laptop down
  to a small tablet.
- **SC-008**: Every primary destination and every key interactive shell control has an automated
  navigation/UX test, consistent with the project's testing rules, and those tests pass.
- **SC-009**: The User Guide documents 100% of primary sections and key tasks (no major user-facing
  capability undocumented), verified against the navigation inventory.

## Assumptions

- **Reference fidelity is "closely related," not exact.** Spacing, exact colours, and copy may differ;
  the shell, IA pattern, and visual language are what must match.
- **Sidebar sections fully adopt the reference grouping** (decided — see Clarifications). Every existing
  screen is re-homed under one of five sections: **Monitor** (intake/threads and any overview/live
  views), **Review Queue**, **Automation** (the Workflow Builder with its in-place graph view, plus the
  saved-workflow gallery), **Configuration** (connector/integration, AI, and notification-channel
  settings as sub-tabs), and **Repos & Apps** (the existing Apps and run/test/history views), with
  **User Guide** as a separated secondary item. Exact sub-tab placement can be refined during planning
  as long as no existing screen is orphaned and no net-new feature section is introduced.
- **Adopt the reference's "Admin Console / Control Plane" branding and section names** in place of
  current labels.
- **Dark-first**: one polished dark theme ships, built on shared design tokens. A light theme and the
  light/dark toggle are deferred; tokens are structured so that follow-up needs no per-screen rework.
- **The intelligent Assistant is a separate feature** (`specs/015-ai-assistant-console/`). Here, the
  Assistant **panel** is presentation chrome that keeps the existing Builder assistant working; the
  agentic console-wide behaviour and AI grounding on the User Guide are specified and built in 015,
  which depends on this redesign.
- **The User Guide authored here is human documentation.** Spec 015 adopts the same guide as the AI's
  single knowledge source; this feature does not build the AI grounding.
- **"Dev Mode" and identity/"switch" affordances** shown in the reference are treated as visual
  elements; wiring them to new behaviour is out of scope unless they already map to an existing
  capability.
- **Preferences are client-side only** (no new server persistence or user accounts introduced).

## Out of Scope

- The **intelligent / agentic console-wide Assistant** and any AI grounding on the User Guide — owned by
  **`specs/015-ai-assistant-console/`**.
- A **light theme** and the **light/dark toggle** (deferred follow-up; this feature ships dark only, on
  themeable tokens).
- New functional surfaces that don't exist today (Agents roster, Policy editor, Observability/Metrics
  backend, multi-user identity/"switch").
- Changes to workflow execution, connector behaviour, intake processing, or any back-office logic.
- Server-side storage of user preferences or any new authentication/authorization.
- A pixel-perfect reproduction of the reference application.

## Dependencies

- The reference screenshots in **GitHub issue #31** as the visual/interaction source of truth.
- The existing screens being re-homed and restyled: the intake/threads view, the Workflow Builder and
  saved-workflow gallery, the standalone Graph view (being **retired**; the Builder's own workflow graph
  supersedes it), the Apps/repo and run views, the Review Queue, and the connector/AI/channel settings.
- The project's existing front-end styling approach and component structure (to be restyled, not
  replaced wholesale).
- The project's UX/navigation test harness, which must cover the new shell and each re-homed destination
  before the feature is considered shippable.
- **Companion feature** `specs/015-ai-assistant-console/` builds on this redesign (the Assistant panel
  chrome and the authored User Guide); 015 depends on 014, not the reverse.
