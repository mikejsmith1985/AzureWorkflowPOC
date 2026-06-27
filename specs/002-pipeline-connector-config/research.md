# Research: Pipeline Connector Configuration Modal

**Date**: 2026-06-18 | **Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

## Decision 1 — Secret Encryption at Rest

**Decision**: ASP.NET Core Data Protection API (`IDataProtector`)

**Rationale**: The project is an ASP.NET Core 8 application — `IDataProtector` is a first-class
framework primitive available with zero additional dependencies. It provides authenticated
encryption (AES-256-CBC + HMACSHA256 by default), handles key ring rotation automatically, and
integrates with the existing DI container via `AddDataProtection()`. This satisfies FR-019 and
Article IX without introducing custom cryptography or additional Azure services.

The encrypted blob is stored as a base64 string in `ConnectorConfigRecord.EncryptedSecretsJson`.
On read, `IDataProtector.Unprotect()` decrypts in memory; the plaintext never touches disk or logs.

**Alternatives considered**:
- Azure Key Vault: Correct production target for managed secrets, but introduces a new Azure
  service dependency and network call per decryption — disproportionate for a POC where the
  config store and key store would live in the same trust boundary.
- Manual AES: Forbidden by Article VII (build custom only against a documented gap). No gap exists
  — `IDataProtector` fully covers the requirement.
- Store unencrypted: Ruled out by FR-019 and Article IX.

---

## Decision 2 — Connector Config Hot-Reload (No Restart Required)

**Decision**: Resolve credentials from `IConnectorConfigRepository` at connector invocation time,
not at DI registration time.

**Rationale**: The existing `AzureDevOpsBoardsClient` already uses lazy initialization — the
`VssConnection` is created on the first RPC call, not in the constructor. Extending this pattern
to read the PAT from `IConnectorConfigRepository` at first use (or on each call, since the repo
is cheap to query) requires minimal structural change and satisfies FR-014 without introducing
a custom options-reload bus.

For `AnthropicChatCompletionService`, which is currently instantiated with credentials at
startup, the approach is the same: the service accepts an `IConnectorConfigRepository` injection
and resolves `ApiKey` + `Model` from it on each inference call. The existing
`Func<Reporter, Kernel>` factory pattern in `PipelineOrchestrator` already creates a fresh
kernel per run — updating credentials there is straightforward.

**Alternatives considered**:
- `IOptionsMonitor<T>` with a custom DB-backed options source: Correct for fully general
  hot-reload but requires a custom `IOptionsChangeTokenSource` implementation — more complexity
  than needed when the lazy-resolution pattern already exists in the codebase.
- Restart-on-change: Ruled out by FR-014.

---

## Decision 3 — ServiceNow Outbound Client (New — No Existing Implementation)

**Decision**: Build a new `ServiceNowClient` class in `DBAIAzure.Connectors` using a named
`HttpClient` (consistent with `TeamsHitlNotifier`'s pattern) that calls the ServiceNow Table API.

**Rationale**: The current ServiceNow integration is inbound-only (webhook receiver). The
functional test (FR-008) requires an authenticated outbound query. The Table API endpoint
`GET /api/now/table/sys_properties?sysparm_limit=1` with Basic Auth is the lightest possible
authenticated call that proves credentials, instance URL, and permissions are all valid.

There is no official ServiceNow .NET SDK in the project. A lightweight typed `HttpClient`
wrapper is the correct approach — consistent with the `TeamsHitlNotifier` pattern and Article VII
(no bespoke infrastructure; HttpClient is the framework primitive for HTTP).

**Alternatives considered**:
- ServiceNow SDK: No .NET SDK exists for ServiceNow; any community library would add an
  unvetted dependency.
- REST call from a test method only (no reuse): Acceptable for FR-008, but the same client will
  be useful if future pipeline steps need to write back to SNow — better to build it as a proper
  seam now.

---

## Decision 4 — Microsoft Teams Functional Test

**Decision**: POST a labeled Adaptive Card test message to the configured webhook URL and confirm
Teams returns `200 OK` from its own endpoint (not from the pipeline server).

**Rationale**: Teams incoming webhooks accept JSON payloads. A POST with a minimal Adaptive Card
body (title: "Pipeline Config Test", text: "Connectivity verified at [timestamp]") is the
only way to prove the URL is valid, reachable, and that the channel is active. A 200 from Teams
means Teams accepted the message — a genuine round-trip, not a network ping to the pipeline's
own loopback. The existing `TeamsHitlNotifier` uses the same POST pattern and the same named
`HttpClient`; the test method reuses this approach.

**Alternatives considered**:
- GET the URL (OPTIONS/HEAD): Teams webhook URLs do not support GET; any non-POST returns 405.
- Validate URL format only: Ruled out by FR-011 (must prove the channel accepted delivery).

---

## Decision 5 — Blazor Modal Architecture

**Decision**: `ConnectorConfigModal.razor` as a Blazor Server component with local state (no
JavaScript library). The modal is toggled via a `bool _isVisible` field and CSS visibility;
the settings gear button on `Index.razor` calls `modal.Open()`.

**Rationale**: The existing dashboard uses pure Tailwind CSS with Blazor Server components and
no JavaScript UI library. A pure-Blazor modal with Tailwind overlay classes is consistent with
the existing codebase style (dark theme: `bg-gray-900`, `text-gray-100`, `cyan-400` accent).
No third-party modal dependency is introduced.

**Alternatives considered**:
- JavaScript interop (e.g., Bootstrap modal): Adds a JS library dependency without benefit when
  Blazor state management and CSS transitions are sufficient.
- Separate route (`/settings`): Navigating away from the dashboard loses context (active run
  list). The spec explicitly requires a modal that stays in the dashboard.

---

## Decision 6 — Pre-Flight Check Integration Point

**Decision**: `IConnectorHealthChecker.CheckAllAsync()` is called in both
`PipelineOrchestrator.StartRunAsync()` and `PhaseHandlerOrchestrator.StartRunAsync()` as the
first step, before any SK process or pipeline step executes. If any test fails, a
`ConnectorPreflightException` (or equivalent typed result) is returned to the caller and the run
is not started.

**Rationale**: The orchestrators are the single choke point for all run entry. Injecting the
check there means it is impossible to start any run (intake or phase-handler) without a passing
pre-flight, regardless of which trigger path is used. The check is a `Task.WhenAll` over four
`TestAsync()` calls — the SK Process Framework is not involved (no process step needed for a
synchronous gate that runs before the process starts).

**Alternatives considered**:
- A dedicated SK process step: Adds framework overhead for a gate that must block *before* the
  process starts, not within it. The orchestrator boundary is the correct insertion point.
- A middleware pipeline guard: Requires exposing the run-start path as HTTP and coupling the
  guard to ASP.NET Core middleware, which would break the Runner CLI entry point.
