param(
    [Parameter(Mandatory=$true)]
    [string]$Theme,
    [int]$Port = 5000
)

$url  = "http://localhost:$Port/internal/research/start"
$body = '{"theme": "' + $Theme.Replace('"', '\"') + '"}'

try {
    $response = Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json" -ErrorAction Stop
    # Response is a quoted job ID string; strip surrounding quotes if present
    $jobId = "$response".Trim('"')
    Write-Host $jobId
    exit 0
} catch {
    Write-Host "Error: $_"
    exit 1
}
