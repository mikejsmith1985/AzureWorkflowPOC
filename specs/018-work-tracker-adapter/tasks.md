# Tasks: Multi Work-Tracker Support via a Work-Tracker Adapter

**Feature**: `specs/018-work-tracker-adapter` · **Branch**: `feature/018-work-tracker-adapter`
**Inputs**: [plan.md](./plan.md), [spec.md](./spec.md), [data-model.md](./data-model.md),
[contracts/work-tracker-adapter.md](./contracts/work-tracker-adapter.md), [research.md](./research.md)

TDD per Constitution Article V: each story's tests precede its implementation. `[P]` = parallelizable
(different files, no incomplete dependencies).

- **US1 (P1)** — Portability MVP: the `IWorkTrackerAdapter` abstraction + ADO refactored behind it (no
  regression) + a Jira adapter (create/set/bind). *Proves the design.*
- **US2 (P2)** — Tracker-neutral field provisioning (ADO process/WIT + Jira field/context/screen).
- **US3 (P3)** — Binding/projection parity, incl. cross-tracker dev-usage ingest.
- **US4 (P4)** — Rollup capability per tracker, gaps surfaced.

> Amended after `/speckit-analyze`: **C1** folded into T008 (also widen `CostLedgerEntry.WorkItemId` for
> Jira anchors) + data-model §6 fix; **U1** added as **T037** (FR-011 tracker-switch data-preservation test).

---

## Phase 1: Setup

- [x] T001 **Superseded** — tracker selection uses the `WorkTracker:Active` config value (resolved by
  `WorkTrackerAdapterProvider`), not a `ConnectorType.Jira` enum entry; no enum change needed.
- [x] T002 [P] Jira connection settings + secret keys documented in `docs/jira-setup.md`.

## Phase 2: Foundational (blocking prerequisites for all stories)

- [x] T003 [P] Create `WorkItemRef` value type in `src/DBAIAzure.Core/Models/WorkTracker/WorkItemRef.cs`.
- [x] T004 [P] Create `LogicalField` constants (the 16 tracker-neutral field names) in
  `src/DBAIAzure.Core/Models/WorkTracker/LogicalField.cs`.
- [x] T005 [P] Create `WorkItemType` enum, `ProvisioningResult`, `RollupCapability`, `WorkRoutingContext`
  in `src/DBAIAzure.Core/Models/WorkTracker/`.
- [x] T006 Create `IWorkTrackerAdapter` (create/upsert/comment/set-fields/resolve-by-binding/provision/
  rollup-capability) in `src/DBAIAzure.Core/Interfaces/IWorkTrackerAdapter.cs`.
- [x] T007 Create `IWorkTrackerAdapterProvider` (`GetAdapter(WorkRoutingContext?)`) in
  `src/DBAIAzure.Core/Interfaces/IWorkTrackerAdapterProvider.cs`.
- [x] T008 Widen storage to a tracker-neutral ref: `BindingWorkItemMapEntity.WorkItemId` `int→string` +
  `IBindingWorkItemMap`/`SqlBindingWorkItemMap` to `WorkItemRef`, **and `CostLedgerEntry(Entity).WorkItemId`
  `int?→string?`** (so a Jira anchor key fits — C1); update the spec-017 map + ledger tests accordingly
  (`tests/DBAIAzure.Tests/CostTracking/BindingWorkItemMapTests.cs`, `CostLedgerTests.cs`).
- [x] T009 Create the shared contract-test harness asserting the C3 behaviour matrix against any registered
  adapter, in `tests/DBAIAzure.Tests/WorkTracker/WorkTrackerAdapterContractTests.cs`.

---

## Phase 3: US1 — Portability MVP (abstraction + ADO refactor + Jira create/set/bind) (P1) 🎯 MVP

**Goal**: With ADO selected, behaviour is unchanged; with Jira selected, the same pipeline path creates an
issue, stamps the binding key, and sets cost fields — no tracker-specific code in the core.
**Independent test**: ADO regression suite green; a Jira-configured run creates an issue with `BIND-*` +
cost fields; the core pipeline contains no tracker-specific types.

### Tests (write first)
- [x] T010 [P] [US1] Contract tests against the **ADO** adapter (fake ADO client) — create/set/resolve/
  best-effort, in `tests/DBAIAzure.Tests/WorkTracker/AzureDevOpsAdapterTests.cs`.
- [x] T011 [P] [US1] Contract tests against the **Jira** adapter (fake Jira REST handler) —
  create/set/resolve, in `tests/DBAIAzure.Tests/WorkTracker/JiraAdapterTests.cs`.
- [x] T012 [P] [US1] Unit: ADO resolver maps logical→`Custom.<logical>`; Jira resolver maps logical→
  `customfield_*` by name, in `tests/DBAIAzure.Tests/WorkTracker/FieldReferenceResolverTests.cs`.

### Implementation
- [x] T013 [P] [US1] ADO field reference resolver (logical → `Custom.<logical>`) in
  `src/DBAIAzure.Web/Integrations/AzureDevOps/AdoFieldReferenceResolver.cs`.
- [x] T014 [US1] `AzureDevOpsWorkTrackerAdapter` delegating to `AzureDevOpsBoardsClient` (create/upsert/
  comment/set via `UpdateFieldsAsync`), `WorkItemRef ↔ int`, logical→`Custom.*`, in
  `src/DBAIAzure.Web/Integrations/AzureDevOps/AzureDevOpsWorkTrackerAdapter.cs`.
- [x] T015 [US1] Jira REST client + `JiraWorkTrackerAdapter` (create issue, set fields by resolved
  `customfield_*`, resolve binding via JQL) + `JiraFieldReferenceResolver`, in
  `src/DBAIAzure.Web/Integrations/Jira/`.
- [x] T016 [US1] `WorkTrackerAdapterProvider` resolving the single active adapter from connector config
  (routing context reserved for later) in `src/DBAIAzure.Web/Services/WorkTrackerAdapterProvider.cs`;
  register both adapters + the provider in `src/DBAIAzure.Web/Program.cs`.
- [x] T017 [US1] Migrate `CreateWorkItemStep` from `IBoardsClient`/`int` to `IWorkTrackerAdapter`/
  `WorkItemRef` (parent + anchor as refs) in `src/DBAIAzure.Processes/Steps/CreateWorkItemStep.cs`.
- [~] T018 [US1] `CostProjectionService` migrated to the adapter + logical fields (done, #49).
  `TelemetryWriteBackService` (the per-item token snapshot) is **deferred** — still ADO-numeric, skipped
  for non-numeric (Jira) refs; a Jira token-snapshot follow-up. Cost + binding are fully tracker-neutral.
- [x] T019 [US1] Migrate binding-map usage (creation + ingest) to `WorkItemRef`.
- [x] T020 [US1] Inject the provider/active adapter into the phase kernel in `Program.cs`, replacing the
  direct `IBoardsClient` injection (kept only as the ADO adapter's internal dependency).

---

## Phase 4: US2 — Tracker-neutral field provisioning (P2)

**Goal**: One provisioning step makes the logical fields usable on the relevant item types for whichever
tracker is active — idempotently.
**Independent test**: run provisioning twice per tracker; first run makes fields usable, second is a no-op.

### Tests (write first)
- [x] T021 [P] [US2] Contract test: `ProvisionFieldsAsync` is idempotent (second run no-op, `IsSuccess`)
  for both adapters, in `tests/DBAIAzure.Tests/WorkTracker/ProvisioningContractTests.cs`.
- [x] T022 [P] [US2] Unit: Jira provisioner find-or-create field → context (issue types+project) → screen
  sequence (fake REST), in `tests/DBAIAzure.Tests/WorkTracker/JiraFieldProvisionerTests.cs`.

### Implementation
- [x] T023 [US2] ADO `ProvisionFieldsAsync` delegates to `AdoTelemetryPreflightService` (preserving the
  #46/#47 inherited-process handling) in `AzureDevOpsWorkTrackerAdapter`.
- [x] T024 [US2] `JiraFieldProvisioner` — find-or-create global field, associate field context to the
  relevant issue types + project, add to screens; idempotent, in
  `src/DBAIAzure.Web/Integrations/Jira/JiraFieldProvisioner.cs`.
- [x] T025 [US2] Route the startup/admin provisioning through `provider.GetAdapter().ProvisionFieldsAsync`
  instead of calling `AdoTelemetryPreflightService` directly, in `Program.cs`.

---

## Phase 5: US3 — Binding/projection parity, cross-tracker ingest (P3)

**Goal**: Binding resolution + cost projection behave identically on any tracker, including the secret-gated
dev-usage ingest.
**Independent test**: dev-usage with a valid key raises the resolved item's cumulative dev cost; unknown key
→ unattributed — on both trackers.

### Tests (write first)
- [x] T026 [P] [US3] Unit: dev-usage ingest resolves binding via the adapter (attributed/unattributed),
  tracker-agnostic, in `tests/DBAIAzure.Tests/CostTracking/DevUsageIngestTests.cs` (extend).
- [x] T027 [P] [US3] Contract test: projection writes cost fields on the resolved ref for both adapters,
  in `tests/DBAIAzure.Tests/WorkTracker/ProjectionContractTests.cs`.

### Implementation
- [x] T028 [US3] `TelemetryIngestController` resolves via `IWorkTrackerAdapter.ResolveByBindingKeyAsync`
  (+ widened map) and projects via the adapter — no ADO assumptions, in
  `src/DBAIAzure.Web/Controllers/TelemetryIngestController.cs`.
- [x] T029 [US3] Confirm `CostProjectionService.ProjectAsync` targets a `WorkItemRef` through the adapter
  end-to-end (cross-tracker), in `src/DBAIAzure.Web/Services/CostProjectionService.cs`.

---

## Phase 6: US4 — Rollup capability per tracker (P4)

**Goal**: Each tracker reports how cost rolls up; a tracker lacking native hierarchical aggregation says so.
**Independent test**: ADO → `Native("ADO Analytics")`; Jira with Advanced Roadmaps → `Native`, without →
`RequiresAddOn` with an operator notice; per-item fields correct either way.

- [x] T030 [P] [US4] Unit: `GetRollupCapability` returns the right `RollupCapability` per adapter, in
  `tests/DBAIAzure.Tests/WorkTracker/RollupCapabilityTests.cs`.
- [x] T031 [US4] Implement `GetRollupCapability` per adapter + surface the notice; document ADO Analytics
  vs Jira Advanced Roadmaps in `docs/work-tracker-rollup.md`.

---

## Phase 7: Polish & Cross-Cutting

- [x] T032 [P] Update `CHANGELOG.md` under `[Unreleased]`.
- [x] T033 Run the full unit suite — confirm **ADO no-regression** (SC-001) except the known pre-existing
  `ConnectorSettings_WhenSaveClicked` bUnit failure.
- [x] T034 Code-quality pass against the constitution across changed files (adapter is a narrow seam; no
  tracker types in core).
- [~] T035 **Deferred** — ADO scenarios covered by the unit/contract suite; a **live Jira round-trip**
  needs a real Jira Cloud site (none wired into this environment), like the prior ADO live verification.
  Original line: Execute `quickstart.md` Scenarios A–F (ADO live; Jira live where a site is available — may defer
  like prior live round-trips).
- [x] T036 [P] Add a Jira connector-setup note to `docs/` + update project memory.

---

## Post-Analyze Remediation (C1 / U1)

- [x] T037 [US3] **(U1)** Test (FR-011): with cost-ledger + binding-map rows present, switching the active
  adapter (changing the configured tracker) leaves those rows intact and still resolvable — the ledger and
  binding data are tracker-neutral and survive a tracker change. File:
  `tests/DBAIAzure.Tests/WorkTracker/TrackerSwitchDataPreservationTests.cs`.

> C1 is folded into **T008** (widen `CostLedgerEntry.WorkItemId` alongside the binding map) and the
> data-model §6 wording fix — no separate task needed.

## Dependencies & Order

- **Setup (T001–T002)** → **Foundational (T003–T009)** → stories.
- **US1 (T010–T020)** is the MVP and the prerequisite for US2/US3/US4 (no adapter → nothing to provision,
  resolve, or roll up). The Jira adapter is introduced here and extended by later stories.
- **US2 / US3 / US4** are largely independent of each other but all depend on US1's adapter + provider.
- **T008** (widened binding map) is a prerequisite for US1's binding migration (T019) and US3 (T028).
- **Polish (T032–T036)** last.

## Parallel Opportunities
- Foundational: T003, T004, T005 in parallel (distinct new files).
- US1 tests: T010, T011, T012 in parallel; T013 parallel with the test trio.
- ADO adapter (T014) and Jira adapter (T015) are different files → parallelizable once the contract (T006) lands.

## MVP Scope
**US1 (T001–T020)** — the `IWorkTrackerAdapter` abstraction with ADO refactored behind it (no regression)
and a Jira adapter proving a second tracker. This is the shippable proof of portability; provisioning,
parity, and rollup build on it.
