# Implementation Plan: Multi Work-Tracker Support via a Work-Tracker Adapter

**Branch**: `018-work-tracker-adapter` | **Date**: 2026-06-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/018-work-tracker-adapter/spec.md`

## Summary

Introduce a single **`IWorkTrackerAdapter`** abstraction that the pipeline, cost, and binding layers
depend on instead of the ADO-specific `IBoardsClient` + `AdoTelemetryPreflightService`. The existing
Azure DevOps code is refactored *behind* the adapter as the reference implementation (no behaviour
change), and a **Jira** adapter is added as the second implementation that proves portability. Work
items are referenced by an opaque **`WorkItemRef`** (numeric for ADO, string-key for Jira); telemetry +
cost fields are addressed by **tracker-neutral logical names** that each adapter maps to its native
field reference. The active adapter is resolved through an **`IWorkTrackerAdapterProvider`** seam from
connector config — a single active tracker per instance now, with the seam designed so per-project
routing is a later additive change. The tracker-neutral cost core (ledger, binding-key minting,
dev-usage ingest) is reused **unchanged**. Rollup stays native per tracker (ADO Analytics; Jira Advanced
Roadmaps), and a tracker without native hierarchical aggregation reports that limitation.

## Technical Context

**Language/Version**: C# / .NET 8

**Primary Dependencies**: Semantic Kernel Process Framework; existing `AzureDevOpsBoardsClient` (ADO REST)
+ `AdoTelemetryPreflightService`; new Jira REST integration (`HttpClient`); EF Core / SQLite.

**Storage**: SQLite via `PipelineDbContext` — the existing `CostLedgerEntries` and `BindingWorkItemMap`
tables (the latter's `WorkItemId` widens from `int` to an opaque string ref). No new store.

**Testing**: xUnit — unit (mocked adapters / fake REST), integration (real Jira/ADO behind the adapter),
Playwright E2E unaffected. TDD per Article V.

**Target Platform**: .NET 8 web app (`DBAIAzure.Web` / Kestrel), Azure Container Apps.

**Project Type**: Web service + SK Process pipeline.

**Performance Goals**: N/A beyond existing — adapter calls are best-effort and off the run's hot path.

**Constraints**: **No regression** for ADO (SC-001); all adapter operations best-effort and must never
block a pipeline run (FR-012); no tracker-specific types in the core layers (FR-009); secrets via the
existing connector-config + vault model (Article IX).

**Scale/Scope**: Two adapters (ADO refactor + Jira new) behind one contract; a third is proven by the
contract, not built.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Article VII — Framework-First (SK Process Framework)**: ✅ The SK framework provides orchestration,
  state, and HITL — it does **not** provide a work-tracker abstraction. `IWorkTrackerAdapter` is custom
  infrastructure against a documented gap (vendor portability), justified at the component. Rollup
  remains **native per tracker** (ADO Analytics / Jira Advanced Roadmaps) — no custom rollup engine is
  built. **PASS.**
- **Article IV — Code Quality**: ✅ Self-documenting names; the adapter is a narrow seam; `WorkItemRef`
  is a typed value, not a stringly-typed leak.
- **Article V — Testing (three-layer)**: ✅ Unit (fake adapter), integration (real tracker behind the
  contract), contract tests assert each adapter honours the same behaviour.
- **Article IX — Secrets**: ✅ Jira credentials via the connector-config + vault model; never in source.
- **Article XI — Output restraint**: ✅ No new dashboards; rollup is native tooling.

No violations → **Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/018-work-tracker-adapter/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (IWorkTrackerAdapter contract)
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/DBAIAzure.Core/
├── Interfaces/
│   ├── IWorkTrackerAdapter.cs          # NEW — the abstraction (create/upsert/comment/set-fields/
│   │                                   #       resolve-by-binding/provision/rollup-capability)
│   ├── IWorkTrackerAdapterProvider.cs  # NEW — resolves the active adapter (the routing seam)
│   └── IBoardsClient.cs                # KEPT — becomes the ADO adapter's internal ADO seam
├── Models/WorkTracker/
│   ├── WorkItemRef.cs                  # NEW — opaque id (numeric ADO / string-key Jira)
│   ├── LogicalField.cs                 # NEW — tracker-neutral logical field-name constants
│   ├── ProvisioningResult.cs           # NEW — per-tracker provisioning outcome
│   └── RollupCapability.cs             # NEW — native | none + the native tool name

src/DBAIAzure.Web/Integrations/
├── AzureDevOps/
│   ├── AzureDevOpsWorkTrackerAdapter.cs  # NEW — wraps AzureDevOpsBoardsClient + AdoTelemetryPreflight
│   └── (existing ADO client + preflight, unchanged behaviour)
└── Jira/
    ├── JiraWorkTrackerAdapter.cs         # NEW — Jira REST: create/set/resolve/provision
    ├── JiraFieldProvisioner.cs           # NEW — field + context/screen association (idempotent)
    └── JiraFieldReferenceResolver.cs     # NEW — logical name → customfield_* (cached)

src/DBAIAzure.Web/Services/
├── CostProjectionService.cs            # EDIT — depend on IWorkTrackerAdapter + logical field keys
└── TelemetryWriteBackService.cs        # EDIT — same

src/DBAIAzure.Processes/Steps/
└── CreateWorkItemStep.cs               # EDIT — depend on IWorkTrackerAdapter + WorkItemRef

src/DBAIAzure.Storage/
└── (BindingWorkItemMap WorkItemId widens int → string ref)

tests/DBAIAzure.Tests/WorkTracker/      # NEW — contract tests + adapter unit tests
```

**Structure Decision**: Reuse the existing layered layout. `IWorkTrackerAdapter` lives in `Core` so the
pipeline/cost layers depend only on it. ADO and Jira adapters live in `Web/Integrations` (where the
connectors already are). `IBoardsClient` is **retained** as the ADO adapter's internal seam so the
ADO refactor is a wrap, not a rewrite — protecting SC-001 (no regression).

## Design Highlights

- **The seam, not a rewrite.** The pipeline/cost layers swap `IBoardsClient` → `IWorkTrackerAdapter`.
  The ADO adapter delegates to the existing `AzureDevOpsBoardsClient` + `AdoTelemetryPreflightService`,
  converting `WorkItemRef ↔ int` and logical field names → `Custom.*` at the boundary. ADO behaviour is
  byte-for-byte preserved.
- **Logical fields.** `default-telemetry-config.json` field references become tracker-neutral logical
  names (e.g. `AIRuntimeCostUSD`). The ADO adapter maps logical → `Custom.AIRuntimeCostUSD` (its current
  refnames, so live ADO fields are unaffected); the Jira adapter resolves logical → `customfield_NNNNN`
  by field name at provision time and caches it.
- **Provisioning is per-tracker.** ADO keeps the process-detection / inherited-WIT-materialization flow
  (spec-009 + the #46/#47 fixes). Jira creates a global field, then associates a **field context** to the
  relevant issue types + project and adds it to the right screens — idempotent (look up by name first).
- **Resolution seam.** `IWorkTrackerAdapterProvider` returns the single active adapter from connector
  config today; its signature accepts a routing context so per-project selection is additive (FR-005).
- **Rollup honesty.** `RollupCapability` advertises the native tool; the UI/runbook surfaces "rollup via
  <tool>" and, for a tracker lacking native hierarchical aggregation, says so rather than omitting data.

## Phase 0 — Research (`research.md`)

Resolves the adapter contract shape, `WorkItemRef` identity model, logical-field mapping, the resolution
seam, Jira provisioning (field contexts/screens), Jira hierarchy + rollup, and the ADO no-regression
refactor strategy. No open NEEDS CLARIFICATION (FR-005 resolved in the spec).

## Phase 1 — Design & Contracts

- `data-model.md` — `WorkItemRef`, `LogicalField`, `ProvisioningResult`, `RollupCapability`, the widened
  `BindingWorkItemMap`, and the logical→native field mapping per adapter.
- `contracts/work-tracker-adapter.md` — the `IWorkTrackerAdapter` + `IWorkTrackerAdapterProvider`
  contracts, the per-method behaviour each adapter must honour, and the contract-test matrix.
- `quickstart.md` — scenarios: ADO no-regression, Jira create+bind+project, idempotent provisioning,
  cross-tracker dev-usage, rollup capability surfacing.

## Phase 2 — Tasks

`/speckit-tasks` will phase this: Setup → Foundational (contract + models + provider seam) → US1 (ADO
refactor behind adapter, no regression) → US2 (provisioning per tracker) → US3 (binding/projection
parity) → US4 (rollup capability) → Polish. Jira adapter rides US1–US4 as the second implementation.
