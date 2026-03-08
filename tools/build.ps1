param(
    [string]$Configuration = "Debug",
    [switch]$StopApp
)

$ProjectRoot = Split-Path $PSScriptRoot -Parent

if ($StopApp) {
    Get-Process -Name "ResearchHarness.Web" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

Push-Location $ProjectRoot
try {
    & dotnet build -c $Configuration --nologo -v quiet
    $exitCode = $LASTEXITCODE
} finally {
    Pop-Location
}

if ($exitCode -eq 0) {
    Write-Host "Build succeeded ($Configuration)"
    exit 0
} else {
    Write-Host "Build failed ($Configuration)"
    exit 1
}
