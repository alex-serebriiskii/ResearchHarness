param(
    [switch]$NoBuild,
    [string]$Configuration = "Debug"
)

$ProjectRoot  = Split-Path $PSScriptRoot -Parent
$TestProject  = "$ProjectRoot\tests\ResearchHarness.Tests.Unit\ResearchHarness.Tests.Unit.csproj"

if (-not $NoBuild) {
    Write-Host "Building test project..."
    & dotnet build $TestProject -c $Configuration --nologo -v quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed"
        exit 1
    }
}

Write-Host "Running tests..."
& dotnet test --project $TestProject
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host "Tests passed"
} else {
    Write-Host "Tests failed"
}

exit $exitCode
