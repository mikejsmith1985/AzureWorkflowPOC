# Phase 0 Research: Work-Tracker Adapter

No open NEEDS CLARIFICATION (FR-005 — single active tracker, behind a routing seam — resolved in the spec).

## R1 — Adapter contract shape

**Decision**: A single `IWorkTrackerAdapter` with the minimal method set that covers the four ADO-coupled
edges plus the two existing comment/upsert behaviours: `CreateWorkItemAsync`, `UpsertWorkItemAsync`,
`AppendCommentAsync`, `SetFieldsAsync`, `ResolveByBindingKeyAsync`, `ProvisionFieldsAsync`,
`GetRollupCapability`.
**Rationale**: These are exactly the operations the pipeline/cost/binding layers perform on a tracker
today (mapped from `IBoardsClient` + `CostProjectionService` + `AdoTelemetryPreflightService` + the
binding map). Keeping it minimal avoids leaking tracker concepts.
**Alternatives**: Splitting into `IWorkItemWriter` + `IFieldProvisioner` + `IBindingResolver` — rejected
for v1 as over-segmented; the single adapter can be refactored into roles later if needed.

## R2 — Work item identity (`WorkItemRef`)

**Decision**: An opaque `WorkItemRef` value type wrapping a `string`. The ADO adapter stores the int id
as its string form; the Jira adapter stores the issue key (`PROJ-123`). Each adapter owns parse/format.
**Rationale**: ADO uses `int`, Jira uses string keys; the core must not know which. A string is the
common superset and serialises cleanly into the binding map.
**Alternatives**: Keep `int` (ADO-only — fails Jira); a discriminated union (overkill for two shapes).
**Impact**: `BindingWorkItemMap.WorkItemId` widens `int → string`; `IBoardsClient` stays int internally to
the ADO adapter (no ADO-side change).

## R3 — Logical field mapping

**Decision**: The telemetry config holds **logical** field names (`AIRuntimeCostUSD`, `CostBindingKey`,
…). Each adapter resolves logical → native: ADO maps to `Custom.<logical>` (its existing reference names,
so **live ADO fields are unchanged**); Jira resolves logical → `customfield_NNNNN` by field display name
at provision time and caches the map.
**Rationale**: ADO uses friendly reference names, Jira uses per-instance numeric ids — only the adapter
can resolve. Logical names keep the config and the cost/telemetry code tracker-neutral (FR-004).
**Alternatives**: Per-tracker config files (duplicative); storing native refs in the core (leaks ADO).

## R4 — Active-adapter resolution seam

**Decision**: `IWorkTrackerAdapterProvider.GetAdapter(routingContext?)` returns the single active adapter
chosen from connector config (which work-tracker connector is configured/active). The optional routing
context is unused in v1 but reserves the extension point for per-project/per-workflow routing.
**Rationale**: Satisfies FR-005 (single active now, additive later) without building routing now.
**Alternatives**: Direct DI of one adapter (no future seam); full routing engine now (over-build).

## R5 — Jira field provisioning

**Decision**: Provision in three idempotent steps via Jira Cloud REST: (1) find-or-create the global
custom field (`GET/POST /rest/api/3/field`), (2) ensure a **field context** scoped to the relevant issue
types + project (`/rest/api/3/field/{id}/context`), (3) add the field to the relevant **screens**
(`/rest/api/3/screens/.../fields`). Look up by name first → idempotent re-runs.
**Rationale**: Jira's customisation model is global-field + context + screen, not ADO's process/WIT
attachment. There is no inherited-WIT materialization concept on Jira.
**Alternatives**: Assume fields pre-created manually (fragile); a marketplace app (out of scope).
**Note**: Jira field types map: string→`text`, double→`number`, integer→`number`, picklist→`select`.

## R6 — Jira hierarchy & rollup

**Decision**: Map issue types Epic→Story/Task/Bug→Sub-task; parent via the parent field / Epic link.
Cost **rollup** is delivered by **Advanced Roadmaps** (initiative level + hierarchy) or a marketplace
aggregation — the adapter's `GetRollupCapability` returns `Native("Advanced Roadmaps")`; if a target
instance lacks it, the capability is `RequiresAddOn` and the operator is told (FR-010, SC-005).
**Rationale**: Jira does not natively sum a numeric field up the parent tree like ADO Analytics; honesty
about the gap beats a custom engine (Article VII / Article XI).
**Alternatives**: Custom rollup engine (rejected — Framework-First); JQL+ScriptRunner (operator-side, documented).

## R7 — ADO no-regression refactor

**Decision**: `AzureDevOpsWorkTrackerAdapter` **delegates** to the existing `AzureDevOpsBoardsClient` and
`AdoTelemetryPreflightService` unchanged, converting `WorkItemRef ↔ int` and logical → `Custom.*` at the
boundary. `IBoardsClient` is retained as the ADO adapter's internal seam.
**Rationale**: Guarantees SC-001 — the live ADO telemetry/cost path (incl. the #46/#47 inherited-process
fixes) is untouched; only the *caller* indirection changes.
**Alternatives**: Rewrite ADO onto the new contract directly (needless risk to a just-fixed path).

## R8 — Pipeline/cost layer migration

**Decision**: `CreateWorkItemStep`, `CostProjectionService`, `TelemetryWriteBackService`, and
`IBindingWorkItemMap` switch their dependency from `IBoardsClient`/`Custom.*`/`int` to
`IWorkTrackerAdapter`/logical fields/`WorkItemRef`. The cost ledger, binding minter, DoR enforcement, and
dev-usage ingest are **untouched** (already tracker-neutral).
**Rationale**: Confines tracker knowledge to the adapters (FR-009); the change surface for a third tracker
is a new adapter only (SC-006).
