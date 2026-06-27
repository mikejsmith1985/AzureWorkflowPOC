# Runbook: Deploy a scale-to-zero site to Azure Container Apps (mirroring DBAI)

> Self-contained handoff for an agent with no prior context. It creates a **second** Azure Container
> Apps (ACA) site in the **same** resource group and environment as DBAI, scale-to-zero (no idle cost),
> as a single combined container behind one external ingress.
>
> Shared values below are real and already exist — **reuse them, do not recreate**. Per-site values are
> marked `«like-this»`.

## 0. Context you're inheriting

You will create a **second** app **in the same resource group and the same Container Apps environment**
as DBAI (both are shared and cheap to reuse — do **not** create a new environment).

**Shared Azure facts (already exist — reuse, don't recreate):**

| Thing | Value |
|---|---|
| Subscription | `Azure subscription 1` — id `9477b553-e058-469a-a468-ea34f14276f3` |
| Tenant | `249fdeb4-7802-49bb-924a-f371fd271c85` |
| Resource group | `dbai-poc-rg` |
| Location | `eastus` (East US) |
| Container Apps environment | `dbai-poc-env` |
| Env default domain | `gentledune-76ebb71e.eastus.azurecontainerapps.io` (your URL becomes `https://«app-name».gentledune-76ebb71e.eastus.azurecontainerapps.io`) |

**Per-site values you choose:**

| Placeholder | Example |
|---|---|
| `«app-name»` | `azure-workflow-poc` (lowercase, hyphens) |
| `«image-repo»` | `mikejsmith993/«app-name»` (Docker Hub) — or an ACR repo, see §3 |
| `«source-dir»` | path to the repo you're building (WSL path, e.g. `/mnt/c/ProjectsWin/AzureWorkflowPOC`) |
| `«dockerfile»` | path to its Dockerfile, e.g. `deploy/aca/Dockerfile` |
| `«target-port»` | the port the container's web server listens on (DBAI = `3000`) |

## 1. Prerequisites (one-time per terminal session)

```bash
az login                       # interactive — ask the human to run this if headless
az account set --subscription 9477b553-e058-469a-a468-ea34f14276f3
az extension add --name containerapp --upgrade --yes
docker login                   # only if using Docker Hub (§3 option A)
```

Confirm `az account show` and `docker info` both succeed before proceeding.

## 2. ⚠️ The one rule that matters most — never deploy a static tag

DBAI silently served stale UI for a while because deploys ran
`az containerapp update --image <repo>:latest`. **That image string never changes, so ACA treats the
update as a no-op: no new revision, no image re-pull.** The push succeeds but the container stays frozen.

**Always deploy a UNIQUE, IMMUTABLE tag** (`<git-sha>-<utc-timestamp>`). The unique string forces a new
revision and a guaranteed fresh pull. This is non-negotiable.

## 3. Pick a registry

- **Option A — Docker Hub** (what DBAI currently uses; simplest). Image `«image-repo»:<tag>`. Needs
  `docker login`. Note: heavy unauthenticated pulls can hit Docker Hub 429 rate-limits → a new revision
  fails to pull and the *old* one keeps serving (looks green but is stale). If you hit this, switch to B.
- **Option B — Azure Container Registry** (more robust, no rate limits). The DBAI repo's
  `deploy/aca/deploy.sh` already does this against ACR `dbaipocacr`. Use `az acr login -n dbaipocacr` and
  image `dbaipocacr.azurecr.io/«app-name»:<tag>`. ACA pulls via the registry credentials set in §4.

For a quick mirror, Option A is fine. For anything long-lived, prefer B.

## 4. Create the app (first time only — scale-to-zero, secrets from env)

```bash
cd «source-dir»

# Build + push a UNIQUE first image
TAG="$(git rev-parse --short HEAD)-$(date -u +%Y%m%d%H%M%S)"
IMAGE="«image-repo»:${TAG}"
docker build -f «dockerfile» -t "$IMAGE" .
docker push "$IMAGE"      # (Option B: az acr login -n dbaipocacr first)

# Create the Container App in the SHARED environment, scale-to-zero
az containerapp create \
  --name «app-name» \
  --resource-group dbai-poc-rg \
  --environment dbai-poc-env \
  --image "$IMAGE" \
  --target-port «target-port» \
  --ingress external \
  --cpu 1.0 --memory 2.0Gi \
  --min-replicas 0 --max-replicas 1 \
  --output table
```

**Secrets/config:** DBAI injects shared infra creds (ServiceNow/Jira/Discord, etc.) from a `team.env`
file, translated into ACA secrets + env-var references at create time. If your app needs the same, add
them as secrets — never bake secrets into the image, and never paste secret values into the conversation
(they live in the Forge Vault):

```bash
# pattern: each KEY=VALUE in team.env becomes a lowercased-hyphen secret + an env ref
az containerapp secret set  -n «app-name» -g dbai-poc-rg --secrets my-secret=secretref-value
az containerapp update      -n «app-name» -g dbai-poc-rg --set-env-vars MY_SECRET=secretref:my-secret
```

(For Forge Vault secrets, use `vault_inject` — the values must never enter a file or log.)

**For ACR (Option B)** also wire registry auth so ACA can pull:

```bash
az containerapp registry set -n «app-name» -g dbai-poc-rg \
  --server dbaipocacr.azurecr.io \
  --username "$(az acr credential show -n dbaipocacr --query username -o tsv)" \
  --password "$(az acr credential show -n dbaipocacr --query 'passwords[0].value' -o tsv)"
```

## 5. Redeploy script (every subsequent deploy)

Drop this in the new repo as `scripts/redeploy.sh`, then deploys are one command. It's the DBAI script
generalized — unique tag + waits for the new revision to take traffic before verifying:

```bash
#!/usr/bin/env bash
set -euo pipefail
cd "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

APP_NAME="${APP_NAME:-«app-name»}"
RESOURCE_GROUP="${RESOURCE_GROUP:-dbai-poc-rg}"
IMAGE_REPO="${IMAGE_REPO:-«image-repo»}"
DOCKERFILE="${DOCKERFILE:-«dockerfile»}"

docker info  >/dev/null 2>&1 || { echo "ERROR: run: docker login"; exit 1; }
az account show >/dev/null 2>&1 || { echo "ERROR: run: az login"; exit 1; }

TAG="$(git rev-parse --short HEAD 2>/dev/null || echo nogit)-$(date -u +%Y%m%d%H%M%S)"
IMAGE="${IMAGE_REPO}:${TAG}"

echo "==> Building + pushing ${IMAGE}"
docker build -f "$DOCKERFILE" -t "$IMAGE" .
docker push "$IMAGE"

echo "==> Updating ${APP_NAME} (unique tag -> new revision -> fresh pull)"
az containerapp update -n "$APP_NAME" -g "$RESOURCE_GROUP" --image "$IMAGE" --output table

FQDN="$(az containerapp show -n "$APP_NAME" -g "$RESOURCE_GROUP" \
  --query properties.configuration.ingress.fqdn -o tsv)"
echo "==> Waiting for the new revision to take 100% traffic (cold start ~10-30s)"
for _ in $(seq 1 30); do
  read -r REV W H < <(az containerapp revision list -n "$APP_NAME" -g "$RESOURCE_GROUP" \
    --query "sort_by([?properties.trafficWeight>\`0\`],&properties.createdTime)[-1].[name,properties.trafficWeight,properties.healthState]" -o tsv)
  [ "${W:-0}" = "100" ] && [ "${H:-}" = "Healthy" ] && break; sleep 3
done
echo "    serving ${REV} traffic=${W}% health=${H}"
curl -s -o /dev/null -w "    HTTP %{http_code}\n" "https://${FQDN}/"
echo "Live: https://${FQDN}"
```

Static command card (matches the DBAI pattern — the card never changes, you update the script):

```
bash -c "cd «source-dir» && bash scripts/redeploy.sh"
```

## 6. Verify it's actually live (don't trust "provisioningState: Succeeded")

```bash
az containerapp revision list -n «app-name» -g dbai-poc-rg -o table
# the NEWEST revision must show TrafficWeight 100 + HealthState Healthy
curl -s https://«app-name».gentledune-76ebb71e.eastus.azurecontainerapps.io/ | head
```

**Prove it with a NEW endpoint/asset, not just a 200** — a stale revision also returns 200. For a web UI,
confirm the content-hashed bundle name (`index-<hash>.js`) **changes** after a deploy; if it doesn't, the
build was cached or the image didn't actually update.

## 7. Gotchas (learned the hard way on DBAI)

1. **`:latest` no-op** — covered in §2. The single biggest trap.
2. **Build cache hides "no change"** — if your build step shows `CACHED` and the bundle hash doesn't
   change, your source genuinely didn't change on the branch you built. The change you want may be on an
   **unmerged branch** — the deploy builds whatever branch is checked out.
3. **Docker Hub 429** — see §3; symptom is a new revision in `ImagePullFailure` while the old one serves.
   Fix: `az containerapp revision restart`, or switch to ACR.
4. **Cold start** — first hit after idle takes ~10–30s; that's scale-to-zero working, not an error.
5. **Tear down** when done so it costs nothing: `az containerapp delete -n «app-name» -g dbai-poc-rg --yes`
   (leave the shared environment/RG alone).

---

_Source pattern: DBAI `scripts/redeploy.sh` (Docker Hub, unique-tag) and `deploy/aca/deploy.sh` (ACR).
DBAI live app: `dbai-poc` in `dbai-poc-rg` / env `dbai-poc-env`._
