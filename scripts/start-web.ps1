# Launches DBAIAzure.Web in a detached process, polls for readiness, then opens the browser.
# Returns immediately after the browser opens - does NOT block the terminal.
param(
    [int]$Port = 5000,
    [int]$TimeoutSeconds = 90
)

$dotnetExe  = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
$projectDir = Resolve-Path (Join-Path $PSScriptRoot "..\src\DBAIAzure.Web")
$logFile    = "$env:TEMP\DBAIAzure.Web.log"

# Check if already running
$alreadyListening = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($alreadyListening) {
    Write-Host "DBAIAzure.Web is already running on port $Port." -ForegroundColor Yellow
    Start-Process "http://localhost:$Port"
    exit 0
}

Write-Host "Starting DBAIAzure.Web on http://localhost:$Port ..." -ForegroundColor Cyan
Write-Host "Log: $logFile"

# Ensure the child process loads appsettings.Development.json
$env:ASPNETCORE_ENVIRONMENT = "Development"

# Launch dotnet run, redirecting output to a log file so failures are visible
$proc = Start-Process -FilePath $dotnetExe `
    -ArgumentList "run --project `"$projectDir`" --urls http://localhost:$Port" `
    -RedirectStandardOutput $logFile `
    -RedirectStandardError  "$env:TEMP\DBAIAzure.Web.err.log" `
    -NoNewWindow -PassThru

# Poll until the port is listening (or we time out)
$waited = 0
$intervalSeconds = 2
while ($waited -lt $TimeoutSeconds) {
    Start-Sleep $intervalSeconds
    $waited += $intervalSeconds

    # Show last log line so the user can see build progress
    $lastLine = Get-Content $logFile -Tail 1 -ErrorAction SilentlyContinue
    if ($lastLine) { Write-Host "  [$($waited)s] $lastLine" -ForegroundColor Gray }

    # Check if the process already exited (crash)
    if ($proc.HasExited) {
        Write-Host "dotnet run exited with code $($proc.ExitCode)." -ForegroundColor Red
        Write-Host "---- stdout ----"
        Get-Content $logFile -ErrorAction SilentlyContinue | Select-Object -Last 20 | ForEach-Object { Write-Host $_ }
        Write-Host "---- stderr ----"
        Get-Content "$env:TEMP\DBAIAzure.Web.err.log" -ErrorAction SilentlyContinue | Select-Object -Last 20 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        exit 1
    }

    $conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if ($conn) {
        Write-Host "Ready after $($waited)s - opening http://localhost:$Port" -ForegroundColor Green
        Start-Process "http://localhost:$Port"
        exit 0
    }
}

Write-Host "Timed out after $($TimeoutSeconds)s." -ForegroundColor Red
Write-Host "Last log output:"
Get-Content $logFile -Tail 10 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
