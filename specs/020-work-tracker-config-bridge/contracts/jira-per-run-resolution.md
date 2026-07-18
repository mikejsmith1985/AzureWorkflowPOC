# Contract: Jira per-run credential resolution (`JiraConnectionFactory`)

Converts the Jira adapter from startup-baked auth to per-run resolution — the core change that makes
UI-entered Jira credentials take effect without a restart (D4, FR-005). Direct port of
`AzureDevOpsBoardsClient.GetClientAsync`.

## Before (current — broken for UI config)

- Named `"Jira"` `HttpClient` in `Program.cs` bakes `BaseAddress` + Basic-auth header **once** at DI-build
  from appsettings `WorkTracker:Jira`. `JiraWorkTrackerAdapter` is a singleton holding that pre-authed client.
- Consequence: saving Jira credentials in the UI has **no effect** until the app restarts.

## After (target)

```csharp
public interface IJiraConnectionFactory
{
    /// Returns an authed HttpClient for the current Jira config, rebuilt only when the resolved
    /// config changes. Throws JiraNotConfiguredException when the active provider is not Jira / unconfigured.
    Task<HttpClient> GetClientAsync(CancellationToken ct = default);
}
```

## Behavioral contract

- **Per-call resolution**: obtains config from `IWorkTrackerConfigResolver.ResolveActiveAsync` on each call.
- **Rebuild-on-change only**: caches the authed client keyed by `siteUrl|email|apiToken`; rebuilds only when
  that key changes (same cache discipline as ADO's `organizationUrl|pat`).
- **Auth**: Basic auth header = base64(`email:apiToken`); `BaseAddress` = `siteUrl`.
- **`JiraWorkTrackerAdapter`** stays a stateless singleton but takes `IJiraConnectionFactory` (not a
  pre-authed client) and calls `GetClientAsync()` at the top of each operation.
- **Removed**: the startup-baked named `"Jira"` client auth and the appsettings-only `JiraOptions` singleton
  as the credential source (appsettings retained only as a first-run seed).

## Parity check

| Concern | ADO (reference) | Jira (target) |
|---|---|---|
| Config source | DB row, per call | DB row, per call |
| Connection cache key | `organizationUrl\|pat` | `siteUrl\|email\|apiToken` |
| Rebuild trigger | key change | key change |
| Startup baking | none | none (after change) |
