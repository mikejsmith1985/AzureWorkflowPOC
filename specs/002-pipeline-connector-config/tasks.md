# Tasks: Pipeline Connector Configuration Modal

**Input**: Design documents from `specs/002-pipeline-connector-config/`

**Prerequisites**: [plan.md](plan.md) · [spec.md](spec.md) · [research.md](research.md) · [data-model.md](data-model.md) · [contracts/](contracts/)

**Tests**: Included per Article V of the project constitution — failing tests are written before or alongside each implementation.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story increment.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel with other [P] tasks in the same phase (different files, no shared dependencies)
- **[Story]**: Maps to a user story from spec.md (US1 = configure for first time, US2 = edit/rotate, US3 = status at a glance)
- Exact file paths are included in every description

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Wire ASP.NET Core Data Protection and configure the named HttpClient the new `ServiceNowClient` will use. These two changes both target `Program.cs` and must be done sequentially.

- [X] T001 Configure ASP.NET Core Data Protection (`builder.Services.AddDataProtection()`) in `src/DBAIAzure.Web/Program.cs`
- [X] T002 Configure named `HttpClient` for `ServiceNowClient` (base address from `IConfiguration`, 35 s timeout) in `src/DBAIAzure.Web/Program.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain types, interfaces, EF Core entity, repository implementation, unit tests, and DI registration. No user story implementation can begin until this phase is complete.

**⚠️ CRITICAL**: All user story phases depend on this phase being complete.

- [X] T003 [P] Define `ConnectorType` enum (`ServiceNow | AzureDevOps | LLM | Teams`) with XML doc in `src/DBAIAzure.Core/Models/ConnectorType.cs`
- [X] T004 [P] Define per-connector non-secret config records (`ServiceNowConnectorConfig`, `AzureDevOpsConnectorConfig`, `LlmConnectorConfig`) with XML doc in `src/DBAIAzure.Core/Models/` (one file per record)
- [X] T005 [P] Define `ConnectorConfig` record (Type, NonSecretConfig, HasSecrets, IsConfigured, LastUpdatedAt, LastTestResult) with XML doc in `src/DBAIAzure.Core/Models/ConnectorConfig.cs`
- [X] T006 [P] Define `ConnectorTestResult` record (Type, IsSuccess, Message, TestedAt) with XML doc in `src/DBAIAzure.Core/Models/ConnectorTestResult.cs`; also define `PipelinePreflightFailure` record (FailingConnectors: `IReadOnlyList<ConnectorTestResult>`) with XML doc in `src/DBAIAzure.Core/Models/PipelinePreflightFailure.cs` — this is the typed return used by orchestrators when pre-flight blocks a run (FR-018)
- [X] T007 [P] Define `IConnectorConfigRepository` interface (GetAsync, GetAllAsync, SaveAsync, GetDecryptedSecretsAsync, UpdateTestResultAsync) with XML doc per method in `src/DBAIAzure.Core/Interfaces/IConnectorConfigRepository.cs`
- [X] T008 [P] Define `IConnectorHealthChecker` interface (TestAsync, CheckAllAsync) with XML doc per method in `src/DBAIAzure.Core/Interfaces/IConnectorHealthChecker.cs`
- [X] T009 Add `ConnectorConfigRecord` EF entity (Id, ConnectorType unique-indexed, ConfigJson, EncryptedSecretsJson, IsConfigured, LastUpdatedAt, LastTestResult, LastTestMessage, LastTestedAt) in `src/DBAIAzure.Storage/Entities/ConnectorConfigRecord.cs`
- [X] T010 Add `DbSet<ConnectorConfigRecord> ConnectorConfigs` to `PipelineDbContext` in `src/DBAIAzure.Storage/PipelineDbContext.cs`
- [X] T011 Implement `SqliteConnectorConfigRepository` — all five methods from `IConnectorConfigRepository`; encrypt secrets via `IDataProtector.Protect()` on save; decrypt via `Unprotect()` only in `GetDecryptedSecretsAsync()`; `SaveAsync` with `null` secret preserves existing encrypted blob; `SaveAsync` always resets `LastTestResult` to null; `SaveAsync` uses a concurrency-safe upsert: `FindAsync(ConnectorType)` → `Update` if exists, `Add` if not, within a single `SaveChangesAsync()` call (prevents duplicate rows when two browser sessions save simultaneously — last write wins per spec edge case) in `src/DBAIAzure.Storage/Repositories/SqliteConnectorConfigRepository.cs`
- [X] T012 [P] Unit tests for `SqliteConnectorConfigRepository`: CRUD round-trip (in-memory SQLite), encryption round-trip (mock `IDataProtector`), null-secret-preserves-existing, test-result persistence, concurrent-write scenario (two sequential `SaveAsync` calls on the same `ConnectorType` produce exactly one row — no duplicate) in `tests/DBAIAzure.Tests/SqliteConnectorConfigRepositoryTests.cs`
- [X] T013 Register `IConnectorConfigRepository` → `SqliteConnectorConfigRepository` (scoped, consistent with existing repository registrations) in `src/DBAIAzure.Web/Program.cs`

**Checkpoint**: Foundation is ready — `IConnectorConfigRepository` is wired and the schema is live. User story phases can now begin.

---

## Phase 3: User Story 1 — Configure a connector for the first time (Priority: P1) 🎯 MVP

**Goal**: Operator can open a configuration modal, enter credentials for all four connectors, test each one with a genuine functional check against the real external service, save settings to the database, and trigger a pipeline run that uses those persisted credentials.

**Independent Test**: With no settings stored, open the modal, fill in all four connectors, click Test on each (confirm real round-trips per quickstart.md Scenarios 1 & 4), save, restart the app, reopen the modal — all four connectors show as configured. Trigger a pipeline run — the pre-flight passes and the run starts.

### Implementation for User Story 1

- [X] T014 [P] [US1] Implement `ServiceNowClient` with `TestConnectionAsync()` (Basic Auth, `GET /api/now/table/sys_properties?sysparm_limit=1`, resolve credentials from `IConnectorConfigRepository.GetDecryptedSecretsAsync()`; returns `ConnectorTestResult` with specific failure reason on 401/403/unexpected-shape) in `src/DBAIAzure.Connectors/ServiceNowClient.cs`
- [X] T015 [P] [US1] Add `TestConnectionAsync()` to `AzureDevOpsBoardsClient` (`GET {org}/_apis/projects/{project}?api-version=7.1`, FR-009); update PAT resolution to call `IConnectorConfigRepository.GetDecryptedSecretsAsync()` at invocation time instead of constructor injection (FR-014, hot-reload) in `src/DBAIAzure.Web/Integrations/AzureDevOps/AzureDevOpsBoardsClient.cs`
- [X] T016 [P] [US1] Add `TestConnectionAsync()` to `AnthropicChatCompletionService` (`POST /v1/messages` with `max_tokens: 5`, `content: "Respond with the word READY."`, confirm non-empty response, FR-010); update `ApiKey` + `Model` resolution to call `IConnectorConfigRepository.GetDecryptedSecretsAsync()` at each invocation (FR-014) in `src/DBAIAzure.Connectors/AnthropicChatCompletionService.cs`
- [X] T017 [P] [US1] Implement `TeamsConnectorTester.TestAsync()` (POST labeled Adaptive Card JSON to webhook URL, confirm Teams returns `1` with HTTP 200, FR-011); update `TeamsHitlNotifier` to resolve webhook URL from `IConnectorConfigRepository.GetDecryptedSecretsAsync()` at each send (FR-014) in `src/DBAIAzure.Connectors/TeamsConnectorTester.cs` and `src/DBAIAzure.Web/Integrations/Teams/TeamsHitlNotifier.cs`
- [X] T018 [US1] Implement `ConnectorHealthChecker` — `TestAsync()` dispatches to the correct connector client by `ConnectorType`; `CheckAllAsync()` uses `Task.WhenAll` over all four; calls `IConnectorConfigRepository.UpdateTestResultAsync()` after each test; CancellationToken with 35 s default timeout per test in `src/DBAIAzure.Connectors/ConnectorHealthChecker.cs`
- [X] T019 [P] [US1] Unit tests for `ConnectorHealthChecker` (mocked connector clients): all-pass returns all `IsSuccess = true`; single failure returns that result as `IsSuccess = false`; not-configured connector returns fail with "no credentials stored" message in `tests/DBAIAzure.Tests/ConnectorHealthCheckerTests.cs`
- [X] T020 [US1] Register `ServiceNowClient`, `TeamsConnectorTester`, and `IConnectorHealthChecker` → `ConnectorHealthChecker` (singleton) in `src/DBAIAzure.Web/Program.cs`
- [X] T021 [P] [US1] Add pre-flight check to `PipelineOrchestrator.StartRunAsync()` — call `IConnectorHealthChecker.CheckAllAsync()` before the SK process starts; if any result has `IsSuccess = false`, return a `PipelinePreflightFailure` record (from `src/DBAIAzure.Core/Models/PipelinePreflightFailure.cs`, defined in T006) containing the failing `ConnectorTestResult` entries; no process step executes (FR-018, SC-008) in `src/DBAIAzure.Processes/Pipeline/PipelineOrchestrator.cs`
- [X] T022 [P] [US1] Add pre-flight check to `PhaseHandlerOrchestrator.StartRunAsync()` — same pattern as T021: call `IConnectorHealthChecker.CheckAllAsync()`, return `PipelinePreflightFailure` if any connector fails (FR-018) in `src/DBAIAzure.Processes/Pipeline/PhaseHandlerOrchestrator.cs`
- [X] T023 [P] [US1] Implement `ConnectorSection.razor` — renders one connector's section: non-secret text inputs, masked password input for secret field, "Test Connection" button (calls `IConnectorHealthChecker.TestAsync()`), inline three-state status indicator: (1) "Not configured" when `ConnectorConfig` is null or `IsConfigured == false` — fields empty, no test button active; (2) "Configured — not yet tested" when `IsConfigured == true` and `LastTestResult == null`; (3) test result panel (success detail or specific failure reason) after a test runs; parameters: `ConnectorConfig?`, `ConnectorType`, `OnSave` callback in `src/DBAIAzure.Web/Shared/ConnectorSection.razor`
- [X] T024 [US1] Implement `ConnectorConfigModal.razor` — modal host: Tailwind overlay, four `ConnectorSection` instances, global "Save All" and "Close" buttons; injects `IConnectorConfigRepository` for load/save and `IConnectorHealthChecker` for test dispatch; toggle visibility via `bool _isVisible` (no JS library) in `src/DBAIAzure.Web/Shared/ConnectorConfigModal.razor`
- [X] T025 [US1] Add settings gear icon button to the header in `Index.razor` that calls `@ref _modal.Open()` to toggle `ConnectorConfigModal`; reference the modal component instance inline in `src/DBAIAzure.Web/Pages/Index.razor`
- [X] T026 [P] [US1] Write integration test stubs for all four connector live round-trips (one test method per connector, decorated `[Trait("Category","Integration")]`, skipped in standard `dotnet test` run via filter, references real credentials from environment variables) in `tests/DBAIAzure.Tests/Integration/ConnectorFunctionalTests.cs`

**Checkpoint**: User Story 1 is fully functional. Operator can configure, test, and save all four connectors from a modal on the dashboard. The pipeline pre-flight blocks unconfigured runs. Validate via quickstart.md Scenarios 1 & 4 before proceeding.

---

## Phase 4: User Story 2 — Edit an existing connector setting (Priority: P2)

**Goal**: Operator can rotate a credential (e.g., a PAT or API key) by opening the modal, entering the new value, testing, and saving — without disturbing any other connector's settings.

**Independent Test**: Configure all four connectors (Scenario 1). Then open the modal, update the LLM API key field only, test, save. Confirm only the LLM secret changed; the other three connectors are unchanged. Follow quickstart.md Scenario 2.

### Implementation for User Story 2

- [X] T027 [US2] Pre-populate non-secret fields in `ConnectorSection.razor` from `ConnectorConfig.NonSecretConfig` on modal open; show masked placeholder (`••••••••`) in the secret field when `HasSecrets == true` in `src/DBAIAzure.Web/Shared/ConnectorSection.razor`
- [X] T028 [US2] Implement write-only secret semantics in `ConnectorSection.razor` — track whether the operator has changed the secret field; pass `null` to `IConnectorConfigRepository.SaveAsync` when the field is unchanged (preserves existing encrypted blob), pass the new value only when the operator has typed in it in `src/DBAIAzure.Web/Shared/ConnectorSection.razor`
- [X] T029 [US2] Reset connector test status to "untested" in component state whenever any field value changes (`oninput` / `@bind:event="oninput"`) to enforce FR-017 in `src/DBAIAzure.Web/Shared/ConnectorSection.razor`
- [X] T030 [P] [US2] Unit tests for credential rotation: `SaveAsync` with `null` secret leaves `EncryptedSecretsJson` unchanged; `SaveAsync` with a new secret replaces it; test-status is null after any save; other connector records are unaffected in `tests/DBAIAzure.Tests/SqliteConnectorConfigRepositoryTests.cs`
- [X] T031 [P] [US2] Unit test verifying pre-flight blocks and returns specific diagnostic when ADO PAT is revoked (mocked `ConnectorHealthChecker` returns fail for ADO, pass for the other three) in `tests/DBAIAzure.Tests/ConnectorHealthCheckerTests.cs`

**Checkpoint**: User Stories 1 and 2 both work independently. Validate via quickstart.md Scenarios 2 & 5 before proceeding.

---

## Phase 5: User Story 3 — View configuration status at a glance (Priority: P3)

**Goal**: Operator can open the modal and immediately see the overall health of all four connectors — configured or not, tested or not, last result and timestamp — without expanding each connector's section.

**Independent Test**: Configure two connectors (tested passing), leave two unconfigured. Open the modal. Confirm the status overview shows four distinct badges. Follow quickstart.md Scenario 1 step 3 and User Story 3 acceptance criteria.

### Implementation for User Story 3

- [X] T032 [P] [US3] Implement `ConnectorStatusBadge.razor` — renders one status chip; parameters: `ConnectorConfig?`; states: "Not configured" (grey) / "Untested" (amber) / "Pass · [timestamp]" (green) / "Fail · [timestamp]" (red); use Tailwind classes consistent with `SourceBadge.razor` and `StatusBadge.razor` in `src/DBAIAzure.Web/Shared/ConnectorStatusBadge.razor`
- [X] T033 [US3] Add status overview row at the top of `ConnectorConfigModal.razor`: four `ConnectorStatusBadge` components side-by-side, bound to the loaded `ConnectorConfig` list; refresh after each test completes in `src/DBAIAzure.Web/Shared/ConnectorConfigModal.razor`
- [X] T034 [US3] Display `LastUpdatedAt` timestamp in each `ConnectorSection.razor` header area (e.g., "Last saved: 18 Jun 2026 09:14") in `src/DBAIAzure.Web/Shared/ConnectorSection.razor`
- [X] T035 [P] [US3] Unit tests for `ConnectorStatusBadge` — verify correct label and CSS class token for each of the four states (pass null config, untested config, pass result, fail result) in `tests/DBAIAzure.Tests/ConnectorStatusBadgeTests.cs`

**Checkpoint**: All three user stories functional. Validate via quickstart.md Scenario 1 step 3 and User Story 3 acceptance scenarios before proceeding to polish.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Inline field validation, XML doc comments, CHANGELOG, and final verification.

- [X] T036 [P] Add XML doc comments to all new public types and methods in the implementation layer — `src/DBAIAzure.Storage/Entities/ConnectorConfigRecord.cs`, `src/DBAIAzure.Storage/Repositories/SqliteConnectorConfigRepository.cs`, `src/DBAIAzure.Connectors/ServiceNowClient.cs`, `src/DBAIAzure.Connectors/TeamsConnectorTester.cs`, `src/DBAIAzure.Connectors/ConnectorHealthChecker.cs`, plus all new public methods added to `src/DBAIAzure.Web/Integrations/AzureDevOps/AzureDevOpsBoardsClient.cs` and `src/DBAIAzure.Connectors/AnthropicChatCompletionService.cs` (Core types in DBAIAzure.Core are already documented in T003–T008)
- [X] T037 Add inline field validation to `ConnectorSection.razor` — required fields (all non-secret fields + secret field on first-time save) show an inline error message when empty; validation runs before any Test or Save network call (FR-016) in `src/DBAIAzure.Web/Shared/ConnectorSection.razor`
- [X] T038 Update `CHANGELOG.md` — add entry under the current version: connector configuration modal (all 4 connectors), database-persisted settings, encrypted secrets at rest, per-connector functional tests, and live parallel pre-flight gate on every pipeline run
- [ ] T039 Run quickstart.md Scenario 6 manually — confirm no secret value appears in browser DevTools Network tab or in the `pipeline.db` `EncryptedSecretsJson` column (viewed with a SQLite browser)
- [X] T040 Run `dotnet test tests/DBAIAzure.Tests/ --filter "Category!=Integration"` — verify all pre-existing tests pass alongside the new unit test suites

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — **blocks all user story phases**
- **Phase 3 (US1)**: Depends on Phase 2 — MVP deliverable
- **Phase 4 (US2)**: Depends on Phase 3 (extends `ConnectorSection.razor` built in T023/T027-T029)
- **Phase 5 (US3)**: Depends on Phase 2 (needs `ConnectorConfig` domain type); most tasks also build on Phase 3 Blazor components
- **Phase 6 (Polish)**: Depends on Phases 3–5

### Within Phase 3 — Sequential Dependencies

```
T014, T015, T016, T017  →  T018  →  T020 (DI)
                         T018  →  T021, T022 (orchestrators) [P with each other]
T023 (ConnectorSection)  →  T024 (ConnectorConfigModal)  →  T025 (Index.razor)
T014–T017 (test methods) →  T026 (integration test stubs)
T018 + T019 can overlap (write unit tests alongside ConnectorHealthChecker implementation)
```

### User Story Dependencies

- **US1 (P1)**: Starts immediately after Phase 2. No dependency on US2 or US3.
- **US2 (P2)**: Requires `ConnectorSection.razor` from T023 (US1). T027–T029 extend that component.
- **US3 (P3)**: Requires `ConnectorConfig` domain type from Phase 2 and `ConnectorConfigModal.razor` from T024 (US1).

---

## Parallel Opportunities

### Phase 2 — Can run in parallel

```
T003 ConnectorType enum
T004 Per-connector non-secret config records
T005 ConnectorConfig record
T006 ConnectorTestResult + PipelinePreflightFailure records
T007 IConnectorConfigRepository interface
T008 IConnectorHealthChecker interface
T012 Unit tests for SqliteConnectorConfigRepository [after T009–T011]
```

### Phase 3 — Parallelizable tasks

```
T014 ServiceNowClient
T015 AzureDevOpsBoardsClient hot-reload + test
T016 AnthropicChatCompletionService hot-reload + test
T017 TeamsConnectorTester + TeamsHitlNotifier hot-reload
T019 ConnectorHealthChecker unit tests [alongside T018]
T021 PipelineOrchestrator pre-flight [after T018+T020]
T022 PhaseHandlerOrchestrator pre-flight [after T018+T020]
T023 ConnectorSection.razor [after T014–T017]
T026 Integration test stubs [after T014–T017]
```

---

## MVP Scope

**Minimum viable product**: Complete Phase 1 + Phase 2 + Phase 3 (Tasks T001–T026). This delivers:
- A working configuration modal for all four connectors
- Encrypted persistence in SQLite
- Per-connector functional tests
- Live pre-flight gate before pipeline runs
