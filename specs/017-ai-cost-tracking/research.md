# Phase 0 Research: Two-Dimensional AI Cost Tracking

## R1 — Rollup mechanism (deferred from clarify)

**Decision**: Roll up via **ADO Analytics (OData)** — and optionally a Power BI report — summing a
numeric per-item cost field up the Epic→Feature→Initiative tree. The app maintains the per-item field;
ADO does the tree aggregation.

**Rationale**: Framework-First (Article VII) — ADO Analytics is built to aggregate custom fields across
the work-item hierarchy, and PMs already live in ADO. Building a parallel rollup engine duplicates it.

**Alternatives**: App-computed parent totals (walk the tree on every write — brittle, racy, reinvents
Analytics). External warehouse + BI (more granular but heavier; viable later for cross-org reporting).

## R2 — Why a ledger instead of mutating per-item fields

**Decision**: Append-only **`CostLedgerEntry`** is the source of truth; per-item work-item fields are a
**cumulative projection** recomputed from the ledger.

**Rationale**: Cumulative-by-construction (FR-007) and duplication-free (FR-008) fall out naturally —
totals are sums, never overwrites. The snapshot-field approach (#42 today) loses history on re-run and
duplicates a run's cost across multiple created items. A ledger fixes both and keeps an audit trail.

## R3 — Binding key: mint point & DoR ordering

**Finding**: The phase pipeline runs signal → `ValidationStep` (DoR) → approval → `CreateWorkItemStep`.
The ADO work item only exists at the end, but DoR must be able to assert a binding key.

**Decision**: **Mint at intake** (signal mapping / orchestrator start) onto `PhaseHandlerState`;
`ValidationStep` asserts presence (DoR gate); `CreateWorkItemStep` writes it to the ADO work item
(`Custom.CostBindingKey`) and back to the ServiceNow ticket. Token is **source-neutral**, branch-safe
(e.g. `BIND-<base32>`), queryable in ADO.

**Rationale**: Guarantees the key exists before DoR and before any developer touches the ticket; one
key spans SNow + ADO (clarified). Belt-and-suspenders: the pipeline mints it, DoR re-checks it.

## R4 — Development-spend ingest & agent emit path

**Decision**: A secret-gated **`POST /api/telemetry/dev-usage`** controller (mirrors the SpecKit/
ServiceNow webhook pattern) accepts a session's usage payload carrying the **binding key**. The agent
side (Claude Code) exports usage and posts it — via its OTLP exporter through a collector, or a thin
session hook — tagged with the binding key the developer declared.

**Rationale**: The app owns the *ingest contract + ledger + rollup*; the *emit + bind* lives in each
engineer's tooling (an org rollout, explicitly out of scope to enforce per the spec). A plain controller
keeps the app side standard and testable.

**Open (org, not app)**: exact emit transport (OTLP collector vs direct post vs hook) — documented in
contracts as "any client that satisfies the ingest contract"; not built here.

## R5 — Dev-cost source (deferred from clarify)

**Decision**: **Re-price token counts via `ModelPricing`** (authoritative + consistent with runtime);
accept a caller-supplied cost only as a fallback when token counts are absent.

**Rationale**: One pricing source of truth; avoids trusting heterogeneous agent cost calculations.

## R6 — Unattributed handling

**Decision**: A dev-usage payload whose binding key does not resolve to a DoR-passed ticket is recorded
as a ledger entry with `WorkItemId = null`, `IsUnattributed = true` — quantifiable, never dropped (FR-010).

## R7 — Schema provisioning

**Finding**: `PipelineDbContext` uses `EnsureCreated` (no migrations).

**Decision**: Add the `CostLedgerEntry` table additively; dev stores recreated to pick it up (matches
the spec-016 approach). New ADO custom fields (`Custom.CostBindingKey`, `Custom.AIRuntimeCostUSD`,
`Custom.AIDevCostUSD`) are provisioned by the existing telemetry preflight (Bootstrap) / Adaptive fallback.
