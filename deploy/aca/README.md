# Deploy the demo to Azure Container Apps

One public URL, scale-to-zero when idle, back-office connectors pre-seeded from the Forge Vault,
visitors supply only their own LLM key. Mirrors the reference app's local `az` deploy model — no
Bicep/ARM, no GitHub Actions (Constitution Article VIII). Full design: `specs/012-azure-container-deploy/`.

## Prerequisites

- `az login` to the target subscription.
- Forge Terminal running with the vault unlocked (back-office connector secrets available).
- The values for the keys below sourced **from the vault** into a gitignored `deploy/aca/team.env`
  (copy `team.env.example`) — never typed by hand, never committed (Article IX).

## Two commands

```powershell
./deploy/aca/seed-secrets.ps1   # loads ConnectorSeed__* values from team.env into this shell (values never printed)
./deploy/aca/deploy.ps1         # builds to ACR, creates/updates the public scale-to-zero ACA app, prints the URL
```

`deploy.ps1` is idempotent and re-runnable; pass `-ResourceGroup/-AcrName/-AppName/-Location` to override
defaults. It refuses to run if `Anthropic__ApiKey` is set in the shell (the LLM key must never be
deployed — FR-004).

## Environment keys the demo seeds (names only — values come from the vault)

Bound to `ConnectorSeedOptions` via the ASP.NET `Section__Key` convention. A connector whose required
values are absent is simply left unconfigured (the demo still runs; visitors can configure it in-app).

| Connector | Keys (required first) |
|-----------|-----------------------|
| ServiceNow (ticketing) | `ConnectorSeed__ServiceNow__InstanceUrl`, `__Username`, `__Password` |
| Azure DevOps (work-items) | `ConnectorSeed__AzureDevOps__OrganizationUrl`, `__ProjectName`, `__PersonalAccessToken` |
| Messaging (Teams/Slack/Discord) | `ConnectorSeed__Messaging__Platform` + either `__WebhookUrl` **or** `__McpServerUrl` (optional: `__McpToolName`, `__McpArgumentTemplate`, `__McpAuthToken`, `__Target`) |

The **LLM key is intentionally absent** — each visitor enters their own in the app (FR-004 / SC-006).

## Validate

After the URL prints, follow `specs/012-azure-container-deploy/quickstart.md` (§3–§7) to verify the
visitor flow, concurrency, runtime repointing, idle→wake, and secret-safety.
