# Quickstart Validation Guide: ADO Telemetry Field Bootstrap

**Feature**: specs/008-ado-telemetry-bootstrap  
**Date**: 2026-06-23

This guide describes how to validate that the feature works end-to-end. Follow each scenario in order; each builds on the prior.

---

## Prerequisites

1. A running instance of `DBAIAzure.Web` (via `dotnet run` or the existing `build-web.cmd` script).
2. An Azure DevOps organization URL and project name. The user's ADO is `https://dev.azure.com/mikejsmith1985rll`. Use a test project for validation to avoid polluting production work item types.
3. A PAT stored in the Forge Vault (vault entry: per `reference_live-verify-secrets.md`) with **Process Read/Write** and **Work Items Read/Write** scopes for Bootstrap Mode validation.
4. A second PAT with **read-only** project scope for Adaptive Mode validation.
5. `dotnet test` passes baseline before any changes.

---

## Scenario 1 — Bootstrap Mode: first-time field creation

**What we are proving**: FR-006, FR-007, FR-009, SC-001, SC-002, SC-003 (idempotency).

### Setup
1. Open the application in a browser.
2. Open the global Settings panel (existing connector modal).
3. In the ADO connection section, enter the org URL and project name.
4. Inject the admin-scoped PAT via Forge Vault.

### Steps
1. Click "Test Connection" in the ADO connection section.
2. Observe the result displayed inline in the Settings panel.

### Expected outcome
- The panel shows mode: **Bootstrap**.
- The manifest summary lists the fields created (first run: all 12 + 2 = 14 fields) or fields already existing (repeat run: all 14).
- No error message.
- Inspect the ADO project via the ADO web UI → Project Settings → Process → Work Item Types → User Story (or PBI for Scrum) → Fields. All `Custom.AI*` fields and `Custom.SpeckitPhase` are present.
- Check `specs/008-ado-telemetry-bootstrap/.ado-bootstrap-manifest.json` — file exists and contains `"mode": "bootstrap"`, `"mappingStrategy": "preferred"`, and the field lists.

### Idempotency check
5. Click "Test Connection" a second time without changing anything.
6. Observe the panel — no error, `"fieldsCreated": []`, all fields in `"fieldsExisting"`. File on disk updated with new timestamp.

---

## Scenario 2 — Adaptive Mode: no admin rights

**What we are proving**: FR-010, FR-011, FR-012, SC-004.

### Setup
1. Change the PAT in the Forge Vault to a read-only token (no process write permission).

### Steps
1. Click "Test Connection".
2. Observe the result.

### Expected outcome
- The panel shows mode: **Adaptive**.
- The manifest summary shows the field mapping (e.g. `Custom.AISessionID → System.Tags`, `Custom.AIInputTokens → Microsoft.VSTS.Scheduling.StoryPoints`).
- Fields with no native fallback are listed as "log only".
- No error halts the display.
- Check `.ado-bootstrap-manifest.json` — contains `"mode": "adaptive"` and the full `"mapping"` object.

---

## Scenario 3 — Missing configuration halts cleanly

**What we are proving**: FR-004, SC-005.

### Setup
1. Clear the org URL in the ADO connection section (leave it blank).

### Steps
1. Click "Test Connection".

### Expected outcome
- The panel displays a clear error: missing ADO organization URL.
- No network call was made (no ADO API errors in application log).
- No manifest file written / previous manifest untouched.

---

## Scenario 4 — Unreachable ADO org halts cleanly

**What we are proving**: FR-013.

### Setup
1. Enter an invalid org URL (e.g. `https://dev.azure.com/this-org-does-not-exist-xyz123`).

### Steps
1. Click "Test Connection".

### Expected outcome
- The panel displays a clear diagnostic error: org not reachable.
- No manifest written.
- The pipeline does not attempt any subsequent ADO operation.

---

## Scenario 5 — Unsupported process type

**What we are proving**: FR-005, edge case "Unsupported ADO process type".

### Setup
1. Point the configuration at an ADO project using CMMI (create a CMMI project in the test org if needed).

### Steps
1. Click "Test Connection".

### Expected outcome
- The panel displays a clear error naming the detected process type (e.g. "CMMI process is not supported").
- No field creation or mapping attempted.
- No manifest written.

---

## Scenario 6 — Workflow-level field config override

**What we are proving**: FR-015, US4.

### Setup
1. Prepare a custom config JSON that omits the cost fields (`Custom.AIEstimatedCostUSD`, `Custom.AIToolAcceptRatePct`, `Custom.AICacheHitRatePct`).
2. Place the file at the path configured in `IConfiguration["AdoTelemetry:ConfigPath"]` (or supply it as a workflow-level override if the UI exposes that).

### Steps
1. Click "Test Connection".

### Expected outcome
- The manifest lists only the non-omitted fields.
- The three omitted fields do not appear in the ADO project (if Bootstrap Mode) or in the mapping (if Adaptive Mode).

---

## Scenario 7 — Automatic preflight on pipeline startup

**What we are proving**: FR-001, SC-006.

### Setup
1. Valid ADO configuration (admin PAT).
2. Restart the application.

### Steps
1. Observe application startup logs.
2. Trigger a phase-complete signal to start a pipeline run.

### Expected outcome
- Startup log contains a preflight completion entry (mode and manifest path).
- The pipeline run proceeds to work item creation without error.
- Work items are written using `Custom.*` fields (not Tags fallback), confirming the Bootstrap manifest is honored.

---

## Automated test coverage

The following test files provide code-level coverage (to be created in `tests/DBAIAzure.Tests/`):

| Test file | What it covers |
|---|---|
| `Steps/AdoTelemetryPreflightStepTests.cs` | SK step emits `AdoPreflightSucceeded` / `AdoPreflightFailed` events; reads config from service; writes manifest path to step state |
| `Services/AdoTelemetryPreflightServiceTests.cs` | Bootstrap: creates fields, skips existing, retries on 429, records partial failures; Adaptive: builds mapping, handles log-only; process type detection returns Agile/Scrum/Unsupported; missing config returns failure result |

Run with: `dotnet test tests/DBAIAzure.Tests/ --filter "FullyQualifiedName~AdoTelemetry"`

---

## References

- Data model: [data-model.md](./data-model.md)
- Service contract: [contracts/preflight-service-interface.md](./contracts/preflight-service-interface.md)
- Field config schema: [contracts/ado-telemetry-config.schema.json](./contracts/ado-telemetry-config.schema.json)
