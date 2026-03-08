$proc = Get-Process -Name "ResearchHarness.Web" -ErrorAction SilentlyContinue

if ($proc) {
    $procId = $proc.Id
    Stop-Process -InputObject $proc -Force
    Write-Host "Stopped (PID $procId)"
    exit 0
} else {
    Write-Host "Not running"
    exit 0
}
