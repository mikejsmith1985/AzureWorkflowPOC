# Data Model: Pipeline Connector Configuration Modal

**Date**: 2026-06-18 | **Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

## EF Core Entity

### `ConnectorConfigRecord` — `DBAIAzure.Storage.Entities`

Persists one configuration row per connector type in the existing SQLite database.

| Column | Type | Nullable | Notes |
|--------|------|----------|-------|
| `Id` | `int` | No | Auto-increment primary key |
| `ConnectorType` | `string` | No | Unique index. Values: `"ServiceNow"`, `"AzureDevOps"`, `"LLM"`, `"Teams"` |
| `ConfigJson` | `string?` | Yes | Non-secret fields serialized to JSON (instance URL, org URL, model name, etc.) |
| `EncryptedSecretsJson` | `string?` | Yes | Secret fields JSON encrypted with `IDataProtector`. Never returned to UI. |
| `IsConfigured` | `bool` | No | True when at least one save has been completed |
| `LastUpdatedAt` | `DateTimeOffset` | No | Set on every save — lightweight change audit (FR-020) |
| `LastTestResult` | `string?` | Yes | `"Pass"` \| `"Fail"` \| `null` (never tested) |
| `LastTestMessage` | `string?` | Yes | Plain-language diagnostic from most recent test |
| `LastTestedAt` | `DateTimeOffset?` | Yes | Timestamp of most recent test (manual or pre-flight) |

**Unique constraint**: `ConnectorType` — exactly one record per connector type.  
**EF Core migration**: Added to `PipelineDbContext` via `DbSet<ConnectorConfigRecord>` + `EnsureCreatedAsync` (consistent with existing schema initialization; no explicit migration files needed for this POC).

---

## Domain Types — `DBAIAzure.Core.Models`

### `ConnectorType` (enum)

```
ServiceNow | AzureDevOps | LLM | Teams
```

### `ConnectorConfig` (record — domain model returned by `IConnectorConfigRepository`)

| Property | Type | Description |
|----------|------|-------------|
| `Type` | `ConnectorType` | Which connector this record describes |
| `NonSecretConfig` | `string?` | Raw JSON of non-secret fields (deserialized by the UI layer) |
| `HasSecrets` | `bool` | True if encrypted secrets are stored — never exposes the actual secret |
| `IsConfigured` | `bool` | True after at least one save |
| `LastUpdatedAt` | `DateTimeOffset` | Timestamp of most recent settings save |
| `LastTestResult` | `ConnectorTestResult?` | Most recent test result (null if never tested) |

### `ConnectorTestResult` (record)

| Property | Type | Description |
|----------|------|-------------|
| `Type` | `ConnectorType` | Which connector was tested |
| `IsSuccess` | `bool` | True if the functional test passed |
| `Message` | `string` | Plain-language result: what was confirmed (pass) or specific failure cause (fail) |
| `TestedAt` | `DateTimeOffset` | When the test ran |

### `PipelinePreflightFailure` (record)

Returned by `PipelineOrchestrator` and `PhaseHandlerOrchestrator` when the live pre-flight check
blocks a run (FR-018). Carries the full list of failing test results so callers can build a
diagnostic message without additional queries.

| Property | Type | Description |
|----------|------|-------------|
| `FailingConnectors` | `IReadOnlyList<ConnectorTestResult>` | Every connector that returned `IsSuccess = false` during the pre-flight `CheckAllAsync()` call. At least one entry is always present. |

**Note**: This is a typed result record, not an exception. Orchestrators return it as a discriminated
outcome so callers (dashboard, API endpoints) can inspect the failures without try/catch.

---

### Per-Connector Non-Secret Config Records

These records are serialized to/from `ConnectorConfig.NonSecretConfig`.

#### `ServiceNowConnectorConfig`

| Field | Type | Description |
|-------|------|-------------|
| `InstanceUrl` | `string` | Full URL of the ServiceNow instance (e.g., `https://acme.service-now.com`) |
| `Username` | `string` | Service account username for Basic Auth |

#### `AzureDevOpsConnectorConfig`

| Field | Type | Description |
|-------|------|-------------|
| `OrganizationUrl` | `string` | ADO organization URL (e.g., `https://dev.azure.com/myorg`) |
| `ProjectName` | `string` | Target project name within the organization |

#### `LlmConnectorConfig`

| Field | Type | Description |
|-------|------|-------------|
| `ProviderEndpoint` | `string` | Base URL of the LLM provider (e.g., `https://api.anthropic.com`) |
| `ModelName` | `string` | Model identifier (e.g., `claude-sonnet-4-6`) |

#### `TeamsConnectorConfig`

No non-secret fields. The webhook URL is entirely a secret (it contains embedded auth tokens)
and is stored only in `EncryptedSecretsJson`.

---

## Secret Field Mapping (per connector)

| Connector | Secret Field(s) stored in `EncryptedSecretsJson` |
|-----------|---------------------------------------------------|
| ServiceNow | `Password` (or API token) |
| Azure DevOps | `PersonalAccessToken` |
| LLM | `ApiKey` |
| Teams | `WebhookUrl` (entire URL is secret) |

The `EncryptedSecretsJson` column holds a JSON object of `{ "FieldName": "value", ... }` for all
secret fields of that connector type, encrypted as a single blob by `IDataProtector`.

---

## Connector Status State Machine

```
           ┌──────────────────┐
           │  Not Configured  │ ←─── factory state (no DB row, or IsConfigured = false)
           └────────┬─────────┘
                    │ operator saves settings
                    ▼
         ┌────────────────────┐
         │  Configured        │ LastTestResult = null
         │  (Untested)        │
         └────────┬───────────┘
                  │ functional test runs (manual or pre-flight)
          ┌───────┴────────┐
          ▼                ▼
   ┌──────────┐     ┌──────────┐
   │  Tested  │     │  Tested  │
   │  (Pass)  │     │  (Fail)  │
   └──────────┘     └──────────┘
          │                │
          │ operator edits any field
          └───────┬────────┘
                  ▼
         ┌────────────────────┐
         │  Configured        │ LastTestResult reset to null
         │  (Untested)        │ (per FR-017 — edits invalidate test result)
         └────────────────────┘
```

**Pre-flight behavior**: A run is blocked if any required connector is in **Not Configured** or
**Configured (Untested)** or **Tested (Fail)** state. Only **Tested (Pass)** unblocks a run —
and the pre-flight always runs a fresh live test at run time, not the stored `LastTestResult`.
