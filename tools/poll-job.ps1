param(
    [Parameter(Mandatory=$true)]
    [string]$JobId,
    [int]$Port = 5000,
    [int]$IntervalSeconds = 10,
    [int]$TimeoutSeconds = 1800
)

$url       = "http://localhost:$Port/internal/research/$JobId/status"
$startTime = Get-Date
$finalStatus = $null

while ($true) {
    $elapsed = [int]((Get-Date) - $startTime).TotalSeconds

    try {
        $status = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
        $status = "$status".Trim('"')
    } catch {
        Write-Host "Error polling status: $_"
        exit 1
    }

    $timestamp = (Get-Date).ToString("HH:mm:ss")
    Write-Host "$timestamp  status=$status  elapsed=${elapsed}s"

    if ($status -eq "Completed" -or $status -eq "Failed") {
        $finalStatus = $status
        break
    }

    if ($elapsed -ge $TimeoutSeconds) {
        Write-Host "Timeout after ${elapsed}s"
        exit 1
    }

    Start-Sleep -Seconds $IntervalSeconds
}

$totalElapsed = [int]((Get-Date) - $startTime).TotalSeconds
Write-Host "Final status: $finalStatus  total elapsed: ${totalElapsed}s"

if ($finalStatus -eq "Completed") {
    exit 0
} else {
    exit 1
}
