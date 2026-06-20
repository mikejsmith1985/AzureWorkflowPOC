# Runs the full Playwright E2E test suite against a freshly built web app.
# Usage: .\scripts\run-e2e.ps1
#   The script builds the solution, installs Playwright browsers if needed,
#   and then runs dotnet test on DBAIAzure.E2ETests only.
#   The WebAppFixture inside the test project starts and stops the app automatically.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent

Write-Host "=== E2E: building solution ===" -ForegroundColor Cyan
dotnet build "$repoRoot\DBAIAzure.sln" --configuration Debug --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed — fix errors before running E2E tests." }

Write-Host "`n=== E2E: installing Playwright browsers ===" -ForegroundColor Cyan
$e2eProject = "$repoRoot\tests\DBAIAzure.E2ETests"
# The playwright install command is run via the test project's bin output.
$playwrightScript = Get-ChildItem "$e2eProject\bin" -Filter "playwright.ps1" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if ($playwrightScript) {
    & pwsh $playwrightScript.FullName install chromium
} else {
    Write-Host "  playwright.ps1 not found in bin — browsers may already be installed." -ForegroundColor Yellow
}

Write-Host "`n=== E2E: running tests ===" -ForegroundColor Cyan
dotnet test "$e2eProject" --no-build --logger "console;verbosity=normal" --configuration Debug
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host "`nAll E2E tests passed." -ForegroundColor Green
} else {
    Write-Host "`nE2E tests FAILED (exit $exitCode)." -ForegroundColor Red
}

exit $exitCode
