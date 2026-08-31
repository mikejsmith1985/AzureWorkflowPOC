---
description: "Task list for feature 013 — Point at a Repo, Run Its App in a Throwaway Container, Monitor It"
---

# Tasks: Point at a Repo, Run Its App in a Throwaway Container, Monitor It

## Status (reconciled 2026-08-31)

**Shipped.** The only open item is **T055**, a `quickstart.md` scenarios 1–4 run capturing behavioral
evidence (Article X) — verification, not development.

---

**Input**: Design documents from `specs/013-repo-app-monitoring/`

**Prerequisites**: plan.md ✅, spec.md ✅ (FR-001…FR-018), research.md ✅, data-model.md ✅, contracts/ ✅

**Tests**: INCLUDED and test-first — the project constitution (Article V) mandates Red → Green →
Refactor with three-layer separation: unit (100% mocked), integration (real infrastructure /
testcontainers-style), E2E (Playwright via `scripts/run-e2e.ps1`).

**Organization**: Tasks are grouped by user story (US1–US4) so each story is independently
implementable and testable. US1–US3 are P1; US4 is P2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3 / US4 (omitted for Setup, Foundational, Polish)
- Exact file paths are included in each description.

## Path Conventions

Multi-project .NET solution (per plan.md): `src/DBAIAzure.{Core,Connectors,Processes,Storage,Web}`,
tests in `tests/DBAIAzure.Tests` (unit + integration) and `tests/DBAIAzure.E2ETests` (Playwright).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and dependencies.

- [x] T001 Add the `Docker.DotNet` package reference to `src/DBAIAzure.Connectors/DBAIAzure.Connectors.csproj`
- [x] T002 [P] Create source folders `src/DBAIAzure.Connectors/Apps/`, `src/DBAIAzure.Processes/Monitoring/`, and test folders `tests/DBAIAzure.Tests/Apps/` and `tests/DBAIAzure.Tests/Integration/`
- [x] T003 [P] Add a tiny buildable/runnable fixture app for integration tests under `tests/DBAIAzure.Tests/Fixtures/sample-app/` (with documented build + run commands)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain models, storage, repositories, and shared UI/DI that every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T004 [P] Create `AppStatus` enum (Registered/Building/Ready/BuildFailed/Running) in `src/DBAIAzure.Core/Models/AppStatus.cs`
- [x] T005 [P] Create `RunOutcome` enum + `AppBuildResult` and `AppRunResult` records in `src/DBAIAzure.Core/Models/AppBuildResult.cs` and `src/DBAIAzure.Core/Models/AppRunResult.cs`
- [x] T006 [P] Create `MonitoredApp` model (name, owner id, repo path, branch, build/run cmd, status, results, linked workflow id, timestamps) in `src/DBAIAzure.Core/Models/MonitoredApp.cs`
- [x] T007 [P] Create `AppExecutionRequest` + `ExecutionMode` enum in `src/DBAIAzure.Core/Models/AppExecutionRequest.cs`
- [x] T008 [P] Create `AppMonitoringHeartbeat` and `AppRaisedIssue` models in `src/DBAIAzure.Core/Models/AppMonitoringHeartbeat.cs` and `src/DBAIAzure.Core/Models/AppRaisedIssue.cs`
- [x] T009 [P] Define `IAppRegistryRepository` (per contracts/app-registry-repository.md) in `src/DBAIAzure.Core/Interfaces/IAppRegistryRepository.cs`
- [x] T010 [P] Define `IAppExecutor`, `IAppMonitoringService`, `IAppHeartbeatStore` (per contracts/) in `src/DBAIAzure.Core/Interfaces/IAppExecutor.cs`, `IAppMonitoringService.cs`, `IAppHeartbeatStore.cs`
- [x] T011 Create EF entities `MonitoredAppRecord`, `AppMonitoringHeartbeatRecord`, `AppRaisedIssueRecord` in `src/DBAIAzure.Storage/Entities/`
- [x] T012 Add DbSets + `OnModelCreating` config (keys, indexes, `(OwnerId,Name)` unique) in `src/DBAIAzure.Storage/PipelineDbContext.cs`
- [x] T013 Add idempotent `CREATE TABLE IF NOT EXISTS` + indexes for `MonitoredApps`, `AppMonitoringHeartbeats`, `AppRaisedIssues` to the startup DDL block in `src/DBAIAzure.Web/Program.cs`
- [x] T014 Implement `SqliteAppHeartbeatStore` (heartbeat record/read + raised-issue dedup) in `src/DBAIAzure.Storage/Repositories/SqliteAppHeartbeatStore.cs`
- [x] T015 [P] Create `AppStatusBadge.razor` (status → colored indicator, parallels `ConnectorStatusBadge`) in `src/DBAIAzure.Web/Shared/AppStatusBadge.razor`
- [x] T016 Add the "Apps" link to primary navigation in `src/DBAIAzure.Web/Shared/MainLayout.razor`
- [x] T017 Create the `SqliteAppRegistryRepository` skeleton (implements `IAppRegistryRepository` with basic CRUD wiring; validation added later in T020) in `src/DBAIAzure.Storage/Repositories/SqliteAppRegistryRepository.cs`, and register all foundational services (registry repo, heartbeat store) in DI in `src/DBAIAzure.Web/Program.cs`

**Checkpoint**: Foundation ready — user stories can now begin.

---

## Phase 3: User Story 1 — Register a local repo as a monitored app (Priority: P1) 🎯 MVP

**Goal**: A user registers a repo by local path (name, branch, build/run commands), and it persists
(owner-scoped) and lists with a status indicator.

**Independent Test**: Register a valid app → it appears as **Registered** and survives reload;
invalid registrations (duplicate name per owner, bad path, missing run command) are rejected clearly.

### Tests for User Story 1 (write first — must FAIL before implementation) ⚠️

- [x] T018 [P] [US1] Unit tests for registration validation (duplicate name per owner, non-existent path, missing run command, persistence round-trip) in `tests/DBAIAzure.Tests/Apps/AppRegistryValidationTests.cs`
- [x] T019 [P] [US1] Unit test that a newly registered app is `Registered` and reloads intact in `tests/DBAIAzure.Tests/Apps/AppLifecycleTests.cs` (Registered-state cases)

### Implementation for User Story 1

- [x] T020 [US1] Complete `SqliteAppRegistryRepository` registration logic — `ExistsByNameAsync` + validation (unique `(OwnerId,Name)`, path existence, run command required) and remove — in `src/DBAIAzure.Storage/Repositories/SqliteAppRegistryRepository.cs` (extends the T017 skeleton)
- [x] T021 [US1] Create `Apps.razor` (`/apps`): register form (name, repo path, branch, build cmd, run cmd) + app list (name, repo, branch, `AppStatusBadge`, last build/run times) in `src/DBAIAzure.Web/Pages/Apps.razor`
- [x] T022 [US1] Wire register/list/remove to `IAppRegistryRepository` with owner scoping and inline validation errors in `src/DBAIAzure.Web/Pages/Apps.razor`
- [x] T023 [P] [US1] E2E test: register valid app (persists across reload) + rejected invalid registrations, in `tests/DBAIAzure.E2ETests/Tests/AppsPageTests.cs` (register section)

**Checkpoint**: US1 fully functional and independently testable.

---

## Phase 4: User Story 2 — Build and run the app in its own throwaway container (Priority: P1)

**Goal**: Build and run a registered app in fresh, disposable containers; capture outcome + redacted
logs; lifecycle Registered→Building→Ready/BuildFailed→Running→Ready; never hang.

**Independent Test**: Build then Run an app and observe the full status lifecycle with logs; a
timeout/start failure resolves to a recorded failure; no container is left behind.

### Tests for User Story 2 (write first — must FAIL before implementation) ⚠️

- [x] T024 [P] [US2] Unit tests for `SimAppExecutor` (Building→Ready, Running→Ready, synth summary/logs, never hangs) in `tests/DBAIAzure.Tests/Apps/SimAppExecutorTests.cs`
- [x] T025 [P] [US2] Unit tests for `BuildCommandAutoDetector` (npm / pip / dotnet / Dockerfile / none-fails) in `tests/DBAIAzure.Tests/Apps/BuildCommandAutoDetectorTests.cs`
- [x] T026 [P] [US2] Unit tests for `ContainerLogRedactor` (known secret values stripped) in `tests/DBAIAzure.Tests/Apps/ContainerLogRedactorTests.cs`
- [x] T027 [P] [US2] Unit tests for lifecycle transitions: timeout/start-failure never-stuck (FR-008) + concurrency guard (FR-016) in `tests/DBAIAzure.Tests/Apps/AppLifecycleTests.cs`
- [x] T028 [P] [US2] Env-gated integration test: real Docker build+run of `Fixtures/sample-app` in throwaway containers, asserting captured logs, cleanup, and timeout, in `tests/DBAIAzure.Tests/Integration/DockerAppExecutorTests.cs`

### Implementation for User Story 2

- [x] T029 [P] [US2] Implement `SimAppExecutor` (synthesized build/run outcomes; default/dev) in `src/DBAIAzure.Connectors/Apps/SimAppExecutor.cs`
- [x] T030 [P] [US2] Implement `BuildCommandAutoDetector` (ecosystem heuristics, R3) in `src/DBAIAzure.Connectors/Apps/BuildCommandAutoDetector.cs`
- [x] T031 [P] [US2] Implement `ContainerLogRedactor` (R6) in `src/DBAIAzure.Connectors/Apps/ContainerLogRedactor.cs`
- [x] T032 [US2] Implement `DockerAppExecutor` (Docker.DotNet: build container + run container, repo bind-mount read-only, per-app named-volume artifact, throwaway cleanup by container id, log capture+redaction, hard timeout, start-failure recorded) in `src/DBAIAzure.Connectors/Apps/DockerAppExecutor.cs` (depends on T030, T031)
- [x] T033 [US2] Add status-transition guard + single-in-flight concurrency guard to `SqliteAppRegistryRepository` `SetStatusAsync`/`SetBuildResultAsync`/`SetRunResultAsync` in `src/DBAIAzure.Storage/Repositories/SqliteAppRegistryRepository.cs`
- [x] T034 [US2] Add Build/Run actions on `Apps.razor` running the executor on a background `Task.Run`, broadcasting live status via the existing SignalR hub, in `src/DBAIAzure.Web/Pages/Apps.razor`
- [x] T035 [US2] Create `AppDetail.razor` (`/apps/{AppId}`): status header + build/run summary + expandable secret-redacted logs in `src/DBAIAzure.Web/Pages/AppDetail.razor`
- [x] T036 [US2] Register `IAppExecutor` implementations in DI (Sim as default binding) in `src/DBAIAzure.Web/Program.cs`
- [x] T037 [P] [US2] E2E test: build → run → status badges + log surfaces (sim mode) in `tests/DBAIAzure.E2ETests/Tests/AppsPageTests.cs` (build/run section)

**Checkpoint**: US1 + US2 both work independently (US2 runs anywhere via Sim; real via Docker).

---

## Phase 5: User Story 3 — Link a chosen saved workflow to monitor the running app (Priority: P1)

**Goal**: Link any saved workflow as the monitor; each cycle hands it a `MonitoringSnapshot` (status
+ latest run outcome + redacted log tail) and runs it on the existing execution path; detected
problems close the loop (one run per ongoing issue); per-app monitoring health is visible.

**Independent Test**: Link a saved workflow, run the app, confirm the workflow executes as monitor
against the snapshot; a detected issue produces exactly one new run/intake (deduped); monitoring
health updates; deleting the workflow reports unlinked without crashing.

### Tests for User Story 3 (write first — must FAIL before implementation) ⚠️

- [x] T038 [P] [US3] Unit tests for `AppMonitoringService.RunCycleAsync` (detection driven by the `MonitoringSnapshot` = status + last run outcome + redacted log tail (FR-018); runs linked workflow via `WorkflowExecutionOrchestrator`; new issue → one run; recurring issue deduped (FR-012); missing/deleted workflow → unlinked, no crash (FR-017)) in `tests/DBAIAzure.Tests/Apps/AppMonitoringServiceTests.cs`
- [x] T039 [P] [US3] Unit tests for heartbeat + dedup store (`IsRaisedAsync`/`RecordRaisedAsync`/`RecordCycleAsync`) in `tests/DBAIAzure.Tests/Apps/AppHeartbeatStoreTests.cs`

### Implementation for User Story 3

- [x] T040 [P] [US3] Create the `MonitoringSnapshot` DTO (app id/name, status, last run outcome/summary, redacted recent log tail) in `src/DBAIAzure.Core/Models/MonitoringSnapshot.cs`
- [x] T041 [US3] Implement `AppMonitoringService` — build a `MonitoringSnapshot`, resolve the linked workflow via `IWorkflowRepository`, execute it via the existing `WorkflowExecutionOrchestrator.StartRunAsync` with that snapshot as input, apply close-the-loop signature dedup, and handle unlinked/deleted workflows — in `src/DBAIAzure.Processes/Monitoring/AppMonitoringService.cs` (depends on T040)
- [x] T042 [US3] Implement `AppMonitoringBackgroundService` (hosted loop over enabled links; per-app heartbeat; one failing app never blocks others; configurable interval) in `src/DBAIAzure.Processes/Monitoring/AppMonitoringBackgroundService.cs`
- [x] T043 [US3] Add "Link workflow" picker (populated from `IWorkflowRepository.ListByOwnerAsync`) with link/unlink persisting via `SetLinkedWorkflowAsync`, on `src/DBAIAzure.Web/Pages/Apps.razor` and `src/DBAIAzure.Web/Pages/AppDetail.razor`
- [x] T044 [US3] Add monitoring-health panel (last cycle time, ok/fail, last error) + raised-runs list linking to Run History, on `src/DBAIAzure.Web/Pages/AppDetail.razor`
- [x] T045 [US3] Register `IAppMonitoringService` + the hosted `AppMonitoringBackgroundService` (with configurable interval) in DI in `src/DBAIAzure.Web/Program.cs`
- [x] T046 [P] [US3] E2E test: link a workflow and observe the monitoring-health panel update (sim mode) in `tests/DBAIAzure.E2ETests/Tests/AppsPageTests.cs` (monitoring section)

**Checkpoint**: US1 + US2 + US3 all independently functional.

---

## Phase 6: User Story 4 — Demonstrate the whole flow without real infrastructure (Priority: P2)

**Goal**: With no container engine (or demo mode), the full register→build→run→monitor flow runs on
the simulated executor with identical screens/status; an indicator shows Simulated vs Docker.

**Independent Test**: Force sim mode and run all of US1–US3 end to end with identical screens and
transitions; the active-executor indicator reads "Simulated".

### Tests for User Story 4 (write first — must FAIL before implementation) ⚠️

- [x] T047 [P] [US4] Unit tests for executor selection (Docker reachable → Docker; unavailable or demo mode → Sim) in `tests/DBAIAzure.Tests/Apps/AppExecutorSelectionTests.cs`

### Implementation for User Story 4

- [x] T048 [US4] Implement `AppExecutorSelector` (Docker reachability probe + demo-mode config → resolve `IAppExecutor`) in `src/DBAIAzure.Connectors/Apps/AppExecutorSelector.cs` and bind it in `src/DBAIAzure.Web/Program.cs`
- [x] T049 [P] [US4] Add a Simulated/Docker active-executor indicator to `src/DBAIAzure.Web/Pages/Apps.razor` and `src/DBAIAzure.Web/Pages/AppDetail.razor`
- [x] T050 [US4] Verify/adjust `AppMonitoringService` so close-the-loop monitoring works under the simulated executor (synthesized detections in the snapshot) in `src/DBAIAzure.Processes/Monitoring/AppMonitoringService.cs`
- [x] T051 [P] [US4] E2E test: full register→build→run→link→monitor flow in forced sim mode in `tests/DBAIAzure.E2ETests/Tests/AppsPageSimFlowTests.cs`

**Checkpoint**: Whole flow demonstrable anywhere; parity between sim and real surfaces.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, quality, and verification across all stories.

- [x] T052 [P] Update `CHANGELOG.md` with the repo-app build/run/monitor feature (Article VI)
- [x] T053 [P] Ensure every new public type/member has an XML doc comment explaining the "why" (Article IV) across new `src/DBAIAzure.*` files
- [x] T054 Review `DockerAppExecutor.cs` for Article II compliance (container stop/remove targets the specific created id; no wildcard) and Article IX (token never persisted, logs redacted)
- [ ] T055 Run `quickstart.md` scenarios 1–4 and capture behavioral evidence (Article X)
- [x] T056 Final `dotnet build` (zero warnings) + `dotnet test` green; E2E via `scripts/run-e2e.ps1`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies — start immediately.
- **Foundational (Phase 2)**: depends on Setup — **blocks all user stories**.
- **User Stories (Phases 3–6)**: each depends on Foundational. US1 → US2 → US3 is the natural order
  (US2 builds/runs the app US1 registered; US3 monitors what US2 runs). US4 depends on US2 (Sim
  executor) and US3 (monitoring) existing, so it comes last.
- **Polish (Phase 7)**: after all targeted stories.

### User Story Dependencies

- **US1 (P1)**: after Foundational. No dependency on other stories. 🎯 MVP.
- **US2 (P1)**: after Foundational; operates on apps from US1 but is independently testable (register
  a fixture app inline).
- **US3 (P1)**: after Foundational; monitors a running app (US2) but its cycle logic is unit-testable
  in isolation with a fake orchestrator + a stub `MonitoringSnapshot`.
- **US4 (P2)**: after US2 + US3 (it exercises both via the simulated executor).

### Within Each User Story

- Tests are written first and must FAIL before implementation (Article V).
- Models → repositories/services → UI → integration.
- Files touched by multiple tasks (`Program.cs`, `Apps.razor`, `AppDetail.razor`,
  `SqliteAppRegistryRepository.cs`, `AppLifecycleTests.cs`, `AppsPageTests.cs`,
  `AppMonitoringService.cs`) are **not** marked [P] with each other — they serialize.

### Parallel Opportunities

- Setup: T002, T003 in parallel.
- Foundational: T004–T010 (all new Core model/interface files) in parallel; T015 in parallel with them.
- US1 tests T018, T019 in parallel; E2E T023 in parallel with non-`Apps.razor` work.
- US2 tests T024–T028 in parallel; implementations T029, T030, T031 in parallel (distinct files).
- US3 tests T038, T039 in parallel; the `MonitoringSnapshot` DTO T040 in parallel with them.
- Polish: T052, T053 in parallel.

---

## Parallel Example: User Story 2

```bash
# Tests first (all distinct files — run together):
Task: "SimAppExecutor unit tests in tests/DBAIAzure.Tests/Apps/SimAppExecutorTests.cs"
Task: "BuildCommandAutoDetector unit tests in tests/DBAIAzure.Tests/Apps/BuildCommandAutoDetectorTests.cs"
Task: "ContainerLogRedactor unit tests in tests/DBAIAzure.Tests/Apps/ContainerLogRedactorTests.cs"
Task: "Lifecycle never-stuck + concurrency tests in tests/DBAIAzure.Tests/Apps/AppLifecycleTests.cs"

# Then independent implementations together:
Task: "Implement SimAppExecutor in src/DBAIAzure.Connectors/Apps/SimAppExecutor.cs"
Task: "Implement BuildCommandAutoDetector in src/DBAIAzure.Connectors/Apps/BuildCommandAutoDetector.cs"
Task: "Implement ContainerLogRedactor in src/DBAIAzure.Connectors/Apps/ContainerLogRedactor.cs"
```

---

## Implementation Strategy

### MVP First

1. Phase 1 Setup → Phase 2 Foundational → **Phase 3 US1** (register/list/persist).
2. **STOP and VALIDATE**: register apps, confirm persistence and validation. This is the minimal MVP.
3. First *useful demo*: add **US2** (build/run in sim mode — works without Docker) for an end-to-end
   register→build→run story on any machine.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → test → demo (registry).
3. US2 → test → demo (build/run, sim everywhere; Docker where available).
4. US3 → test → demo (workflow monitoring via snapshot + close-the-loop).
5. US4 → test → demo (forced-sim full-flow parity).
6. Polish → CHANGELOG, docs, quickstart evidence, green build/test.

### Parallel Team Strategy

After Foundational: one developer can take US1+US2 (registry/executor) while another preps US3
(monitoring service + snapshot + background loop) against the interfaces; US4 integrates both at the end.

---

## Notes

- [P] = different files, no incomplete-task dependency.
- Test-first per Article V (Red → Green → Refactor); verify each test fails before implementing.
- Commit after each task or logical group; keep generated/scratch output out of the tree (Article XI).
- Framework-first (Article VII): reuse `WorkflowExecutionOrchestrator`, `IWorkflowRepository`,
  run/observer/SignalR surfaces, the connector-config + `ISecretProtector` pattern, and
  `PipelineDbContext` — the only new infrastructure is the `Docker.DotNet` executor and the monitoring
  `BackgroundService`.
- Article II: never wildcard-kill containers/processes — target the specific created container id.
