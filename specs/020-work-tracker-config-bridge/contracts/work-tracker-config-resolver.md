# Contract: `IWorkTrackerConfigResolver` (new internal seam)

The single place that reads the generic `WorkTracker` connector row, decrypts its secret, and reports which
provider is active. Every downstream consumer dispatches on its result — no duplicated row parsing (D3).

## Interface

```csharp
public interface IWorkTrackerConfigResolver
{
    /// Reads the active WorkTracker connector (non-secret + decrypted secret) from the store, per call.
    /// Returns IsConfigured = false when no WorkTracker row exists or no provider is set.
    Task<ResolvedWorkTrackerConfig> ResolveActiveAsync(CancellationToken ct = default);
}
```

## Behavioral contract

- **Per-call read**: reads `IConnectorConfigRepository.GetAsync(ConnectorType.WorkTracker)` +
  `GetDecryptedSecretsAsync(...)` on every call — never caches config across calls (FR-004/FR-005).
- **Provider precedence**: the active provider is the `WorkTracker` row's `provider` field. When no row
  exists, falls back to the `WorkTracker:Active` seed config value; when neither is present, `IsConfigured`
  is false.
- **Secret isolation**: `DecryptedSecret` is populated server-side only; callers in Blazor UI code MUST use
  a non-decrypting path (Article IX).
- **Best-effort**: a store/crypto error resolves to `IsConfigured = false` with a logged reason, never throws
  into the pipeline (FR-012).

## Consumers

| Consumer | Uses |
|---|---|
| `WorkTrackerAdapterProvider.GetAdapter()` | `Provider` → select adapter by `TrackerKey` |
| `AzureDevOpsBoardsClient.ResolveAllConfigAsync` | when `Provider == AzureDevOps`, parse org/project/PAT |
| `JiraConnectionFactory` | when `Provider == Jira`, parse site/email/token + build authed client |
| `JiraConnectorTester` | probe with resolved Jira credentials |
