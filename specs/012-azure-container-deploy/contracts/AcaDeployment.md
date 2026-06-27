# Contract: Azure Container Apps deployment

**Type**: Local deploy artifacts — `deploy/aca/deploy.ps1`, `deploy/aca/seed-secrets.ps1`,
`deploy/aca/team.env.example`. Mirrors the reference `C:\ProjectsWin\DBAI\deploy\aca\` model using
the `az` CLI (no Bicep/ARM/Actions).

**Purpose**: Build the image to ACR and create/update an ACA app that is publicly reachable at one
URL, scales to zero when idle, wakes on request, and carries vault-sourced back-office secrets — all
reproducibly from committed config, with no secret committed.

## Deployment contract

| # | Requirement | Maps to |
|---|-------------|---------|
| 1 | Build/push the image to **Azure Container Registry** via `az acr` (local build, no Docker Hub) | research Decision 1 |
| 2 | Create the ACA app with `--ingress external --target-port 8080 --min-replicas 0 --max-replicas 1 --cpu 0.25 --memory 0.5Gi` | FR-001, FR-010, FR-018, research D1/D6 |
| 3 | Re-assert `--min-replicas 0 --max-replicas 1` after deploy (idempotent guard, as the reference does) | FR-012 |
| 4 | Idle scale-in uses the **ACA/KEDA platform default** (no custom timer) | FR-012, research D2 |
| 5 | No authentication is configured (no EasyAuth / identity provider) | FR-017 |
| 6 | Back-office secrets are set as **ACA secrets** with env-var **secretrefs** (`--secrets name=val --env-vars KEY=secretref:name`) | FR-006, Article IX |
| 7 | Secret **values** are sourced from the Forge Vault at deploy time via `seed-secrets.ps1`; the agent never handles plaintext; the real `deploy/aca/team.env` is gitignored | FR-006, FR-014, Article IX |
| 8 | The **LLM key is not** among the deployed secrets/env | FR-004, SC-006 |
| 9 | Output the public FQDN; re-feed it to the app (e.g. `Portal:BaseUrl`/`APP_PUBLIC_URL`) for correct deep links | FR-001 |
| 10 | The deployment is reproducible from committed files (`Dockerfile`, `deploy/aca/*`, `team.env.example`); only values are withheld | FR-014, SC-007 |

## `team.env.example` (committed — names only, no values)

Lists the env keys the demo seeds (ServiceNow, Azure DevOps, Messaging) so a deployer knows what to
provide from the vault. Contains **no secret values**. The real `team.env` (values) is gitignored.

## Secret-injection flow (zero-knowledge, Article IX)

```
seed-secrets.ps1
  → mcp__forge-vault__vault_inject({ secret_names: [ ...back-office secret names... ] })
  → source the returned script into the deploy shell (values live only in the shell)
deploy.ps1
  → az acr build/push image
  → az containerapp create/update ... --secrets <name=value from shell> --env-vars KEY=secretref:name
  → az containerapp update --min-replicas 0 --max-replicas 1   # re-assert scale-to-zero
  → print the FQDN
# No secret value is ever written to a committed file, a log, or the conversation.
```

## Verification (Article X — on a real deployment, via quickstart)

- Open the printed FQDN from a clean machine → app loads, no login, only an LLM-key field to fill.
- Enter an LLM key → run a ticket end-to-end against the seeded connectors.
- Open the same URL in a second browser → both observe a live run without errors.
- Leave idle past the platform window → 0 replicas (no compute) → reopen → wakes and serves.
- Audit: `az containerapp secret list` shows secrets masked; the repo/logs contain no secret values;
  connectors still function (proves vault delivery).
