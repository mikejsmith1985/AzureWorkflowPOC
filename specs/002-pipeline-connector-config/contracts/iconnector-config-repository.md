# Contract: IConnectorConfigRepository

**Namespace**: `DBAIAzure.Core.Interfaces`  
**Implementation**: `DBAIAzure.Storage.Repositories.SqliteConnectorConfigRepository`  
**Registered as**: Scoped (via `IDbContextFactory<PipelineDbContext>`, consistent with existing repositories)

---

## Purpose

Provides read/write access to connector configuration records persisted in the SQLite database.
Abstracts the storage layer so that Blazor components, orchestrators, and connector classes can
retrieve current credentials without depending on EF Core directly.

Secrets are decrypted only when explicitly requested by a caller that needs them for a live
connection (e.g., `IConnectorHealthChecker`). The Blazor UI never receives secret values —
it reads only `ConnectorConfig`, which exposes `HasSecrets` (bool), not the values themselves.

---

## Method Signatures

```csharp
/// <summary>
/// Returns the current configuration record for the given connector type,
/// or null if no settings have been saved yet.
/// </summary>
Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default);

/// <summary>
/// Returns configuration records for all four connector types.
/// Connectors that have never been configured are returned with IsConfigured = false.
/// </summary>
Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default);

/// <summary>
/// Persists non-secret fields and (if provided) replaces the encrypted secret blob.
/// Passing null for plaintextSecretsJson leaves the existing encrypted secret unchanged
/// (write-only semantics — blank field on update = preserve existing secret).
/// Sets IsConfigured = true and updates LastUpdatedAt.
/// Resets LastTestResult to null (per FR-017 — any field change invalidates the test).
/// </summary>
Task SaveAsync(
    ConnectorType type,
    string? nonSecretConfigJson,
    string? plaintextSecretsJson,
    CancellationToken ct = default);

/// <summary>
/// Returns the decrypted secrets JSON for the given connector type, for use by
/// connector clients executing a live connection. Returns null if no secrets are stored.
/// MUST NOT be called from Blazor UI components — only from server-side service code.
/// </summary>
Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default);

/// <summary>
/// Records the result of a functional test (manual or pre-flight) against the given connector.
/// Updates LastTestResult, LastTestMessage, and LastTestedAt.
/// Does not affect IsConfigured or LastUpdatedAt.
/// </summary>
Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default);
```

---

## Invariants

- `GetAsync` and `GetAllAsync` never return a decrypted secret value — only `HasSecrets: bool`.
- `SaveAsync` with `plaintextSecretsJson = null` preserves the existing stored secret unchanged.
- `SaveAsync` always resets `LastTestResult` to null (edit invalidates the prior test result).
- `GetDecryptedSecretsAsync` decrypts using `IDataProtector.Unprotect()` in memory only — the
  plaintext is never written to any log or field.
- The repository is the only layer that calls `IDataProtector`; no other class handles raw secrets.

---

## Error Conditions

| Condition | Behavior |
|-----------|----------|
| No record exists for type | `GetAsync` returns `null`; `GetAllAsync` returns a placeholder with `IsConfigured = false` |
| `GetDecryptedSecretsAsync` called when no secrets stored | Returns `null` |
| `IDataProtector.Unprotect()` fails (key ring rotated, corrupted blob) | Throws `CryptographicException` — caller must treat as "secrets unavailable" and prompt operator to re-enter |
| DB unavailable | Propagates `SqliteException` — callers handle at the orchestrator / UI level |
