# Stop any dotnet process running DBAIAzure.Web on port 5000
$port = 5000
$connections = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
if ($connections) {
    $pids = $connections | Select-Object -ExpandProperty OwningProcess -Unique
    foreach ($processId in $pids) {
        $proc = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($proc) {
            Write-Host "Stopping $($proc.ProcessName) (PID $processId) on port $port" -ForegroundColor Yellow
            Stop-Process -Id $processId -Force
        }
    }
    Write-Host "DBAIAzure.Web stopped." -ForegroundColor Green
} else {
    Write-Host "No process found listening on port $port." -ForegroundColor Gray
}
