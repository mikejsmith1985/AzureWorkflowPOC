# Start DBAIAzure.Web Blazor Server on http://localhost:5000
# Uses the user-profile SDK install which includes the full build toolchain.
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
$project = Join-Path $PSScriptRoot "..\src\DBAIAzure.Web"

Write-Host "Starting DBAIAzure.Web at http://localhost:5000" -ForegroundColor Cyan
& $dotnet run --project $project --urls "http://localhost:5000"
