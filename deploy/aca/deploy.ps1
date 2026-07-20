# Builds the demo image into Azure Container Registry and creates/updates an Azure Container Apps app
# that is publicly reachable at one URL, scales to zero when idle, wakes on request, and carries the
# vault-sourced back-office secrets as masked ACA secrets. Mirrors the reference app's local `az`
# deploy model (no Bicep/ARM, no GitHub Actions — Constitution Article VIII). The LLM key is never
# deployed (FR-004); each visitor supplies their own in the app.
#
# Prerequisites: `az login` to the target subscription; run `./seed-secrets.ps1` first so the
# ConnectorSeed__* values are present in this shell. Run from anywhere — paths are script-relative.

[CmdletBinding()]
param(
    # The shared resource group + environment hosting the reference (DBAI) apps. This subscription
    # caps Container Apps environments at ONE per region, so the demo MUST reuse the existing
    # environment rather than create its own (see deploy/aca/README.md).
    [string] $ResourceGroup = 'dbai-poc-rg',
    [string] $Location      = 'eastus',
    [string] $AcrName       = 'dbaiazurepocacr',          # must be globally unique, lowercase alnum
    [string] $AcrResourceGroup = 'dbaiazure-poc-rg',      # ACR lives in its own RG; the name is globally unique so cross-RG pulls work
    [string] $EnvironmentName = 'dbai-poc-env',
    [string] $AppName       = 'dbaiazure-poc',
    [string] $ImageRepo     = 'dbai-azure-demo',
    [string] $ImageTag      = 'latest',
    [string] $Cpu           = '0.25',
    [string] $Memory        = '0.5Gi',
    [int]    $TargetPort    = 8080,
    # The DoR Validation Workflow (spec-021) runs a durable SLA/reply-poll BackgroundService, which cannot fire
    # while the app is scaled to zero — so the default is 1. Set to 0 only if the DoR workflow is not in use.
    [int]    $MinReplicas   = 1
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

# ── 0. Sanity checks ────────────────────────────────────────────────────────────
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI ('az') not found. Install it and run 'az login' before deploying."
}
az account show --output none 2>$null
if ($LASTEXITCODE -ne 0) { throw "Not logged in. Run 'az login' (try '! az login' in this session)." }

# This subscription disables server-side ACR Tasks builds, so the image is built with the local
# Docker engine and pushed to ACR. Confirm Docker is installed and its daemon is reachable.
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker not found. A local Docker build is required (ACR Tasks are disabled on this subscription)."
}
docker info --format '{{.ServerVersion}}' > $null 2>&1
if ($LASTEXITCODE -ne 0) { throw "Docker engine is not running. Start Docker Desktop and retry." }

# ── 1. Collect vault-sourced seed values → ACA secrets + secretref env vars ──────
# Every non-blank ConnectorSeed__* variable becomes a masked ACA secret and a secretref env var, so
# the running app reads it exactly as the DemoConnectorSeeder expects. Values are never printed.
$secretArgs = @()
$envArgs    = @()
foreach ($entry in Get-ChildItem Env: | Where-Object { $_.Name -like 'ConnectorSeed__*' }) {
    if ([string]::IsNullOrWhiteSpace($entry.Value)) { continue }
    $secretName = ($entry.Name.ToLowerInvariant() -replace '__', '-' -replace '[^a-z0-9-]', '-').Trim('-')
    $secretArgs += "$secretName=$($entry.Value)"
    $envArgs    += "$($entry.Name)=secretref:$secretName"
}
if ($secretArgs.Count -eq 0) {
    Write-Warning "No ConnectorSeed__* values found — the app will deploy with NO pre-seeded connectors. Did you run ./seed-secrets.ps1?"
}
# Guard: the LLM key must never be deployed.
if (Get-ChildItem Env: | Where-Object { $_.Name -eq 'Anthropic__ApiKey' -and -not [string]::IsNullOrWhiteSpace($_.Value) }) {
    throw "Anthropic__ApiKey is set in this shell — refusing to deploy the LLM key (FR-004). Unset it and retry."
}

# ── 2. Resource groups + ACR (admin-enabled so ACA can pull) ─────────────────────
Write-Host "Ensuring resource group '$ResourceGroup' (app/env) in '$Location'..."
az group create --name $ResourceGroup --location $Location --output none

Write-Host "Ensuring resource group '$AcrResourceGroup' (registry) in '$Location'..."
az group create --name $AcrResourceGroup --location $Location --output none

Write-Host "Ensuring container registry '$AcrName'..."
az acr create --resource-group $AcrResourceGroup --name $AcrName --sku Basic --admin-enabled true --output none 2>$null

# ── 3. Build the image locally and push to ACR ───────────────────────────────────
# Built with the local Docker engine (ACR Tasks are disabled on this subscription) and tagged with a
# unique, immutable tag (git short SHA + UTC timestamp). The unique tag is essential: ACA treats a
# repushed static ':latest' as a no-op and silently keeps serving the stale revision.
$gitSha = (git -C $repoRoot rev-parse --short HEAD 2>$null)
if (-not $gitSha) { $gitSha = 'nogit' }
$uniqueTag = "$gitSha-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"
$image = "$AcrName.azurecr.io/${ImageRepo}:${uniqueTag}"

Write-Host "Logging in to ACR '$AcrName'..."
az acr login --name $AcrName --output none
if ($LASTEXITCODE -ne 0) { throw "az acr login failed for '$AcrName' (is the registry reachable?)." }

Write-Host "Building image '$image' locally from $repoRoot ..."
docker build --file (Join-Path $repoRoot 'Dockerfile') --tag $image $repoRoot
if ($LASTEXITCODE -ne 0) { throw "docker build failed." }

Write-Host "Pushing image to ACR..."
docker push $image
if ($LASTEXITCODE -ne 0) { throw "docker push failed." }

$acrServer   = "$AcrName.azurecr.io"
$acrUser     = az acr credential show --name $AcrName --query username --output tsv
$acrPassword = az acr credential show --name $AcrName --query 'passwords[0].value' --output tsv

# ── 4. Container Apps environment (reuse if present) ─────────────────────────────
# The subscription allows only one environment per region, so reuse the shared one when it already
# exists. Only create when genuinely absent, and surface the real error if that create fails.
az containerapp env show --name $EnvironmentName --resource-group $ResourceGroup --output none 2>$null
$envExists = ($LASTEXITCODE -eq 0)
if ($envExists) {
    Write-Host "Reusing existing Container Apps environment '$EnvironmentName'."
} else {
    Write-Host "Creating Container Apps environment '$EnvironmentName'..."
    az containerapp env create --name $EnvironmentName --resource-group $ResourceGroup --location $Location --output none
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create environment '$EnvironmentName'. This subscription caps environments at one per region — point -EnvironmentName/-ResourceGroup at the existing shared environment instead."
    }
}

# ── 5. Create or update the app (public, scale-to-zero, single replica) ──────────
az containerapp show --name $AppName --resource-group $ResourceGroup --output none 2>$null
$exists = ($LASTEXITCODE -eq 0)
if (-not $exists) {
    Write-Host "Creating container app '$AppName' (public, min=0, max=1)..."
    az containerapp create `
        --name $AppName --resource-group $ResourceGroup --environment $EnvironmentName `
        --image $image --registry-server $acrServer --registry-username $acrUser --registry-password $acrPassword `
        --target-port $TargetPort --ingress external `
        --min-replicas $MinReplicas --max-replicas 1 --cpu $Cpu --memory $Memory `
        --secrets $secretArgs --env-vars $envArgs `
        --output none
    if ($LASTEXITCODE -ne 0) { throw "az containerapp create failed for '$AppName'." }
} else {
    Write-Host "Updating container app '$AppName' image + seed config..."
    if ($secretArgs.Count -gt 0) {
        az containerapp secret set --name $AppName --resource-group $ResourceGroup --secrets $secretArgs --output none
    }
    az containerapp update --name $AppName --resource-group $ResourceGroup --image $image --set-env-vars $envArgs --output none
}

# ── 6. Re-assert scale-to-zero (idempotent guard, as the reference does) ─────────
az containerapp update --name $AppName --resource-group $ResourceGroup --min-replicas $MinReplicas --max-replicas 1 --output none

# ── 7. Feed the public FQDN back to the app for correct deep links, then print it ─
$fqdn = az containerapp show --name $AppName --resource-group $ResourceGroup --query 'properties.configuration.ingress.fqdn' --output tsv
if ([string]::IsNullOrWhiteSpace($fqdn)) { throw "Could not resolve the app's public FQDN — the deploy did not complete successfully." }
az containerapp update --name $AppName --resource-group $ResourceGroup --set-env-vars "Portal__BaseUrl=https://$fqdn" --output none

Write-Host ''
Write-Host '────────────────────────────────────────────────────────────'
Write-Host "Demo is live (scales to zero when idle, wakes on first request):"
Write-Host "  https://$fqdn"
Write-Host 'Share this URL. Visitors supply only their own LLM API key.'
Write-Host '────────────────────────────────────────────────────────────'
