# Tasks: ADO Telemetry Field Bootstrap

**Input**: Design documents from `specs/008-ado-telemetry-bootstrap/`

**Prerequisites**: plan.md ✅ spec.md ✅ research.md ✅ data-model.md ✅ contracts/ ✅

**TDD required**: Constitution Article V — failing tests MUST be written before every implementation task. Red → Green → Refactor.

**E2E required**: Constitution Article V — every interactive UI element needs a Playwright test before the feature is shippable.

## Format: `[ID] [P?] [Story?] Description with file path`

- **[P]**: Parallelizable — different files, no incomplete task dependencies
- **[US#]**: User story label — required on all story-phase tasks
- Exact file paths are given for every task

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create new directories and wire the embedded-resource configuration before any C# is written.

- [ ] T001 Create `src/DBAIAzure.Core/Models/AdoTelemetry/` and `src/DBAIAzure.Core/Resources/` directories (no files yet — establishes namespace root and resource folder)
- [ ] T002 Add `<EmbeddedResource Include="Resources/default-telemetry-config.json" />` to `src/DBAIAzure.Core/DBAIAzure.Core.csproj` so the default field config is baked into the assembly

**Checkpoint**: Build passes (`dotnet build src/DBAIAzure.Core/`) — directories and csproj entry exist, no source errors.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core enums, records, and interface that every subsequent phase compiles against. No story-specific logic yet.

**⚠️ CRITICAL**: All user story phases depend on these types being present and compiling.

- [ ] T003 [P] Create `AdoProcessType.cs` (enum: Agile, Scrum, Unsupported; XML doc on each value) in `src/DBAIAzure.Core/Models/AdoTelemetry/AdoProcessType.cs`
- [ ] T004 [P] Create `AdoFieldType.cs` (enum: String, Integer, Double, PicklistString; XML doc) in `src/DBAIAzure.Core/Models/AdoTelemetry/AdoFieldType.cs`
- [ ] T005 [P] Create `PreflightMode.cs` (enum: Bootstrap, Adaptive; XML doc) in `src/DBAIAzure.Core/Models/AdoTelemetry/PreflightMode.cs`
- [ ] T006 Create `AdoTelemetryFieldDefinition.cs` (record with Name, ReferenceName, FieldType, PicklistValues?, Required, FallbackReferenceName?, FallbackDisplayName?; invariant: PicklistValues non-null when FieldType==PicklistString; XML doc) in `src/DBAIAzure.Core/Models/AdoTelemetry/AdoTelemetryFieldDefinition.cs`
- [ ] T007 [P] Create `AdoTelemetryWorkItemTypeConfig.cs` (record with WorkItemTypeName, Fields: IReadOnlyList<AdoTelemetryFieldDefinition>; XML doc) in `src/DBAIAzure.Core/Models/AdoTelemetry/AdoTelemetryWorkItemTypeConfig.cs`
- [ ] T008 Create `AdoTelemetryFieldConfig.cs` (record with Version, WorkItemTypes: IReadOnlyDictionary<string,AdoTelemetryWorkItemTypeConfig>, FallbackStrategy: IReadOnlyDictionary<AdoFieldType,string?>, TagsEncoding; XML doc) in `src/DBAIAzure.Core/Models/AdoTelemetry/AdoTelemetryFieldConfig.cs`
- [ ] T009 Create `PreflightManifest.cs` (abstract record PreflightManifestBase with Mode/Timestamp/OrgUrl/Project/ProcessType; sealed record BootstrapManifest with FieldsCreated/FieldsExisting/FieldsFailed/MappingStrategy; sealed record AdaptiveManifest with Mapping/UnmatchedFields/LogOnlyFields; record FieldBootstrapFailure with ReferenceName/Error/AttemptsExhausted; XML doc on all) in `src/DBAIAzure.Core/Models/AdoTelemetry/PreflightManifest.cs`
- [ ] T010 Create `PreflightResult.cs` (record with IsSuccess, ErrorMessage?, Manifest?: PreflightManifestBase; XML doc) in `src/DBAIAzure.Core/Models/AdoTelemetry/PreflightResult.cs`
- [ ] T011 Create `IAdoTelemetryPreflightService.cs` (interface with `Task<PreflightResult> RunPreflightAsync(AdoTelemetryFieldConfig? overrideConfig, CancellationToken ct)` and XML doc explaining the why) in `src/DBAIAzure.Core/Interfaces/IAdoTelemetryPreflightService.cs`
- [ ] T012 Create `default-telemetry-config.json` with all 14 fields (12 UserStory fields: AISessionID/AIModelUsed/AIInputTokens/AIOutputTokens/AICacheTokens/AIEstimatedCostUSD/AISessionDurationSec/AIToolCalls/AIToolAcceptRatePct/AIAPIErrors/AICacheHitRatePct/SpeckitPhase with picklist; 2 Task fields: AISessionID/AIModelUsed) matching the schema in `contracts/ado-telemetry-config.schema.json`, placed at `src/DBAIAzure.Core/Resources/default-telemetry-config.json`

**Checkpoint**: `dotnet build` — all projects compile cleanly against the new types.

---

## Phase 3: User Story 1 — First-time Bootstrap with Admin Rights (Priority: P1) 🎯 MVP

**Goal**: The service detects an ADO org, confirms admin rights, creates all 14 custom fields (skipping any that exist), retries up to 3× on transient failures, and writes a `BootstrapManifest` to disk.

**Independent Test**: With a fresh ADO project and admin PAT, call `RunPreflightAsync(null, ct)` — the returned `BootstrapManifest.FieldsCreated` should contain all 14 reference names and `.ado-bootstrap-manifest.json` should be written to the active feature's spec directory.

### Tests for User Story 1 (write FIRST — must fail before T019)

- [ ] T013 [P] [US1] Write failing unit test: missing org URL in resolved config → `RunPreflightAsync` returns `IsSuccess=false` with no HTTP calls made in `tests/DBAIAzure.Tests/Services/AdoTelemetryPreflightServiceTests.cs` [RED]
- [ ] T014 [P] [US1] Write failing unit tests: process detection — mock HTTP returning Agile process list → `AdoProcessType.Agile`; Scrum → `AdoProcessType.Scrum`; CMMI → `IsSuccess=false` naming "CMMI" in error message in `tests/DBAIAzure.Tests/Services/AdoTelemetryPreflightServiceTests.cs` [RED]
- [ ] T015 [P] [US1] Write failing unit tests: Bootstrap field operations — existence GET 404 → field creation POST called; existence GET 200 → creation POST not called (idempotency); field creation POST returns 429 twice then 200 → success after 2 retries; field creation POST returns 429 three times → `FieldsFailed` entry, run continues in `tests/DBAIAzure.Tests/Services/AdoTelemetryPreflightServiceTests.cs` [RED]

### Implementation for User Story 1

- [ ] T016 [US1] Create `AdoTelemetryPreflightService.cs` skeleton (implements `IAdoTelemetryPreflightService`; all methods `throw new NotImplementedException()`; XML doc class comment; `private const int MaxRetryAttempts = 3`) in `src/DBAIAzure.Web/Integrations/AzureDevOps/AdoTelemetryPreflightService.cs`
- [ ] T017 [US1] Implement `ResolveCredentialsAsync` in `AdoTelemetryPreflightService.cs` — mirror `AzureDevOpsBoardsClient.ResolveAllConfigAsync` exactly: read `IConnectorConfigRepository.GetDecryptedSecretsAsync` for PAT, `GetAsync` for org URL/project; fall back to `IOptions<AzureDevOpsOptions>` on any repo error; never log credential values
- [ ] T018 [US1] Implement `BuildBasicAuthHeader` helper and `CreateAdoHttpClientAsync` in `AdoTelemetryPreflightService.cs` — returns an `HttpClient` with `Authorization: Basic :{PAT}` header and 30-second timeout (reuse `HttpClientFactory` if registered, else `new HttpClient()`)
- [ ] T019 [US1] Implement `DetectProcessTypeAsync` in `AdoTelemetryPreflightService.cs` — GET `{orgUrl}/_apis/process/processes?api-version=7.1` then GET `{orgUrl}/{project}/_apis/work/process/configuration?api-version=7.1`; return `AdoProcessType.Agile`, `Scrum`, or `Unsupported` (with name in failure message)
- [ ] T020 [US1] Implement `ProbeAdminAccessAsync` in `AdoTelemetryPreflightService.cs` — GET `{orgUrl}/_apis/process/processes?api-version=7.1`; 200 → `PreflightMode.Bootstrap`; 403 → `PreflightMode.Adaptive`; other → propagate error
- [ ] T021 [US1] Implement `RetryWithBackoffAsync<T>` private method in `AdoTelemetryPreflightService.cs` — up to `MaxRetryAttempts` attempts; retry on `HttpRequestException`, `TaskCanceledException`, HTTP 429/503; delays `TimeSpan.FromSeconds(Math.Pow(2, attempt + 1))` (2 s, 4 s, 8 s); do not retry 4xx other than 429; on all retries exhausted return final exception/status
- [ ] T022 [US1] Implement Bootstrap Mode field existence check (GET `{orgUrl}/_apis/wit/fields/{ref}?api-version=7.1` → 200 = exists, 404 = absent) in `AdoTelemetryPreflightService.cs`
- [ ] T023 [US1] Implement Bootstrap Mode org-level field creation (POST `{orgUrl}/_apis/wit/fields?api-version=7.1` body `{name, type}`) wrapped in `RetryWithBackoffAsync` in `AdoTelemetryPreflightService.cs`
- [ ] T024 [US1] Implement picklist creation for `Custom.SpeckitPhase` (POST `{orgUrl}/_apis/work/processes/lists?api-version=7.1`; on 409 GET existing list to retrieve ID; on other error fall back to plain string type and log downgrade via `ILogger`) in `AdoTelemetryPreflightService.cs`
- [ ] T025 [US1] Implement Bootstrap Mode WIT field attachment (POST `{orgUrl}/_apis/work/processes/{processId}/workItemTypes/{witRef}/fields?api-version=7.1` body `{referenceName}`) wrapped in `RetryWithBackoffAsync` in `AdoTelemetryPreflightService.cs`
- [ ] T026 [US1] Create `ManifestPathResolver.cs` — reads `IConfiguration["SpecKit:SpecsRoot"]`; reads `{specsRoot}/.specify/feature.json` to get `feature_directory`; returns `Path.Combine(specsRoot, featureDir, ".ado-bootstrap-manifest.json")`; fallback path `Path.Combine(specsRoot, ".ado-bootstrap-manifest.json")` when `feature.json` absent in `src/DBAIAzure.Web/Integrations/AzureDevOps/ManifestPathResolver.cs`
- [ ] T027 [US1] Implement `WriteManifestAsync` in `AdoTelemetryPreflightService.cs` — serializes `PreflightManifestBase` to JSON (`System.Text.Json`, camelCase, indented), calls `ManifestPathResolver`, writes file with `File.WriteAllTextAsync`
- [ ] T028 [US1] Implement `RunBootstrapAsync` in `AdoTelemetryPreflightService.cs` — iterates fields sequentially (no parallel to avoid rate spikes), calls existence check → create → attach per field, accumulates `FieldsCreated`/`FieldsExisting`/`FieldsFailed`, calls `WriteManifestAsync`; returns `BootstrapManifest`
- [ ] T029 [US1] Implement `RunPreflightAsync` Bootstrap path orchestration in `AdoTelemetryPreflightService.cs`: validate config → detect process type (fail fast on Unsupported) → load default config → probe admin access → call `RunBootstrapAsync` or defer to Adaptive → return `PreflightResult`
- [ ] T030 [US1] Run `dotnet test tests/DBAIAzure.Tests/ --filter "FullyQualifiedName~AdoTelemetryPreflight" --filter "Category!=Integration"` — all Bootstrap unit tests GREEN

**Checkpoint**: Bootstrap Mode fully functional and unit-tested. Calling `RunPreflightAsync` with a mocked HTTP layer returns a correct `BootstrapManifest`.

---

## Phase 4: User Story 3 — Configure ADO Connection from Console UI (Priority: P1)

**Goal**: Developer can enter ADO org URL / project in the global Settings panel and click "Test Connection" to run the preflight on-demand and see results inline. Pipeline auto-runs preflight on startup.

**Independent Test**: Open app in browser → open settings modal → enter valid ADO config → click "Test Connection" → result badge appears showing Bootstrap or Adaptive mode. Changing org URL and clicking again targets the new URL.

### Tests for User Story 3 (write FIRST — must fail before T034)

- [ ] T031 [US3] Write failing unit tests for `AdoTelemetryPreflightStep`: service returns `BootstrapManifest` → step emits `AdoPreflightSucceeded` event; service returns `IsSuccess=false` → step emits `AdoPreflightFailed`; `CancellationToken` already cancelled on entry → service not called in `tests/DBAIAzure.Tests/Steps/AdoTelemetryPreflightStepTests.cs` [RED]

### Implementation for User Story 3

- [ ] T032 [US3] Create `AdoTelemetryPreflightStep.cs` (KernelProcessStep<AdoPreflightStepState>; state: ManifestPath/Mode/IsComplete; input event `AdoPreflightRequested`; output events `AdoPreflightSucceeded`/`AdoPreflightFailed`; step body calls `IAdoTelemetryPreflightService.RunPreflightAsync`, updates state, emits typed event; XML doc on class and events) in `src/DBAIAzure.Processes/Steps/AdoTelemetryPreflightStep.cs`
- [ ] T033 [US3] Register services in `src/DBAIAzure.Web/Program.cs`: `builder.Services.AddScoped<IAdoTelemetryPreflightService, AdoTelemetryPreflightService>()` and `builder.Services.AddScoped<ManifestPathResolver>()`
- [ ] T034 [US3] Add `IHostApplicationLifetime.ApplicationStarted` callback in `src/DBAIAzure.Web/Program.cs` that fire-and-forgets `RunPreflightAsync(null, CancellationToken.None)` using a scoped service resolved from `app.Services.CreateScope()`; log outcome via `ILogger<AdoTelemetryPreflightService>` at Information/Warning level
- [ ] T035 [US3] Add to `src/DBAIAzure.Web/Shared/ConnectorConfigModal.razor`: `@inject IAdoTelemetryPreflightService PreflightService`; private state fields `_isTestRunning (bool)`, `_testResult (PreflightResult?)`; "Test Connection" button (disabled while `_isTestRunning`) calling `RunPreflightAsync(null, _cts.Token)`; inline result display: green badge showing mode + field counts on success, red badge + error text on failure; call `StateHasChanged()` after completion
- [ ] T036 [US3] Write Playwright E2E test: navigate to app, open connector config modal, fill ADO org URL, click "Test Connection" button, assert result element contains "Bootstrap" or "Adaptive" text (use `[data-testid="ado-preflight-result"]` attribute on the result badge) in `tests/DBAIAzure.E2ETests/ConnectorConfigTests.cs`
- [ ] T037 [US3] Run `dotnet test tests/DBAIAzure.Tests/ --filter "FullyQualifiedName~PreflightStep"` — SK step unit tests GREEN

**Checkpoint**: US1 + US3 complete. "Test Connection" triggers Bootstrap Mode end-to-end. Startup auto-run logs preflight outcome on application start.

---

## Phase 5: User Story 2 — Graceful Fallback Without Admin Rights (Priority: P2)

**Goal**: When the configured PAT has no process-write permission, the service builds a native-field fallback mapping and writes an `AdaptiveManifest` to disk. Pipeline continues without error.

**Independent Test**: With a read-only PAT, call `RunPreflightAsync(null, ct)` — the returned `AdaptiveManifest.Mapping` should map each desired field to a native ADO field or mark it log-only; no HTTP 403 error surfaces to the caller.

### Tests for User Story 2 (write FIRST — must fail before T041)

- [ ] T038 [P] [US2] Write failing unit tests: admin probe returns HTTP 403 → mode switches to Adaptive (no field creation called); string-type field → mapped to `System.Tags`; integer-type field → mapped to `Microsoft.VSTS.Scheduling.StoryPoints`; double-type field with null fallback → in `LogOnlyFields`; field with no native match → in `UnmatchedFields`; Tags encoding result is pipe-separated key-value string in `tests/DBAIAzure.Tests/Services/AdoTelemetryPreflightServiceTests.cs` [RED]
- [ ] T039 [P] [US2] Write failing unit test: `Custom.AISessionID` already exists as a custom field in available-fields response → mapped to `Custom.AISessionID` (priority-1 exact match, not Tags fallback) in `tests/DBAIAzure.Tests/Services/AdoTelemetryPreflightServiceTests.cs` [RED]

### Implementation for User Story 2

- [ ] T040 [US2] Implement `FetchAvailableFieldsAsync` in `AdoTelemetryPreflightService.cs` — GET `{orgUrl}/{project}/_apis/wit/fields?api-version=7.1`; return list of reference names present on the project
- [ ] T041 [US2] Implement `BuildAdaptiveMappingAsync` in `AdoTelemetryPreflightService.cs` — for each desired field: (1) exact ref name match in available fields; (2) `FallbackReferenceName` from field definition if present in available fields; (3) `System.Tags` (always available); (4) log-only when `FallbackReferenceName` is null and no Tags; populate `Mapping`, `UnmatchedFields`, `LogOnlyFields`
- [ ] T042 [US2] Implement `RunAdaptiveAsync` in `AdoTelemetryPreflightService.cs` — calls `FetchAvailableFieldsAsync` for each target WIT, calls `BuildAdaptiveMappingAsync`, builds `AdaptiveManifest`, calls `WriteManifestAsync`; returns `AdaptiveManifest`
- [ ] T043 [US2] Wire Adaptive path into `RunPreflightAsync` in `AdoTelemetryPreflightService.cs` — admin probe 403 → call `RunAdaptiveAsync` instead of `RunBootstrapAsync`
- [ ] T044 [US2] Run `dotnet test tests/DBAIAzure.Tests/ --filter "FullyQualifiedName~AdoTelemetryPreflight" --filter "Category!=Integration"` — all Adaptive unit tests GREEN

**Checkpoint**: US1 + US2 + US3 complete. Both Bootstrap and Adaptive modes work end-to-end.

---

## Phase 6: User Story 4 — Telemetry Field Config Overridable Per Workflow (Priority: P3)

**Goal**: The default 14-field config can be replaced by a custom JSON file (deployment-level) or a passed-in `AdoTelemetryFieldConfig` object (workflow-level) without code changes.

**Independent Test**: Supply a custom config omitting cost fields → call `RunPreflightAsync(customConfig, ct)` → `BootstrapManifest.FieldsCreated` contains only the non-omitted fields; cost fields absent from manifest and from ADO.

### Tests for User Story 4 (write FIRST — must fail before T047)

- [ ] T045 [P] [US4] Write failing unit tests: embedded `default-telemetry-config.json` deserializes to `AdoTelemetryFieldConfig` with 12 UserStory fields + 2 Task fields + correct picklist values; `IConfiguration["AdoTelemetry:ConfigPath"]` pointing to a temp JSON file → that file's config is used instead; null override + no config path → embedded default used in `tests/DBAIAzure.Tests/Services/AdoTelemetryPreflightServiceTests.cs` [RED]

### Implementation for User Story 4

- [ ] T046 [US4] Implement `LoadDefaultConfigAsync` in `AdoTelemetryPreflightService.cs` — load embedded `default-telemetry-config.json` via `typeof(AdoTelemetryPreflightService).Assembly.GetManifestResourceStream("DBAIAzure.Core.Resources.default-telemetry-config.json")`; deserialize with `JsonSerializer.DeserializeAsync<AdoTelemetryFieldConfig>` using camelCase options
- [ ] T047 [US4] Implement `ResolveFieldConfigAsync` in `AdoTelemetryPreflightService.cs` — if `overrideConfig` non-null return it; else if `IConfiguration["AdoTelemetry:ConfigPath"]` set and file exists read + deserialize it; else return embedded default; update `RunPreflightAsync` to call `ResolveFieldConfigAsync` before the bootstrap/adaptive flow
- [ ] T048 [US4] Run `dotnet test tests/DBAIAzure.Tests/ --filter "FullyQualifiedName~AdoTelemetryPreflight" --filter "Category!=Integration"` — all config override tests GREEN

**Checkpoint**: All four user stories independently functional and unit-tested.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Full test-suite pass, Playwright E2E, CHANGELOG, and quickstart validation.

- [ ] T049 [P] Run full unit test suite `dotnet test tests/DBAIAzure.Tests/ --filter "Category!=Integration"` — zero failures
- [ ] T050 [P] Run Playwright E2E suite `dotnet test tests/DBAIAzure.E2ETests/` — "Test Connection" scenario passes
- [ ] T051 Update `CHANGELOG.md` under `[Unreleased]` — add entries for US1 (Bootstrap Mode), US2 (Adaptive Mode), US3 (Test Connection button + startup auto-run), US4 (config override)
- [ ] T052 Run quickstart.md Scenario 1 against live ADO (`https://dev.azure.com/mikejsmith1985rll`): inject admin PAT via Forge Vault, click "Test Connection", confirm `.ado-bootstrap-manifest.json` written with `"mode": "bootstrap"` and all 14 fields listed
- [ ] T053 Run quickstart.md Scenario 3 (missing org URL): clear ADO URL in settings, click "Test Connection", confirm error badge displayed and no manifest written

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — **BLOCKS all story phases**
- **US1 Bootstrap (Phase 3)**: Depends on Phase 2
- **US3 Console UI (Phase 4)**: Depends on Phase 3 (Test Connection button requires working service)
- **US2 Adaptive (Phase 5)**: Depends on Phase 2 (can start in parallel with US1 once T016 skeleton exists)
- **US4 Config Override (Phase 6)**: Depends on Phase 3 (needs `RunPreflightAsync` to exist)
- **Polish (Phase 7)**: Depends on all story phases complete

### User Story Dependencies

- **US1 (P1)**: Foundational phase complete → no other story dependency
- **US3 (P1)**: US1 complete → adds UI layer over the working service
- **US2 (P2)**: Foundational phase complete → parallel with US1 after T016 skeleton created
- **US4 (P3)**: US1 complete → extends config loading in `RunPreflightAsync`

### Within Each User Story

1. Tests (T013–T015, T031, T038–T039, T045) written and confirmed **FAILING** first
2. Skeleton created before implementation tasks (T016 before T017–T029)
3. Helper methods before the methods that call them (T021 retry wrapper before T023 creation call)
4. `ManifestPathResolver` (T026) before `WriteManifestAsync` (T027)
5. `RunBootstrapAsync` (T028) before full `RunPreflightAsync` orchestration (T029)

### Parallel Opportunities

- T003, T004, T005, T007 — independent enums/records, write simultaneously
- T013, T014, T015 — all test cases in the same file, write simultaneously
- T038, T039 — Adaptive test cases, write simultaneously
- T049, T050 — independent test suite invocations, run simultaneously

---

## Parallel Example: User Story 1 Tests

```
# Write all US1 failing tests together (T013, T014, T015 all target the same file):
Task T013: "missing org URL → IsSuccess=false, no HTTP calls"
Task T014: "process detection: Agile/Scrum/CMMI"
Task T015: "Bootstrap field: 404→create, 200→skip, 429×3→FieldsFailed"
```

## Parallel Example: Foundational Enums

```
# T003, T004, T005, T007 target different files — write in parallel:
Task T003: AdoProcessType.cs
Task T004: AdoFieldType.cs
Task T005: PreflightMode.cs
Task T007: AdoTelemetryWorkItemTypeConfig.cs
```

---

## Implementation Strategy

### MVP First (User Story 1 + User Story 3)

1. Complete Phase 1: Setup (T001–T002)
2. Complete Phase 2: Foundational (T003–T012) — **critical path**
3. Complete Phase 3: US1 Bootstrap Mode (T013–T030)
4. Complete Phase 4: US3 Console UI (T031–T037)
5. **STOP and VALIDATE**: Bootstrap Mode works end-to-end via "Test Connection" button
6. Ship MVP if ready

### Incremental Delivery

1. Phase 1 + Phase 2 → foundation compiled
2. Phase 3 (US1) → Bootstrap Mode tested and working
3. Phase 4 (US3) → UI live, Test Connection button functional
4. Phase 5 (US2) → Adaptive Mode tested and working
5. Phase 6 (US4) → Config override working
6. Phase 7 → Polish, CHANGELOG, quickstart validation

---

## Notes

- `[P]` tasks target different files or are independent test cases — write or implement simultaneously
- Every test task is explicitly marked `[RED]` — run `dotnet test` to confirm failure before implementing
- `ManifestPathResolver` (T026) must be created before any manifest-write code is exercised
- The Playwright test (T036) requires `data-testid="ado-preflight-result"` on the result badge in the Razor component — add this attribute in T035
- Commit after each phase checkpoint; do not commit a broken build
- Never hard-code the PAT or org URL in any source file — always read from `IConnectorConfigRepository`
