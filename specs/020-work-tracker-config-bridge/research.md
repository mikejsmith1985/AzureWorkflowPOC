# Phase 0 Research: Work-Tracker Config Bridge

Decisions resolving the Technical Context. Each is grounded in the existing codebase seams (spec-018
adapters, spec-002 connector-config store, the LLM hot-reload pattern) so the bridge **extends** proven
patterns rather than inventing new ones (Constitution Article VII).

## D1 — Generic connector identity (`ConnectorType.WorkTracker`)

**Decision**: Add a single `ConnectorType.WorkTracker` member. The vendor-specific `ConnectorType.AzureDevOps`
member is **retained in the enum for backward-compatible parsing of legacy rows only** but removed from the
operator-facing connector set (`AllConnectorTypes` in `SqliteConnectorConfigRepository`), so the UI renders
one generic "Work Tracking System" card instead of an ADO card. Its non-secret JSON is a **discriminated
shape** carrying a `provider` field.

**Rationale**: Storage is string-keyed and migration-less (rows keyed by `ConnectorType.ToString()`; table
created via `EnsureCreated` + a hand-rolled `CREATE TABLE IF NOT EXISTS` in `Program.cs`), so a new member
needs **no schema change**. Keeping the `AzureDevOps` member avoids breaking `MapToConfig` when it reads a
dormant legacy row post-migration.

**Alternatives rejected**: (a) Separate per-vendor connector types + a standalone active-tracker picker
(Q1 option B) — two places to reason about, no single generic card. (b) Deleting the `AzureDevOps` enum
member — breaks legacy-row parsing during migration.

**Supersedes**: spec-018 `data-model.md` line 71 ("`ConnectorType` gains `Jira`"). Spec-020 uses one generic
`WorkTracker` type with a `provider` discriminator instead of a `Jira` sibling member (Clarification Q1).

## D2 — Discriminated config + secret shapes

**Decision**: The `WorkTracker` connector's **non-secret JSON** carries `provider` plus that provider's
non-secret fields; the **encrypted secret blob** carries the provider's single secret.

- Azure DevOps: non-secret `{ "provider": "AzureDevOps", "organizationUrl": "...", "projectName": "..." }`;
  secret `{ "personalAccessToken": "..." }` (unchanged field names → ADO regression-safe).
- Jira: non-secret `{ "provider": "Jira", "siteUrl": "...", "email": "...", "projectKey": "..." }`;
  secret `{ "apiToken": "..." }`.

**Rationale**: Mirrors the existing per-connector JSON convention (`AzureDevOpsConnectorConfig`), keeps ADO's
existing field names so `AzureDevOpsBoardsClient` reads them unchanged, and stores exactly one secret per
provider in the existing `EncryptedSecretsJson` blob via `ISecretProtector` (Article IX — never plaintext).

**Alternatives rejected**: A generic `{ "secret": "..." }` key — loses the self-documenting field name and
would force ADO's reader to change.

## D3 — Active-tracker resolution moves to the connector store (per run)

**Decision**: Introduce `IWorkTrackerConfigResolver` — a single service that reads the `WorkTracker` row via
`IConnectorConfigRepository` (`GetAsync` + `GetDecryptedSecretsAsync`) and returns `{ Provider, non-secret
values, decrypted secret }`, or "unconfigured". `WorkTrackerAdapterProvider.GetAdapter()` calls it **per run**
and selects the adapter whose `TrackerKey` equals the resolved `provider`. The startup-only `WorkTracker:Active`
config key is demoted to a **first-run seed/fallback** when no `WorkTracker` row exists.

**Rationale**: Centralizes "read the row, parse provider, decrypt secret" in one place so the ADO client, the
Jira client, the tester, and the provider all dispatch on the same resolved value — no duplicated parsing.
Matches FR-004/FR-005 (stored config is the source of truth, resolved per run).

**Alternatives rejected**: Keeping `WorkTracker:Active` as the authority (env-only, never surfaced — the
current broken state) or each consumer re-reading the row independently (duplicated, drift-prone).

## D4 — Jira adapter converted to per-run resolution (the core fix)

**Decision**: Remove the startup-baked named `"Jira"` `HttpClient` (which bakes `BaseAddress` + Basic-auth
header once from appsettings). Replace with a `JiraConnectionFactory` that resolves Jira config per call via
`IWorkTrackerConfigResolver` and **rebuilds the authed client only when the resolved config changes** (cache
key `siteUrl|email|apiToken`). The `JiraWorkTrackerAdapter` becomes a stateless singleton that obtains its
client from the factory on each operation.

**Rationale**: This is a **direct port of the ADO pattern** — `AzureDevOpsBoardsClient.GetClientAsync` already
does exactly this (rebuild cached `VssConnection` when `organizationUrl|pat` changes). It is the specific
change that makes UI-entered Jira credentials take effect without restart (FR-005).

**Alternatives rejected**: `IHttpClientFactory` typed client with a `DelegatingHandler` that injects auth per
request — viable, but the connection-cache-keyed-on-config pattern is already the house standard (ADO + LLM),
so consistency wins.

## D5 — Connection test reuses the existing health-checker seam

**Decision**: Add a `JiraConnectorTester` that plugs into the existing `IConnectorHealthChecker` seam
(`TestAsync` → `ConnectorTestResult`). It probes auth + reachability (`GET /rest/api/3/myself`) and project
existence (`GET /rest/api/3/project/{projectKey}`), returning a `ConnectorTestResult` with an actionable
message. The UI's "Test Connection" resolves the tester for the **selected provider** and persists the
outcome via `UpdateTestResultAsync`. ADO keeps its existing preflight-based tester.

**Rationale**: `ConnectorTestResult` and `IConnectorHealthChecker.TestAsync` already exist and are the
documented home for a functional connector test (spec-002). Framework-first: extend it, don't invent a
parallel tester interface. `GET /myself` is Jira's canonical cheap auth probe; it creates no work item (safe).

**Alternatives rejected**: Adding `TestConnectionAsync` to `IWorkTrackerAdapter` — bloats the pipeline-facing
contract with a UI concern; a create-and-delete probe — unsafe and slow.

## D6 — One-time idempotent auto-migration of the existing ADO connector

**Decision**: At startup, immediately after the DB-init block in `Program.cs`, run an idempotent migration:
if an `AzureDevOps` connector row exists **and** no `WorkTracker` row exists, write a new `WorkTracker` row —
non-secret JSON = the ADO JSON with `"provider":"AzureDevOps"` injected, `EncryptedSecretsJson` copied
verbatim (secret preserved, never decrypted), plus `IsConfigured` and the last test result. The legacy
`AzureDevOps` row is left **dormant** (no longer surfaced). Re-running is a no-op (guarded by the WorkTracker
row's presence).

**Rationale**: Satisfies FR-015 / SC-003 ("zero manual reconfiguration") and follows the spec-019 precedent
of an in-place, idempotent, one-time migration. Copying (not moving) the encrypted blob avoids any window of
data loss; the migration never handles plaintext.

**Alternatives rejected**: Operator re-entry on upgrade (Q3 option B) — violates SC-003. Deleting the legacy
row during migration — removes the rollback safety net for one release.

## D7 — Generic UI card with provider selector

**Decision**: `ConnectorSettings.razor`'s `WorkTracker` card renders a **provider `<select>`** (Azure DevOps /
Jira) with provider-conditional sub-forms and help text (ADO: org URL / project / PAT; Jira: site URL / email
/ API token / project key). The `ConnectorEntry` draft model gains `DraftProvider` and the extra Jira fields
(email, project key). `LoadDraftFromJson`/`SerializeToJson` switch arms move from `AzureDevOps` to
`WorkTracker` and branch on the selected provider. `OnboardingBanner` and `UserGuide` copy are generified; the
provider name shown always reflects the active selection (FR-002/FR-013, SC-006).

**Rationale**: One card, one enum arm, provider-conditional rendering — the minimal change that makes the UI
truly generic rather than ADO-shaped-with-a-rename.

## D8 — Field provisioning follows the active adapter

**Decision**: Startup/first-run field provisioning and the "provision fields" action resolve the **active**
adapter via `IWorkTrackerAdapterProvider.GetAdapter()` and call `ProvisionFieldsAsync`, so provisioning runs
against whichever provider is selected (ADO `Bootstrap/Adaptive`, Jira `JiraFieldProvisioner`). The current
startup provisioning that targets ADO directly is repointed to the active adapter (FR-011).

**Rationale**: The adapter contract already exposes `ProvisionFieldsAsync`; routing provisioning through the
provider is the same seam the pipeline already uses. The earlier startup log
`tracker=AzureDevOps mode=Failed` was ADO provisioning firing regardless of selection — this repoint fixes
that coupling too.
