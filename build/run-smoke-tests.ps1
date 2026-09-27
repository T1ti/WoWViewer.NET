param(
    [ValidateSet('All', 'Render', 'DX11', 'Avalonia')]
    [string]$Scope = 'All'
)

$ErrorActionPreference = 'Stop'

# Smoke runs are short-lived and must not leave Avalonia's background telemetry
# collector attached to the invoking terminal after the tests have exited.
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$smokeSolution = Join-Path $PSScriptRoot 'WTEditor.SmokeTests.slnx'
$testProjects = @{
    Render = Join-Path $repoRoot 'WoWRenderLib.Tests\WoWRenderLib.Tests.csproj'
    DX11 = Join-Path $repoRoot 'WoWRenderLib.DX11.Tests\WoWRenderLib.DX11.Tests.csproj'
    Avalonia = Join-Path $repoRoot 'WTEditor.Avalonia.Tests\WTEditor.Avalonia.Tests.csproj'
}
$target = if ($Scope -eq 'All') { $smokeSolution } else { $testProjects[$Scope] }

Write-Host "Restoring $Scope smoke-test dependencies..."
dotnet restore $target --disable-build-servers --verbosity quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Running $Scope smoke tests..."
# Build the editor and selected tests while keeping routine output concise.
dotnet test $target --no-restore --disable-build-servers --verbosity quiet --logger 'console;verbosity=minimal' -p:BaseOutputPath=bin\SmokeTests\
exit $LASTEXITCODE
