# Tasks: Two-Dimensional AI Cost Tracking on the Work Hierarchy

**Feature**: `specs/017-ai-cost-tracking` · **Branch**: `feature/017-ai-cost-tracking`
**Inputs**: [plan.md](./plan.md), [spec.md](./spec.md), [data-model.md](./data-model.md),
[contracts/cost-tracking.md](./contracts/cost-tracking.md), [research.md](./research.md)

TDD per Constitution Article V: each story's tests precede its implementation. `[P]` = parallelizable
(different files, no incomplete dependencies).

- **US1 (P1)** — Binding key: minted at intake, DoR-enforced, persisted to ADO + ServiceNow. *The
  universal join everything else depends on.*
- **US2 (P2)** — Runtime cost ledger: append-only, one-run→single-anchor, cumulative; per-item projection.
- **US3 (P3)** — Development cost: secret-gated ingest → dev ledger entries (incl. unattributed); projection.
- **US4 (P4)** — Rollup: ADO Analytics / Power BI view over the per-item cost fields.

> Amended after `/speckit-analyze` with **T035–T038**: the binding→work-item resolution map (**C1**),
> ServiceNow write-back as its own caveated task (**C2**), and an FR-011 non-blocking test (**U1**).
> FR-003's app/tooling boundary was clarified in the spec (**I1**).

---

## Phase 1: Setup

- [x] T001 Move aside any existing dev DB so the new `CostLedgerEntry` table provisions cleanly: rename
  `src/DBAIAzure.Web/pipeline.db*` to `*.bak` (`EnsureCreated`, not migrations).
- [x] T002 Add `WebhookSecrets:Telemetry` to `appsettings`/config docs for the dev-usage endpoint.

## Phase 2: Foundational (blocking prerequisites for all stories)

- [x] T003 [P] Create `CostDimension` enum (`Runtime`/`Development`) + `CostLedgerEntry` model in
  `src/DBAIAzure.Core/Models/AdoTelemetry/CostLedgerEntry.cs`.
- [x] T004 [P] Create `ICostLedger` (`AppendAsync`, `GetTotalsAsync`) + `CostTotals` record in
  `src/DBAIAzure.Core/Interfaces/ICostLedger.cs`.
- [x] T005 [P] Create `IBindingKeyMinter` (`Mint`, `IsValid`) in
  `src/DBAIAzure.Core/Interfaces/IBindingKeyMinter.cs`.
- [x] T006 Add `CostLedgerEntryEntity` + `DbSet` to
  `src/DBAIAzure.Storage/Entities/CostLedgerEntryEntity.cs` and `src/DBAIAzure.Storage/PipelineDbContext.cs`.
- [x] T007 Implement `SqlCostLedger : ICostLedger` (append-only; `GetTotalsAsync` sums by binding key +
  dimension; never throws) in `src/DBAIAzure.Storage/Repositories/SqlCostLedger.cs`.
- [x] T008 Add the three custom fields (`Custom.CostBindingKey`, `Custom.AIRuntimeCostUSD`,
  `Custom.AIDevCostUSD`) to `src/DBAIAzure.Core/Resources/default-telemetry-config.json` (UserStory + Task).

---

## Phase 3: US1 — Binding key (mint → DoR → persist) (P1) 🎯 MVP

**Goal**: Every ticket carries a minted, DoR-enforced, source-neutral binding key, written to ADO + SNow.
**Independent test**: a phase run has a `CostBindingKey` from intake; a blank key fails DoR; the created
work item has `Custom.CostBindingKey` and the SNow ticket carries the same key.

### Tests (write first)
- [x] T009 [P] [US1] Unit test: `BindingKeyMinter.Mint` is branch-safe + unique; `IsValid` rejects
  blank/whitespace/slashes, in `tests/DBAIAzure.Tests/BindingKeyMinterTests.cs`.
- [ ] T010 [P] [US1] Unit test: `PhaseValidationStep` fails DoR when binding key is missing/invalid.
  **Deferred** — `KernelProcessStepContext` is sealed (can't capture the emitted event in a unit test);
  the guard logic is `IBindingKeyMinter.IsValid` (covered by `BindingKeyMinterTests`) + quickstart A.
  Implementation (T015) is done.
- [x] T011 [P] [US1] Unit test: `SpecKitSignalMapper`/intake places a minted key on the initial state,
  in `tests/DBAIAzure.Tests/SpecKitSignalMapperTests.cs`.

### Implementation
- [x] T012 [P] [US1] Implement `BindingKeyMinter : IBindingKeyMinter` (`BIND-<base32>`) in
  `src/DBAIAzure.Web/Services/BindingKeyMinter.cs`; register in `src/DBAIAzure.Web/Program.cs`.
- [x] T013 [US1] Add `CostBindingKey` to `src/DBAIAzure.Core/Models/PhaseHandlerState.cs`.
- [x] T014 [US1] Mint at intake: `SpecKitWebhookController` injects `IBindingKeyMinter`, mints, and
  `SpecKitSignalMapper.ToInitialState` carries it onto the state.
- [x] T015 [US1] Enforce at DoR: `src/DBAIAzure.Processes/Steps/ValidationStep.cs` fails when the binding
  key is absent/invalid.
- [x] T016 [US1] Persist on creation (ADO): `CreateWorkItemStep` writes `Custom.CostBindingKey` via
  `IBoardsClient.UpdateFieldsAsync`. (ServiceNow write-back → **T037**; binding→work-item map → **T036**.)

---

## Phase 4: US2 — Runtime cost ledger + projection (P2)

**Goal**: Each run appends one runtime ledger entry on its anchor; the work item's cumulative runtime
cost reflects the ledger; multi-item runs are not duplicated.
**Independent test**: a Plan run creating N Tasks yields exactly one `Runtime` ledger entry on the Epic;
`Custom.AIRuntimeCostUSD` equals the ledger sum; re-runs accumulate.

### Tests (write first)
- [x] T017 [P] [US2] Unit test: `SqlCostLedger.GetTotalsAsync` sums runtime cost by binding key,
  cumulative across appends (no overwrite), in `tests/DBAIAzure.Tests/CostTracking/CostLedgerTests.cs`.
- [ ] T018 [P] [US2] Unit test: one run → one runtime entry on the anchor; multi-item run not duplicated.
  **Deferred** — exercises `CreateWorkItemStep` (sealed `KernelProcessStepContext`); the append-once-on-anchor
  logic is implemented (T020) and the cumulative/no-overwrite ledger behaviour is covered by `CostLedgerTests`
  + quickstart Scenario B.
- [x] T019 [P] [US2] Unit test: per-item `Custom.AIRuntimeCostUSD` equals the ledger total for the key,
  in `tests/DBAIAzure.Tests/CostTracking/CostProjectionTests.cs`.

### Implementation
- [x] T020 [US2] Replace the per-child telemetry stamping in `CreateWorkItemStep.WriteTelemetryAsync`
  with a single runtime `ICostLedger.AppendAsync` on the **anchor** work item (binding key + run cost).
- [x] T021 [US2] Implement the per-item cost projection (recompute `Custom.AIRuntimeCostUSD` from
  `GetTotalsAsync` after append) in `src/DBAIAzure.Web/Services/CostProjectionService.cs`; wire into the
  write path.
- [x] T022 [US2] Register ledger + projection in `src/DBAIAzure.Web/Program.cs` and inject into the
  phase-handler kernel (alongside the existing telemetry services).

---

## Phase 5: US3 — Development cost ingest (P3)

**Goal**: Coding-agent sessions post usage with a binding key → Development ledger entries; unresolvable
keys are recorded unattributed; the work item's cumulative dev cost reflects the ledger.
**Independent test**: a valid-key POST increases `Custom.AIDevCostUSD` (accumulates on re-post); an
unknown-key POST is recorded `IsUnattributed`; both return 202.

### Tests (write first)
- [x] T023 [P] [US3] Unit test: ingest with a valid key → attributed `Development` entry, cost re-priced
  via `ModelPricing` when absent, in `tests/DBAIAzure.Tests/CostTracking/DevUsageIngestTests.cs`.
- [x] T024 [P] [US3] Unit test: ingest with an unknown key → entry with `IsUnattributed = true`,
  response `attributed:false` (FR-010), same file.
- [x] T025 [P] [US3] Unit test: missing/blank `X-Telemetry-Secret` → 401, in
  `tests/DBAIAzure.Tests/CostTracking/DevUsageAuthTests.cs`.

### Implementation
- [x] T026 [P] [US3] Create `DevUsageIngestPayload` DTO in
  `src/DBAIAzure.Web/Integrations/Telemetry/DevUsageIngestPayload.cs`.
- [x] T027 [US3] Implement `TelemetryIngestController` (`POST /api/telemetry/dev-usage`, secret-gated via
  the existing `WebhookSecretValidator`) in `src/DBAIAzure.Web/Controllers/TelemetryIngestController.cs`:
  resolve binding key → work item via **`IBindingWorkItemMap.ResolveAsync`** (T035), append a
  `Development` ledger entry (unattributed when unresolved), re-price via `ModelPricing`.
- [x] T028 [US3] Extend the cost projection to recompute `Custom.AIDevCostUSD` from the ledger after a
  dev append, in `src/DBAIAzure.Web/Services/CostProjectionService.cs`.

---

## Phase 6: US4 — Rollup (ADO Analytics) (P4)

**Goal**: A leader sees runtime + dev totals per Feature/Initiative/Project, summed from descendants.
**Independent test**: the Analytics/Power BI view sums the per-item cost fields up the tree, dimensions
distinguishable (SC-001, SC-006).

- [x] T029 [US4] Author the ADO Analytics (OData) saved query / Power BI report definition that sums
  `Custom.AIRuntimeCostUSD` + `Custom.AIDevCostUSD` up Task→Story→Feature→Initiative→Project, in
  `docs/ado-cost-analytics.md` (query + report def; not application code — Framework-First).

---

## Phase 7: Polish & Cross-Cutting

- [x] T030 [P] Write the org-rollout runbook: how to configure a coding agent (Claude Code) to emit usage
  and bind it to a ticket key, in `docs/dev-agent-telemetry-setup.md`.
- [x] T031 [P] Update `CHANGELOG.md` under `[Unreleased]`.
- [x] T032 Run the full unit suite (`dotnet test tests/DBAIAzure.Tests`) — confirm green except the known
  pre-existing `ConnectorSettings_WhenSaveClicked` bUnit failure.
- [x] T033 Code-quality pass against the constitution across all changed files.
- [ ] T034 Execute `quickstart.md` Scenarios A–D (evidence per Article X; live ADO/agent round-trip may
  defer like spec-016 T035 until ADO/Azure are available).

---

## Post-Analyze Remediation (C1 / C2 / U1)

- [x] T035 [P] **(C1)** Create `IBindingWorkItemMap` (`PutAsync`/`ResolveAsync`) + `BindingWorkItemMapEntity`
  + `DbSet` + `SqlBindingWorkItemMap`, with a unit test that put-then-resolve round-trips and an unknown
  key resolves to null. Files: `src/DBAIAzure.Core/Interfaces/IBindingWorkItemMap.cs`,
  `src/DBAIAzure.Storage/Entities/BindingWorkItemMapEntity.cs`, `src/DBAIAzure.Storage/PipelineDbContext.cs`,
  `src/DBAIAzure.Storage/Repositories/SqlBindingWorkItemMap.cs`,
  `tests/DBAIAzure.Tests/CostTracking/BindingWorkItemMapTests.cs`. **Foundational for US3 (T027).**
- [x] T036 [US1] **(C1)** Populate the map in `CreateWorkItemStep` (`PutAsync(bindingKey, anchorWorkItemId)`)
  when the work item is created, in `src/DBAIAzure.Processes/Steps/CreateWorkItemStep.cs`.
- [x] T037 [US1] **(C2)** ServiceNow write-back of the binding key: first verify the ServiceNow connector
  supports setting a field (`src/DBAIAzure.Web/Integrations/ServiceNow/*`). If it does, add the write to
  `CreateWorkItemStep`; **if the connector is read-only, descope to a follow-up** and record that in
  `CHANGELOG.md` + spec Out of Scope (resolution still works via the map + ADO field).
- [x] T038 [P] [US2] **(U1)** Non-blocking test (FR-011): a throwing `ICostLedger`/projection does NOT
  bubble out of the run/ingest path, in `tests/DBAIAzure.Tests/CostTracking/CostBestEffortTests.cs`.

## Dependencies & Order

- **Setup (T001–T002)** → **Foundational (T003–T008)** → stories.
- **US1 (T009–T016)** is the MVP and the prerequisite for US2/US3 (no binding key → nothing to attribute).
- **US2 (T017–T022)** and **US3 (T023–T028)** are independent of each other but both touch
  `CostProjectionService` and `Program.cs` — sequence those shared-file edits.
- **T035 (binding→work-item map) is a prerequisite for US3 T027** (resolution); **T036** (populate the
  map) rides with US1 creation. T037 (SNow write-back) is conditional on connector capability.
- **US4 (T029)** needs the per-item cost fields populated by US2/US3.
- **Polish (T030–T034)** last.

## Parallel Opportunities
- Foundational: T003, T004, T005 in parallel (distinct new files).
- US1 tests: T009, T010, T011 in parallel; T012 in parallel with T013.
- US2 / US3 test trios each parallelizable.

## MVP Scope
**US1 (T001–T016)**: the binding key minted, DoR-enforced, and persisted to ADO + ServiceNow — the
universal join. Nothing downstream attributes without it, so it is the first shippable increment.
