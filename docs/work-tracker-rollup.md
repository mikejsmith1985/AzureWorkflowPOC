# AI-cost rollup per work tracker (spec-018)

The app maintains two cumulative per-item cost fields — `AIRuntimeCostUSD` and `AIDevCostUSD` (logical
names; each adapter resolves them to its native field) — on every work item via the active
`IWorkTrackerAdapter`. **Hierarchical rollup is delegated to each tracker's native tooling** (Framework-First):
no custom cross-tracker aggregation engine is built. Each adapter advertises how it rolls up through
`GetRollupCapability()`, and a tracker that lacks native hierarchical aggregation says so rather than
silently producing incomplete numbers (FR-010).

## Azure DevOps — `Native("ADO Analytics")`

ADO sums the cost fields up the work hierarchy (Task → User Story → Feature → Epic → Initiative) natively
via **ADO Analytics / OData**. See [`docs/ado-cost-analytics.md`](./ado-cost-analytics.md) for the saved
query + Power BI report. Nothing extra is required — the per-item fields are summed by the tree.

## Jira — `RequiresAddOn("Jira Advanced Roadmaps")`

Jira does **not** natively sum a numeric custom field up the parent hierarchy. The per-item fields are
populated correctly on each issue, but hierarchical rollup needs one of:

- **Advanced Roadmaps** (Jira Premium) — add the cost fields as roll-up columns across the
  Epic → Story/Task → Sub-task hierarchy.
- A **marketplace aggregation app** (e.g. a numeric roll-up / "sum up the tree" plugin), or
- A **JQL + ScriptRunner** scheduled job that aggregates the field over a parent's descendants.

The adapter surfaces this as a `RequiresAddOn` capability with an operator notice, so the gap is explicit.

## Adding a tracker

A new adapter returns its own `RollupCapability`:
- `Native(tool)` — the tracker sums the fields up the tree itself (like ADO Analytics).
- `RequiresAddOn(tool)` — per-item fields are correct; rollup needs the named add-on.
- `None` — no hierarchical rollup available.

The per-item cost fields are always written the same way (`SetFieldsAsync` + logical names); only the
rollup mechanism differs per tracker.
