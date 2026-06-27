# Loads the demo's back-office connector secrets into the current deploy shell (process environment)
# without ever printing a value. Zero-knowledge (Constitution Article IX): values originate from the
# Forge Vault and live only in this shell's environment for the duration of the deploy.
#
# Two supported sources, in priority order:
#   1. Already-present environment (e.g. the agent ran `vault_inject` and sourced the returned script,
#      so `$env:ConnectorSeed__*` are already set) — nothing to do.
#   2. A gitignored `deploy/aca/team.env` (populated from the vault) — its KEY=VALUE lines are loaded.
#
# The LLM key is never handled here — visitors supply their own (FR-004).

[CmdletBinding()]
param(
    # Path to the gitignored env file holding vault-sourced values (names mirror team.env.example).
    [string] $TeamEnvPath = (Join-Path $PSScriptRoot 'team.env')
)

$ErrorActionPreference = 'Stop'

# The non-secret + secret variable names the seeder consumes. Required (the demo will not seed a
# connector without these); optional ones may be left blank.
$requiredNames = @(
    'ConnectorSeed__ServiceNow__InstanceUrl',
    'ConnectorSeed__ServiceNow__Username',
    'ConnectorSeed__ServiceNow__Password',
    'ConnectorSeed__AzureDevOps__OrganizationUrl',
    'ConnectorSeed__AzureDevOps__ProjectName',
    'ConnectorSeed__AzureDevOps__PersonalAccessToken',
    'ConnectorSeed__Messaging__Platform'
)

# Load from team.env when present. Each KEY=VALUE line becomes a process env var. Values are never
# echoed — only the variable name is ever logged.
if (Test-Path $TeamEnvPath) {
    Write-Host "Loading seed values from $TeamEnvPath (values hidden)..."
    foreach ($line in Get-Content -LiteralPath $TeamEnvPath) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $separator = $trimmed.IndexOf('=')
        if ($separator -lt 1) { continue }
        $name  = $trimmed.Substring(0, $separator).Trim()
        $value = $trimmed.Substring($separator + 1).Trim()
        Set-Item -Path "Env:$name" -Value $value
        Write-Host "  set $name"
    }
} else {
    Write-Host "No team.env found; relying on ConnectorSeed__* values already present in the environment."
}

# Validate required names are present (without revealing values). Warn but do not fail — a connector
# missing its required values is simply left unconfigured by the seeder.
$missing = $requiredNames | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) }
if ($missing.Count -gt 0) {
    Write-Warning "These seed values are not set; their connector will be left unconfigured:"
    $missing | ForEach-Object { Write-Warning "  $_" }
} else {
    Write-Host "All required back-office seed values are present."
}

# Confirm the LLM key is NOT present in the deploy environment (it must never be seeded).
if (-not [string]::IsNullOrWhiteSpace($env:Anthropic__ApiKey)) {
    Write-Warning "Anthropic__ApiKey is set in the deploy shell — the LLM key must NOT be deployed (FR-004). Unset it before deploying."
}
