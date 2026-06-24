# Research: ADO Telemetry Field Bootstrap

**Feature**: specs/008-ado-telemetry-bootstrap  
**Date**: 2026-06-23

---

## Decision 1 — ADO API strategy for field creation and process management

**Decision**: Use the existing `Microsoft.TeamFoundationServer.Client` SDK (`WorkItemTrackingHttpClient`) for org-level field operations (`GET`/`POST` on `/_apis/wit/fields`). Use raw `HttpClient` with Basic PAT auth for process-scoped WIT field attachment (`/_apis/work/processes/{processId}/workItemTypes/{witRefName}/fields`) and process-list detection (`/_apis/process/processes`), following the identical pattern already established in `AzureDevOpsBoardsClient.TestConnectionAsync`.

**Rationale**: The managed `WorkItemTrackingHttpClient` covers field CRUD reliably. The process-scoped WIT attachment endpoint is outside the WIT client scope and varies by API version; raw HTTP is both explicit and already proven in the codebase. No new NuGet packages needed — `Microsoft.TeamFoundationServer.Client` (20.256.2) ships both `VssConnection` and `WorkItemTrackingHttpClient`; `HttpClient` covers the rest.

**Alternatives considered**:
- `WorkItemProcessDefinitionHttpClient` (also in the package) — potentially covers the process WIT field attachment, but its surface area is less documented and version-pinning with the rest of the SDK introduces risk. Raw REST is unambiguous.
- Separate `Microsoft.TeamFoundation.WorkItemTracking.Process.WebApi` package — not needed; functionality is included in the existing mega-package.

---

## Decision 2 — Retry policy implementation

**Decision**: Hand-roll exponential backoff with 3 attempts per field. Delays: attempt 1 → 2 s, attempt 2 → 4 s, attempt 3 → 8 s (`TimeSpan.FromSeconds(Math.Pow(2, attempt + 1))`). Only retry on `HttpRequestException`, `TaskCanceledException` (timeout), or HTTP 429/503. Do not retry 400-class errors (bad request, 404 on existence check) — those are caller errors.

**Rationale**: 3 retries with exponential backoff is the agreed spec requirement (FR-014). The total worst-case delay per field is 14 s (2+4+8), so even with all 14 fields failing and retrying, the overall budget stays well under the 30-second SC-001 target for the happy path. No Polly dependency introduced — the logic is trivial at this scale and adding Polly would pull in a new transitive dependency for three retry calls.

**Alternatives considered**:
- Polly — appropriate at larger scale but over-engineered for 14 fields with 3 retries.
- Single retry — insufficient to absorb ADO rate-limit windows (429 responses can require a 2–5 s backoff).

---

## Decision 3 — Field configuration externalization

**Decision**: Embed `default-telemetry-config.json` as an `EmbeddedResource` in `DBAIAzure.Core`. Load it via `Assembly.GetManifestResourceStream`. Allow an override file path via `IConfiguration["AdoTelemetry:ConfigPath"]`; if that key is present and the file exists, deserialize it and merge over the default. The per-workflow override (US4) is supplied as an additional JSON payload passed directly to the preflight service call site.

**Rationale**: Embedded resource guarantees the default is always present with no file-not-found risk. The `IConfiguration` override path satisfies deployment-level customization without code changes. Workflow-level overrides are passed in-process (Blazor Server DI), avoiding a second file I/O path for the common case.

**Alternatives considered**:
- Store in database alongside connector config — adds a schema migration and UI surface for a rarely-changed value; overkill for a POC.
- Inline C# `static readonly` constant — non-overridable by non-developers; violates FR-015.

---

## Decision 4 — Process type detection strategy

**Decision**: Call `GET https://dev.azure.com/{org}/_apis/process/processes?api-version=7.1` using `HttpClient` with Basic PAT auth. Inspect each process entry's `type` field (`"Inherited"`) and `name` field (case-insensitive match on `"Agile"` or `"Scrum"`). Then call `GET https://dev.azure.com/{org}/{project}/_apis/work/process/configuration?api-version=7.1` to determine which process the target project inherits from. Fail with a descriptive error for CMMI, hosted XML, or any other type.

**Rationale**: The project-level process configuration endpoint directly returns the process reference for a given project, avoiding the need to enumerate all processes and guess. The org-level process list gives us the set of inherited processes to validate against.

**Alternatives considered**:
- `ProcessHttpClient` from the SDK — requires a `VssConnection` scoped to the org and returns `TeamFoundationProcess` objects; works but adds another SDK surface that is less tested in this codebase.

---

## Decision 5 — Manifest file path resolution

**Decision**: Read `IConfiguration["SpecKit:SpecsRoot"]` (already configured in `appsettings.json`) and append the feature directory from the active `feature.json` file (`<SpecsRoot>/.specify/feature.json` or repo-relative `specs/.specify/feature.json` path derived at runtime). Write the manifest to `<feature-dir>/.ado-bootstrap-manifest.json`. If no active feature directory is resolvable (e.g. `feature.json` is missing), fall back to `<SpecsRoot>/.ado-bootstrap-manifest.json`.

**Rationale**: Reuses the existing `SpecKit:SpecsRoot` configuration that is already set in `appsettings.json`. Writes alongside spec artifacts as agreed in clarification Q1. Fallback to specs root prevents a hard failure when no feature is currently active.

**Alternatives considered**:
- Pass manifest path as a constructor argument to the service — couples callers to path logic; centralizing in the service is cleaner.

---

## Decision 6 — Global Settings panel placement

**Decision**: Extend the existing `ConnectorConfigModal.razor` with a "Test Connection" button and result display area in its `AzureDevOps` section. No new top-level Settings page needed for this feature. The `ConnectorConfigModal.razor` is already the de-facto global settings surface; the ADO connection section just gains one new interactive element.

**Rationale**: The simplest change that satisfies FR-001b and SC-005 without introducing a new routed page or navigation item. The modal is already accessible from every page (it is in `MainLayout.razor`). 

**Alternatives considered**:
- New `/settings` routed page — adds navigation complexity for a single button.
- WorkflowSettingsPanel.razor injection — that panel is per-workflow, not global.

---

## Decision 7 — SK Process step placement

**Decision**: Add `AdoTelemetryPreflightStep` to `DBAIAzure.Processes/Steps/`. The step is invoked from the existing `PhaseHandlerOrchestrator` (or its equivalent run startup path) as the first step before any `CreateWorkItemStep` call. The step emits typed process events (`AdoPreflightSucceeded`, `AdoPreflightFailed`) following the existing event convention.

**Rationale**: Article VII compliance — all pipeline orchestration uses SK Process Framework primitives. The step fits naturally alongside the 15 existing steps. The typed event pattern is consistent with every other step in the codebase.

**Alternatives considered**:
- Run the preflight as a non-step service call in the orchestrator before building the process — valid but bypasses the SK Framework for a step that is genuinely part of the run lifecycle.

---

## Decision 8 — Picklist field creation

**Decision**: Use the ADO REST API to create a picklist first (`POST /_apis/work/processes/lists?api-version=7.1`), then reference the picklist ID when creating the `Custom.SpeckitPhase` field. If picklist creation returns 409 (already exists), retrieve the existing picklist ID and proceed. If the picklist API returns any other error, fall back to creating the field as a plain `string` type and log the downgrade (FR-008).

**Rationale**: Exactly matches the ADO two-step picklist flow documented in the source spec. The 409 idempotency handling is necessary because ADO picklists persist at the org level independently of the field.

**Alternatives considered**:
- Always create as string and skip picklist — violates the spec's intent; picklist provides data validation in ADO forms.
