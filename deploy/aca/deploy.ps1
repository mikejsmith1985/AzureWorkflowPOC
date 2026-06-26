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
    [string] $ResourceGroup = 'dbaiazure-poc-rg',
    [string] $Location      = 'eastus',
    [string] $AcrName       = 'dbaiazurepocacr',          # must be globally unique, lowercase alnum
    [string] $EnvironmentName = 'dbaiazure-poc-env',
    [string] $AppName       = 'dbaiazure-poc',
    [string] $ImageRepo     = 'dbai-azure-demo',
    [string] $ImageTag      = 'latest',
    [string] $Cpu           = '0.25',
    [string] $Memory        = '0.5Gi',
    [int]    $TargetPort    = 8080
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

# ── 0. Sanity checks ────────────────────────────────────────────────────────────
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI ('az') not found. Install it and run 'az login' before deploying."
}
az account show --output none 2>$null
if ($LASTEXITCODE -ne 0) { throw "Not logged in. Run 'az login' (try '! az login' in this session)." }

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

# ── 2. Resource group + ACR (admin-enabled so ACA can pull) ──────────────────────
Write-Host "Ensuring resource group '$ResourceGroup' in '$Location'..."
az group create --name $ResourceGroup --location $Location --output none

Write-Host "Ensuring container registry '$AcrName'..."
az acr create --resource-group $ResourceGroup --name $AcrName --sku Basic --admin-enabled true --output none 2>$null

# ── 3. Build the image in ACR (no local Docker required) ─────────────────────────
$image = "$AcrName.azurecr.io/${ImageRepo}:${ImageTag}"
Write-Host "Building image '$image' from $repoRoot ..."
az acr build --registry $AcrName --image "${ImageRepo}:${ImageTag}" --file (Join-Path $repoRoot 'Dockerfile') $repoRoot --output none

$acrServer   = "$AcrName.azurecr.io"
$acrUser     = az acr credential show --name $AcrName --query username --output tsv
$acrPassword = az acr credential show --name $AcrName --query 'passwords[0].value' --output tsv

# ── 4. Container Apps environment ────────────────────────────────────────────────
Write-Host "Ensuring Container Apps environment '$EnvironmentName'..."
az containerapp env create --name $EnvironmentName --resource-group $ResourceGroup --location $Location --output none 2>$null

# ── 5. Create or update the app (public, scale-to-zero, single replica) ──────────
$exists = (az containerapp show --name $AppName --resource-group $ResourceGroup --output none 2>$null; $LASTEXITCODE -eq 0)
if (-not $exists) {
    Write-Host "Creating container app '$AppName' (public, min=0, max=1)..."
    az containerapp create `
        --name $AppName --resource-group $ResourceGroup --environment $EnvironmentName `
        --image $image --registry-server $acrServer --registry-username $acrUser --registry-password $acrPassword `
        --target-port $TargetPort --ingress external `
        --min-replicas 0 --max-replicas 1 --cpu $Cpu --memory $Memory `
        --secrets $secretArgs --env-vars $envArgs `
        --output none
} else {
    Write-Host "Updating container app '$AppName' image + seed config..."
    if ($secretArgs.Count -gt 0) {
        az containerapp secret set --name $AppName --resource-group $ResourceGroup --secrets $secretArgs --output none
    }
    az containerapp update --name $AppName --resource-group $ResourceGroup --image $image --set-env-vars $envArgs --output none
}

# ── 6. Re-assert scale-to-zero (idempotent guard, as the reference does) ─────────
az containerapp update --name $AppName --resource-group $ResourceGroup --min-replicas 0 --max-replicas 1 --output none

# ── 7. Feed the public FQDN back to the app for correct deep links, then print it ─
$fqdn = az containerapp show --name $AppName --resource-group $ResourceGroup --query 'properties.configuration.ingress.fqdn' --output tsv
az containerapp update --name $AppName --resource-group $ResourceGroup --set-env-vars "Portal__BaseUrl=https://$fqdn" --output none

Write-Host ''
Write-Host '────────────────────────────────────────────────────────────'
Write-Host "Demo is live (scales to zero when idle, wakes on first request):"
Write-Host "  https://$fqdn"
Write-Host 'Share this URL. Visitors supply only their own LLM API key.'
Write-Host '────────────────────────────────────────────────────────────'
