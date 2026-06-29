# Quickstart: Work-Tracker Adapter

Validation scenarios proving the abstraction works and ADO is unchanged. See
[contracts/work-tracker-adapter.md](./contracts/work-tracker-adapter.md) and [data-model.md](./data-model.md).

## Prerequisites

- A work-tracker connector configured (Azure DevOps **or** Jira) via Connector Settings + vault secrets.
- For Jira: a Jira Cloud site + API token; project + issue types (Epic/Story/Task/Bug).

## Scenario A — ADO no-regression (SC-001)

1. Configure Azure DevOps as the active tracker (as today).
2. Run a phase (Specify/Plan/Implement).
3. **Expect**: identical to current behaviour — work item created, `Custom.CostBindingKey` + cost fields
   set, fields attached via the existing preflight (incl. inherited-process handling), ADO Analytics
   rollup unchanged. The full existing regression suite passes unmodified.

## Scenario B — Jira create + bind + project (US1/US3, SC-003)

1. Configure Jira as the active tracker.
2. Run a phase.
3. **Expect**: a Jira issue is created; its binding-key field carries `BIND-*`; the cumulative cost
   fields (`AIRuntimeCostUSD`/`AIDevCostUSD`) are set on the anchor issue — through the same pipeline/cost
   code path, no tracker branching.

## Scenario C — Idempotent provisioning on both trackers (US2, SC-004)

1. Run field provisioning for the active tracker, then run it again.
2. **Expect**: first run makes the logical fields usable on Epic/Story(UserStory)/Task/Bug; second run
   reports `IsSuccess` with no changes. (ADO: process/WIT attach; Jira: field + context + screen.)

## Scenario D — Cross-tracker dev-usage (US3, SC-003)

1. `POST /api/telemetry/dev-usage` with a valid binding key on each tracker.
2. **Expect**: the resolved issue's cumulative dev-cost field reflects the ledger total; an unknown key is
   recorded unattributed — identical behaviour on ADO and Jira.

## Scenario E — Rollup capability surfaced (US4, SC-005)

1. Request the rollup view per tracker.
2. **Expect**: ADO sums up the tree via ADO Analytics; a Jira instance with Advanced Roadmaps reports
   `Native("Advanced Roadmaps")`; an instance without it reports `RequiresAddOn` with an operator notice —
   the per-item cost fields are correct either way.

## Scenario F — Third-tracker change surface (SC-006)

1. Inspect the diff required to add a hypothetical third tracker.
2. **Expect**: a new adapter class implementing `IWorkTrackerAdapter` + its config — **zero** edits to the
   pipeline, cost, or binding layers.
