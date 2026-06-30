# Jira work-tracker setup (spec-018)

To target Jira Cloud instead of Azure DevOps, set the active tracker and the Jira connection settings.
The API token is a secret — supply it via environment/vault, never commit it.

## Configuration (`appsettings` / environment)

```jsonc
{
  "WorkTracker": {
    "Active": "Jira",                 // "AzureDevOps" (default) | "Jira"
    "Jira": {
      "SiteUrl": "https://your-org.atlassian.net",
      "Email": "automation@your-org.com",
      "ApiToken": "<secret — env/vault>",
      "ProjectKey": "PROJ"
    }
  }
}
```

Environment-variable form (double-underscore): `WorkTracker__Active=Jira`,
`WorkTracker__Jira__SiteUrl=…`, `WorkTracker__Jira__ApiToken=…` (inject via the vault).

## What happens on startup

The startup field-provisioning hook resolves the **active** adapter and calls `ProvisionFieldsAsync`:
- **Jira** → `JiraFieldProvisioner` find-or-creates each telemetry/cost custom field by name and ensures a
  global context (so values are writable via the REST API). Idempotent — safe to re-run.
- The pipeline then creates issues, stamps the binding key, and projects cost onto the issues — through
  the same tracker-neutral code path as ADO.

## Rollup

Per-item cost fields are populated on every issue; **hierarchical rollup needs Advanced Roadmaps** (or a
marketplace aggregation) — see [`docs/work-tracker-rollup.md`](./work-tracker-rollup.md). The adapter
reports this via `GetRollupCapability()` (`RequiresAddOn`).

## Notes / follow-ups

- Tracker selection is per application instance (`WorkTracker:Active`); per-project routing is a reserved
  extension point (`IWorkTrackerAdapterProvider`), not built.
- The per-item **token snapshot** (`TelemetryWriteBackService`) is currently ADO-numeric and skipped for
  Jira issue keys — a follow-up; the cost ledger + projection are fully tracker-neutral.
- A live Jira round-trip has not been run (no Jira Cloud site wired into this environment).
