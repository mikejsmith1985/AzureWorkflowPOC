# Phase 1 Data Model: Work-Tracker Config Bridge

No new persisted table. The change is one new logical connector type over the **existing**
`ConnectorConfigs` row model (spec-002) plus a few new in-memory records. Cost/binding/adapter data
(spec-016/017/018) are unchanged.

## 1. `ConnectorType.WorkTracker` (new enum member)

The single generic work-tracker connector identity. `AzureDevOps` stays in the enum for legacy-row parsing
but is dropped from the operator-facing `AllConnectorTypes` set (D1). Persisted as its string name — **no
schema migration**.

## 2. `WorkTracker` connector row (existing `ConnectorConfigRecord`, new content)

Reuses the existing columns; only the JSON payloads are new shapes (D2):

| Column (existing) | Content for `WorkTracker` |
|---|---|
| `ConnectorType` | `"WorkTracker"` |
| `ConfigJson` (non-secret) | Discriminated: `{ "provider": "AzureDevOps"\|"Jira", ...provider fields }` |
| `EncryptedSecretsJson` | Provider secret blob (`{ "personalAccessToken" }` or `{ "apiToken" }`), encrypted via `ISecretProtector` |
| `IsConfigured`, `LastTestResult`, `LastTestMessage`, `LastTestedAt`, `LastUpdatedAt` | Unchanged semantics |

## 3. `WorkTrackerProvider` (new discriminator)

`AzureDevOps | Jira` — the provider selected on the generic connector. String value equals the adapter's
existing `IWorkTrackerAdapter.TrackerKey` ("AzureDevOps" / "Jira"), so resolution is a direct key match.

## 4. Non-secret config records (new)

| Record | Fields | Notes |
|---|---|---|
| `AzureDevOpsConnectorConfig` *(existing)* | `OrganizationUrl`, `ProjectName` | Reused unchanged; now nested under the discriminated JSON |
| `JiraConnectorConfig` *(new)* | `SiteUrl`, `Email`, `ProjectKey` | Non-secret Jira fields. **Secret (`apiToken`) NOT here** |

## 5. `ResolvedWorkTrackerConfig` (new — in-memory, from `IWorkTrackerConfigResolver`)

| Field | Type | Notes |
|---|---|---|
| `Provider` | `WorkTrackerProvider` | Which adapter is active |
| `NonSecretJson` | `string?` | Raw discriminated non-secret JSON |
| `DecryptedSecret` | `string?` | Decrypted secret JSON (server-side only, never to UI) |
| `IsConfigured` | `bool` | False when no `WorkTracker` row / no provider set |

Resolved **per run** (D3). Consumers (ADO client, Jira factory, tester, adapter provider) dispatch on `Provider`.

## 6. `ConnectorEntry` draft (existing UI model — extended)

Adds `DraftProvider` (the selector) and the extra Jira inputs so one card serves both providers:

| Draft field | ADO meaning | Jira meaning |
|---|---|---|
| `DraftProvider` *(new)* | "AzureDevOps" | "Jira" |
| `DraftUrl` | Organization URL | Site URL |
| `DraftUsername` | Project name | *(unused — Jira uses ProjectKey)* |
| `DraftEmail` *(new)* | *(unused)* | Account email |
| `DraftProjectKey` *(new)* | *(unused)* | Project key |
| `DraftSecret` | PAT | API token |

`LoadDraftFromJson` / `SerializeToJson` gain a `WorkTracker` arm that branches on `DraftProvider` (D7).

## 7. `ConnectorTestResult` (existing — reused for Jira)

Reused verbatim (`Type`, `IsSuccess`, `Message`, `TestedAt`) as the return of the new `JiraConnectorTester`
via the existing `IConnectorHealthChecker` seam (D5). `Type` = `WorkTracker`.

## 8. Unchanged (reused as-is)

- `IWorkTrackerAdapter` and both adapters' operation contracts (create/upsert/set-fields/comment/provision,
  `WorkItemRef`, `LogicalField`, `ProvisioningResult`, `RollupCapability`) — spec-018, untouched.
- `IConnectorConfigRepository`, `ISecretProtector`, `ConnectorConfigRecord` table, `PipelineDbContext` — the
  storage seam is reused; only new JSON content and a new enum value flow through it.
- Cost ledger, binding key, dev-usage ingest — tracker-neutral, unchanged.

## 9. Migration (one-time, idempotent — D6)

Existing `AzureDevOps` row → new `WorkTracker` row: inject `"provider":"AzureDevOps"` into the non-secret
JSON, copy `EncryptedSecretsJson` verbatim, carry `IsConfigured`/test result. Guarded by absence of a
`WorkTracker` row (re-run = no-op). Legacy row left dormant. No plaintext handled; no data deleted.
