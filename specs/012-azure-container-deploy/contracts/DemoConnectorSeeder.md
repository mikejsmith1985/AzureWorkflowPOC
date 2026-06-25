# Contract: DemoConnectorSeeder

**Type**: Startup service, `src/DBAIAzure.Web/Services/DemoConnectorSeeder.cs`, invoked from the
post-`Build()` startup scope in `Program.cs` (after `EnsureCreatedAsync`, before the app serves
traffic). This is the one new runtime component (the documented Article VII gap: nothing seeds
connectors from environment).

**Purpose**: On each cold start, pre-configure the demo's back-office connectors from
vault-injected environment variables so the app works out of the box — while leaving the LLM
connector unseeded so each visitor supplies their own key.

## Surface

```csharp
public sealed class DemoConnectorSeeder
{
    /// <summary>
    /// Seeds the ServiceNow, Azure DevOps, and Messaging connectors from environment configuration
    /// (vault-injected at deploy time). Never seeds the LLM connector. Idempotent; tolerant of
    /// missing values (an under-specified connector is left unconfigured, not half-written).
    /// </summary>
    Task SeedAsync(CancellationToken cancellationToken = default);
}
```

## Behavioral contract

| # | Given | Then | Maps to |
|---|-------|------|---------|
| 1 | Env provides full ServiceNow / ADO / Messaging values | Each connector row is created via the existing repository with secrets encrypted via `ISecretProtector`, `IsConfigured = true` | FR-005, FR-006 |
| 2 | Any connector's required env values are missing | That connector is left unconfigured; a non-secret info log notes it; boot continues (no crash) | FR-015, edge cases |
| 3 | The LLM connector | Is **never** seeded — no row, no key, regardless of env | FR-004, SC-006 |
| 4 | `SeedAsync` runs on a fresh (cold-start) DB | Produces exactly one row per seeded connector type | FR-016 |
| 5 | `SeedAsync` runs when rows already exist (re-run safety) | Idempotent — overwrites to the seeded defaults without duplicating rows | — |
| 6 | Any secret value | Never written to logs, never echoed; only stored encrypted | FR-006, Article IX |
| 7 | After seeding | Non-secret fields (URLs, org/project, tool/target) are stored plain; secret fields encrypted; the UI masks secrets on read-back | FR-009 |
| 8 | A visitor later repoints a seeded connector | The seeder does not run again until the next cold start, so the visitor's value stands (last-writer-wins) | FR-007, FR-008, FR-018 |

## Inputs (environment variable names — values from the Forge Vault, never committed)

Names are illustrative and finalized against the app's existing connector config shape and the
project's live-verify secret set; **only names are committed** (in `team.env.example`):

- ServiceNow: base URL (non-secret) + username/password or token (secret)
- Azure DevOps: organization + project (non-secret) + PAT (secret)
- Messaging: platform + endpoint/target (non-secret) + auth token / webhook (secret)
- LLM: **absent by design**

## Tests (Article V)

- **Unit** (`DemoConnectorSeederTests`): with a fake repository + fake `ISecretProtector` and a
  controlled env source — seeds the three connectors; never creates an LLM row even if an LLM key is
  present in env; tolerates each connector's missing values independently; asserts no secret value is
  passed to the logger.
- **Integration** (`DemoConnectorSeederIntegrationTests`): against the real
  `SqliteConnectorConfigRepository` + real Data Protection — after `SeedAsync`, the rows exist and
  secrets round-trip via `GetDecryptedSecretsAsync`; the LLM connector is absent so an LLM-dependent
  action prompts for a key.
