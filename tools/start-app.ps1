param(
    [int]$Port = 5000,
    [switch]$NoBuild,
    [string]$LogFile = "",
    [string]$ErrFile = ""
)

$ProjectRoot = Split-Path $PSScriptRoot -Parent

if (-not $LogFile) { $LogFile = "$ProjectRoot\run.log" }
if (-not $ErrFile) { $ErrFile = "$ProjectRoot\run.err" }

# Stop existing process
Get-Process -Name "ResearchHarness.Web" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# Remove old logs
if (Test-Path $LogFile) { Remove-Item $LogFile -Force }
if (Test-Path $ErrFile) { Remove-Item $ErrFile -Force }

# Build
if (-not $NoBuild) {
    Write-Host "Building..."
    $buildResult = & dotnet build -c Debug --nologo -v quiet 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed:"
        $buildResult | Write-Host
        exit 1
    }
}

# Start app
$startArgs = @{
    FilePath         = "dotnet"
    ArgumentList     = "run --project src/ResearchHarness.Web/ResearchHarness.Web.csproj --no-build"
    WorkingDirectory = $ProjectRoot
    RedirectStandardOutput = $LogFile
    RedirectStandardError  = $ErrFile
    WindowStyle      = "Hidden"
    PassThru         = $true
}
$proc = Start-Process @startArgs

# Poll for readiness
$deadline = (Get-Date).AddSeconds(30)
$ready = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 1
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:$Port/" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
        $ready = $true
        break
    } catch [System.Net.WebException] {
        # If we got an HTTP response (even 404/405), the app is listening
        if ($_.Exception.Response -ne $null) {
            $ready = $true
            break
        }
        # Otherwise connection refused / timeout — keep waiting
    } catch {
        # Any other error — keep waiting
    }
}

if ($ready) {
    Write-Host "App started at http://localhost:$Port"
    exit 0
} else {
    Write-Host "Timeout waiting for app to start. Check $ErrFile for details."
    exit 1
}
