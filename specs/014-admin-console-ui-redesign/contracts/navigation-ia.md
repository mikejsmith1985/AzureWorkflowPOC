# Contract: Navigation IA (sections, sub-tabs, redirect)

Backed by `Navigation/NavModel.cs` (see data-model). Sidebar and sub-tabs render from this single
model.

## Section → sub-view map (routes preserved)

| Section | Sub-views → routes |
|---------|--------------------|
| Monitor | Threads `/` · Run History `/runs` (New Ticket `/new-ticket` as an action) |
| Review Queue | `/review-queue` |
| Automation | Workflow Builder `/workflow-builder` · Workflow Gallery `/workflow-gallery` |
| Configuration | Connector/AI/Channel settings `/settings/connectors` |
| Repos & Apps | Apps `/apps` (detail `/apps/{id}` via `MatchPrefix=/apps`) |
| User Guide *(secondary)* | `/user-guide` |

Detail routes (`/runs/{id}`, `/run/{id}`, `/apps/{id}`) resolve active state via their section's
`MatchPrefix`. Exact sub-tab placement may be refined in tasks provided no route is orphaned.

## Behavioral contract

- **C-NAV-1**: Every route that existed before the redesign resolves to a destination under exactly
  one section (FR-012/SC-003). No pre-redesign screen is removed except the standalone Graph.
- **C-NAV-2**: A section with multiple sub-views renders a `SectionTabs` strip; the sub-view matching
  the current route is the active tab (FR-011).
- **C-NAV-3**: `/graph` MUST resolve to `/workflow-builder` (redirect), never 404 (FR-009). There MUST
  be no standalone "Graph" sidebar entry and no fixed intake-pipeline diagram anywhere (FR-006).
- **C-NAV-4**: The Builder visualizes the **currently loaded workflow's** nodes/edges; switching the
  loaded workflow updates the graph (FR-007/FR-008).

## E2E acceptance

- Assert the sidebar lists exactly the six entries (five primary + User Guide) and no "Graph" entry.
- Visit `/graph`; assert it lands on the Builder (no 404).
- For a section with sub-tabs (Automation), assert the active sub-tab tracks the route.
- Inventory check: every pre-redesign route returns a rendered destination (no 404, no orphan).
