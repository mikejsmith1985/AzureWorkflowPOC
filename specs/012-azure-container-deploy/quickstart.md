# Quickstart & Validation Guide: One-URL Azure Container Demo Deployment

A runnable guide to build, deploy, and prove the demo deployment. Details live in
[plan.md](./plan.md), [research.md](./research.md), [data-model.md](./data-model.md), and
[contracts/](./contracts). Behavioral target: the reference app at `C:\ProjectsWin\DBAI`.

## Prerequisites

- Azure CLI (`az`) logged in to the target subscription (`az login`), with rights to create a
  resource group, ACR, and an ACA environment + app.
- Docker available locally (for `az acr build` or local image build).
- Forge Terminal running with the vault unlocked (back-office connector secrets available to inject).
- The pinned .NET 8 SDK (repo `global.json`).
- **Never** paste secret values into the shell by hand — they come from the vault via
  `deploy/aca/seed-secrets.ps1`.

## 1. Local container smoke test (before any cloud cost)

```powershell
docker build -t dbai-azure-demo .
docker run --rm -p 8080:8080 -e ASPNETCORE_URLS=http://+:8080 `
  -e <ServiceNow/ADO/Messaging demo env vars with THROWAWAY values> dbai-azure-demo
```
**Pass:** `GET http://localhost:8080` serves the app; back-office connectors show **configured**; the
LLM connector prompts for a key; container logs contain **no** secret values. (Article V container
smoke test; Article X evidence.)

## 2. Deploy to Azure Container Apps

```powershell
# Injects vault values into the deploy shell (zero-knowledge — no plaintext in files/logs):
./deploy/aca/seed-secrets.ps1
# Builds to ACR and creates/updates the ACA app with scale-to-zero + vault secrets:
./deploy/aca/deploy.ps1
```
**Pass:** the script prints a public HTTPS FQDN; `az containerapp show` reports
`minReplicas=0, maxReplicas=1, ingress=external`, no auth; `az containerapp secret list` shows the
back-office secrets present and masked; **no LLM secret** is set.

## 3. Validate US1 — hand someone a URL, run with only an LLM key

1. From a clean machine/browser, open the FQDN.
2. **Pass:** the app loads with no install and no login; the only thing to provide is an LLM API key.
3. Enter an LLM key in-app; start a demo ticket/workflow run.
4. **Pass:** the run executes against the pre-seeded connectors and shows live progress.
5. With the **same** entered key, exercise a design-time LLM feature — the Workflow Builder AI design
   assistant or Node Realization ("make it real"). **Pass:** it works without a restart (proves the
   design-time singletons resolve the visitor's DB-stored key — research Decision 7 / T009A).
6. Without an LLM key, an LLM-dependent run shows a clear "enter your key" prompt (not an opaque
   error).

Expected: FR-002, FR-003, FR-005, FR-015; SC-001, SC-006.

## 4. Validate US2 — two simultaneous visitors, shared workspace

1. Open the same FQDN in two separate browsers/sessions.
2. Start/observe a run; **Pass:** both sessions stay usable; live updates arrive without crash,
   freeze, or corrupted state in either.
3. Both interact at once; **Pass:** the shared environment stays stable (single replica, shared
   `"demo"` workspace, last-writer-wins on config).

Expected: FR-010, FR-011, FR-018; SC-002.

## 5. Validate US3 — repoint a connector at runtime

1. In the running app, open connector settings and repoint one connector (e.g. ServiceNow) to a
   different target/credentials.
2. Run an action that uses it; **Pass:** it hits the new target, without redeploy.
3. **Pass:** untouched connectors still use their seeded defaults; a health check on the repointed
   connector reflects the new target.
4. Repoint a connector to an **unreachable** target and run its health/test check. **Pass:** it fails
   gracefully with a clear message and does not crash the shared environment for the other visitor
   (FR-015(b)).

Expected: FR-007, FR-008, FR-015; SC-003.

## 6. Validate US4 — idle to zero, wake on demand

1. Leave the URL untouched past the ACA platform inactivity window.
2. **Pass:** `az containerapp replica list` shows **0** replicas (no running compute).
3. Reopen the URL; **Pass:** it wakes automatically and becomes usable after a short startup, never
   showing a broken page.

Expected: FR-012, FR-013; SC-004.

## 7. Validate US5 — vault secrets delivered safely; state resets on cold start

1. Audit: search the repo, deploy logs, and UI responses for any back-office secret value.
2. **Pass:** none found in plaintext; connectors nonetheless work (proves vault delivery); the UI
   masks stored secrets and cannot read seeded secrets back.
3. After an idle scale-down, reopen the URL; **Pass:** run history is cleared, workflows revert to the
   seeded set, the previously entered LLM key is gone, and any repointed connector reverted to the
   seeded default (fresh demo), and the app is usable — not broken.

Expected: FR-006, FR-009, FR-016; SC-005, SC-007.

## Evidence to capture (Article X)

- `dotnet test` (DemoConnectorSeeder unit + integration) green.
- Local `docker run` smoke output (connectors configured, LLM prompt, no secret in logs).
- The deployed FQDN; `az containerapp show`/`secret list`/`replica list` excerpts proving scale-to-zero
  and masked secrets.
- A short capture of two concurrent sessions watching one run, and an idle→wake cycle.
