# Contract: One-time ADO → generic connector migration

Idempotent startup migration that carries an existing Azure DevOps connector onto the generic `WorkTracker`
connector with zero operator action (D6, FR-015, SC-003). Follows the spec-019 in-place migration precedent.

## Trigger & guard

- Runs once at startup, immediately after the existing DB-init block in `Program.cs`
  (`EnsureCreated` + `CREATE TABLE IF NOT EXISTS`).
- **Guard (idempotency)**: proceed only if a `AzureDevOps` row exists **and** no `WorkTracker` row exists.
  If a `WorkTracker` row is already present, do nothing.

## Transformation

| Source (`AzureDevOps` row) | Target (`WorkTracker` row) |
|---|---|
| `ConfigJson` = `{ organizationUrl, projectName }` | `{ "provider":"AzureDevOps", organizationUrl, projectName }` |
| `EncryptedSecretsJson` | Copied **verbatim** (never decrypted) |
| `IsConfigured`, `LastTestResult`, `LastTestMessage`, `LastTestedAt` | Copied |
| — | `LastUpdatedAt` = migration time |

Written via a direct repository/DbContext upsert (secret blob copied as ciphertext, so `SaveAsync`'s
plaintext path is bypassed to preserve the exact encrypted bytes).

## Guarantees

- **No data loss**: the legacy `AzureDevOps` row is left dormant (not deleted) for one release as a rollback
  safety net; it is simply no longer surfaced in the UI (`AllConnectorTypes` drops it).
- **No plaintext**: the migration copies ciphertext; it never calls `Unprotect`.
- **Idempotent**: re-running is a no-op; safe across repeated restarts.
- **Post-condition**: after migration, `IWorkTrackerConfigResolver.ResolveActiveAsync` returns
  `Provider = AzureDevOps` and the pipeline behaves exactly as before (SC-003).

## Validation

- Fresh install (no ADO row) → no migration; operator configures via the generic card.
- Existing ADO install → one migration; next run targets ADO with no reconfiguration; second restart is a no-op.
