# Implementation Plan: ADO Telemetry Field Bootstrap

**Branch**: `feature/sk-process-intake-pipeline` | **Date**: 2026-06-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/008-ado-telemetry-bootstrap/spec.md`

---

## Summary

The ADO Telemetry Field Bootstrap preflight step runs once per pipeline session (and on-demand from the global Settings panel) to ensure all custom AI telemetry fields exist in the target ADO organization before any work items are created. The step detects whether admin rights are available, then either creates custom fields (Bootstrap Mode) or builds a native-field fallback mapping (Adaptive Mode), and writes a JSON manifest to the active feature's spec directory. The global Settings panel gains a "Test Connection" button that surfaces the preflight result inline.

---

## Technical Context

**Language/Version**: C# 12 / .NET 8 (`net8.0`)

**Primary Dependencies**:
- `Microsoft.SemanticKernel` 1.77.0 — SK Process Framework for the preflight step
- `Microsoft.SemanticKernel.Process.Core` 1.77.0-alpha — `KernelProcessStep` base
- `Microsoft.TeamFoundationServer.Client` 20.256.2 — `WorkItemTrackingHttpClient` for org-level field CRUD; raw `HttpClient` (BCL) for process API calls not in the managed client
- Blazor Server (via `DBAIAzure.Web`) — UI for global Settings panel

**Storage**: File I/O only — manifest written as JSON to `specs/<feature-dir>/.ado-bootstrap-manifest.json`; config read as embedded resource + optional override file.

**Testing**: xUnit + Moq (existing test project `DBAIAzure.Tests`). Unit tests are 100% mocked. No integration tests against live ADO in CI (marked `[Trait("Category","Integration")]` and excluded from default run).

**Target Platform**: Windows / Linux server (same as existing app)

**Performance Goals**: Preflight completes in under 30 seconds under normal network (SC-001). Per-field retry delay budget: 14 s worst-case per field (2+4+8 s). With 14 fields and all failing: 14 × 14 s = 196 s worst-case failure path (acceptable — only on complete ADO outage).

**Constraints**: No new NuGet packages. No Polly. Article IX: PAT resolved from `IConnectorConfigRepository` / Forge Vault — never hard-coded.

**Scale/Scope**: 14 fields, 2 work item types, 1 pipeline session per invocation. Single-threaded ADO field creation (sequential, not parallel, to avoid rate-limit spikes).

---

## Constitution Check

### Article I — Prime Directive ✅
Taking the best route: SK Process step for orchestration, clean service/interface separation, embedded default config, full unit test coverage per TDD.

### Article II — Process Protection ✅
No process kills. The new step only performs file I/O and HTTP calls.

### Article IV — Code Quality ✅
All new types: PascalCase, nullable reference types enabled, XML doc comments, async/await with CancellationToken, no magic numbers (retry count = `MaxRetryAttempts = 3` constant).

### Article V — Testing (TDD) ✅
Failing unit tests written before implementation. Two test classes cover the step and the service. Integration tests are category-gated (`[Trait("Category","Integration")]`) and excluded from CI.

### Article VII — Framework-First Gate ✅
- **Orchestration/state**: `AdoTelemetryPreflightStep : KernelProcessStep<AdoPreflightStepState>` — uses SK Process Framework, not a bespoke state machine.
- **Structured output**: `PreflightResult` record returned from service; manifest serialized via `System.Text.Json`.
- **Step wiring/DI**: `IAdoTelemetryPreflightService` registered in DI, injected into the SK step via the framework's step DI.
- **No custom gap**: No SK Framework capability is being rebuilt. The ADO field creation is genuine application domain logic outside SK's scope.

### Article IX — Secrets ✅
PAT resolved from `IConnectorConfigRepository.GetDecryptedSecretsAsync` (existing pattern in `AzureDevOpsBoardsClient`) — never in source, config files, or logs.

---

## Project Structure

### Documentation (this feature)

```text
specs/008-ado-telemetry-bootstrap/
├── plan.md              ← this file
├── research.md          ← Phase 0 decisions
├── data-model.md        ← entity reference
├── quickstart.md        ← validation guide
├── contracts/
│   ├── ado-telemetry-config.schema.json
│   └── preflight-service-interface.md
└── tasks.md             ← Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/DBAIAzure.Core/
├── Models/AdoTelemetry/
│   ├── AdoProcessType.cs                  (new) enum
│   ├── AdoFieldType.cs                    (new) enum
│   ├── PreflightMode.cs                   (new) enum
│   ├── AdoTelemetryFieldDefinition.cs     (new) config model
│   ├── AdoTelemetryWorkItemTypeConfig.cs  (new) config model
│   ├── AdoTelemetryFieldConfig.cs         (new) root config model
│   ├── PreflightManifest.cs               (new) abstract base + BootstrapManifest
│   │                                           + AdaptiveManifest + FieldBootstrapFailure
│   └── PreflightResult.cs                 (new) service return type
├── Interfaces/
│   └── IAdoTelemetryPreflightService.cs   (new)
└── Resources/
    └── default-telemetry-config.json      (new, EmbeddedResource)

src/DBAIAzure.Processes/Steps/
└── AdoTelemetryPreflightStep.cs           (new) KernelProcessStep<AdoPreflightStepState>

src/DBAIAzure.Web/
├── Integrations/AzureDevOps/
│   └── AdoTelemetryPreflightService.cs    (new) implements IAdoTelemetryPreflightService
├── Shared/
│   └── ConnectorConfigModal.razor         (modified) add Test Connection + result display
└── Program.cs                             (modified) register service + auto-run on startup

tests/DBAIAzure.Tests/
├── Steps/
│   └── AdoTelemetryPreflightStepTests.cs  (new)
└── Services/
    └── AdoTelemetryPreflightServiceTests.cs  (new)
```

---

## Implementation Phases

### Phase A — Core models and interface (DBAIAzure.Core)

**Order**: TDD first — unit tests written before source types.

1. Add `src/DBAIAzure.Core/Models/AdoTelemetry/` directory.
2. Create enums: `AdoProcessType`, `AdoFieldType`, `PreflightMode`.
3. Create config records: `AdoTelemetryFieldDefinition`, `AdoTelemetryWorkItemTypeConfig`, `AdoTelemetryFieldConfig`.
4. Create manifest records: `PreflightManifestBase` (abstract), `BootstrapManifest`, `AdaptiveManifest`, `FieldBootstrapFailure`.
5. Create `PreflightResult` record.
6. Create `IAdoTelemetryPreflightService` interface.
7. Add `src/DBAIAzure.Core/Resources/default-telemetry-config.json` as `EmbeddedResource`.

**Unit tests first** (`AdoTelemetryPreflightServiceTests.cs` — compile only, services not yet available):
- Deserialization of `default-telemetry-config.json` produces correct `AdoTelemetryFieldConfig` (12 UserStory + 2 Task fields, correct types, picklist values).
- `AdoProcessType.Agile` resolves story WIT to `"User Story"`; `Scrum` resolves to `"Product Backlog Item"`.

---

### Phase B — SK Process step (DBAIAzure.Processes)

1. Create `AdoTelemetryPreflightStep : KernelProcessStep<AdoPreflightStepState>`.
   - `AdoPreflightStepState`: `ManifestPath (string?)`, `Mode (PreflightMode?)`, `IsComplete (bool)`.
   - Input event: `AdoPreflightRequested` carrying `AdoTelemetryFieldConfig?` (null = use default).
   - Output events: `AdoPreflightSucceeded` (carries `PreflightManifestBase`), `AdoPreflightFailed` (carries error string).
   - Step body: resolves config (default or supplied), calls `IAdoTelemetryPreflightService.RunPreflightAsync`, emits appropriate event, updates state.

**Unit tests first** (`AdoTelemetryPreflightStepTests.cs`):
- Given service returns success with `BootstrapManifest` → step emits `AdoPreflightSucceeded`, state `IsComplete = true`.
- Given service returns failure → step emits `AdoPreflightFailed` with the error message.
- Step does not call service when CancellationToken is already cancelled.

---

### Phase C — Preflight service implementation (DBAIAzure.Web)

`AdoTelemetryPreflightService : IAdoTelemetryPreflightService`

Internal flow:

```
1. Validate config — org URL and project name present
   → if missing: return PreflightResult { IsSuccess=false, ErrorMessage="ADO org URL is required" }

2. Detect process type
   GET {org}/_apis/process/processes?api-version=7.1
   GET {org}/{project}/_apis/work/process/configuration?api-version=7.1
   → Agile:  storyWit = "User Story"
   → Scrum:  storyWit = "Product Backlog Item"
   → Other:  return failure with process name
   → 404/net: return failure with diagnostic

3. Probe admin access
   GET {org}/_apis/process/processes → 200 = Bootstrap Mode, 403 = Adaptive Mode

4a. Bootstrap Mode (per field, sequential):
      a. GET {org}/_apis/wit/fields/{ref}?api-version=7.1
      b. 404 → create:
           If PicklistString: POST {org}/_apis/work/processes/lists?api-version=7.1
                              (409 = picklist exists, fetch ID)
           POST {org}/_apis/wit/fields?api-version=7.1
      c. POST {org}/_apis/work/processes/{processId}/workItemTypes/{witRef}/fields
      d. On 429/503/timeout: wait 2^(attempt+1) seconds, retry (max 3 total attempts)
      e. After 3 failures: record in FieldsFailed, continue to next field
    Collect results → BootstrapManifest
    Write manifest to disk → ManifestPathResolver.ResolveAsync()
    return PreflightResult { IsSuccess=true, Manifest=bootstrapManifest }

4b. Adaptive Mode:
    GET {org}/{project}/_apis/wit/fields?api-version=7.1 for each target WIT
    For each desired field:
      Priority: (1) exact ref match → (2) native fallback ref → (3) Tags → (4) log-only
    Build mapping → AdaptiveManifest
    Write manifest to disk
    return PreflightResult { IsSuccess=true, Manifest=adaptiveManifest }
```

Auth: identical to `AzureDevOpsBoardsClient.ResolveAllConfigAsync` — reads `IConnectorConfigRepository` with `IOptions<AzureDevOpsOptions>` fallback.

**Unit tests** (all mocked — no live ADO calls):
- Bootstrap: existence 404 → POST creates field → `fieldsCreated` contains reference name.
- Idempotency: existence 200 → no POST → `fieldsExisting` contains reference name.
- Retry: two 429 then 200 → field in `fieldsCreated` (2 retries consumed, success).
- Max-retry exhaustion: three 429 → `fieldsFailed`, run continues with other fields.
- Adaptive: admin probe 403 → mapping built correctly → manifest written.
- Process detection Agile/Scrum/Other.
- Missing org URL → `IsSuccess=false`, zero HTTP calls.
- Picklist 409 on creation → uses existing picklist ID, field created.
- Picklist non-409 error → field created as plain string, downgrade logged.

---

### Phase D — UI: "Test Connection" in global Settings panel

Modify `src/DBAIAzure.Web/Shared/ConnectorConfigModal.razor`:

1. `@inject IAdoTelemetryPreflightService PreflightService`
2. Private state: `bool _isTestRunning`, `PreflightResult? _testResult`.
3. "Test Connection" button — calls `PreflightService.RunPreflightAsync(defaultConfig, _cts.Token)`.
4. Result display: success → green badge showing mode + field counts; failure → red badge + error message.
5. Disable button while `_isTestRunning`; re-enable on completion.

---

### Phase E — Startup registration and auto-run

Modify `src/DBAIAzure.Web/Program.cs`:

1. `builder.Services.AddScoped<IAdoTelemetryPreflightService, AdoTelemetryPreflightService>();`
2. After `app.MapRazorPages()`: register `IHostApplicationLifetime.ApplicationStarted` callback that fire-and-forgets `RunPreflightAsync(defaultConfig)` with `ILogger` for outcome.

---

## Key Design Decisions Summary

| Decision | Choice | Where documented |
|---|---|---|
| ADO API for process calls | Raw `HttpClient` + Basic auth | research.md § Decision 1 |
| Retry policy | Hand-rolled 3× exp-backoff, no Polly | research.md § Decision 2 |
| Config externalization | Embedded JSON resource + optional file override | research.md § Decision 3 |
| Process detection | Process list + project configuration endpoints | research.md § Decision 4 |
| Manifest path | `SpecKit:SpecsRoot` + `feature.json` lookup | research.md § Decision 5 |
| Settings panel | Extend `ConnectorConfigModal.razor` | research.md § Decision 6 |
| SK step placement | `DBAIAzure.Processes/Steps/`, typed events | research.md § Decision 7 |
| Picklist creation | Separate lists API call, 409 = idempotent | research.md § Decision 8 |
