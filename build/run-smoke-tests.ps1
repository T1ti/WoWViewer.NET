$ErrorActionPreference = 'Stop'

# Smoke runs are short-lived and must not leave Avalonia's background telemetry
# collector attached to the invoking terminal after the tests have exited.
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testProject = Join-Path $repoRoot 'WTEditor.Avalonia.Tests\WTEditor.Avalonia.Tests.csproj'
$editorProject = Join-Path $repoRoot 'WTEditor.Avalonia\WTEditor.Avalonia.csproj'

Write-Host 'Restoring smoke-test dependencies...'
dotnet restore $testProject --disable-build-servers
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Building WTEditor.Avalonia...'
dotnet build $editorProject --no-restore --disable-build-servers -p:BaseOutputPath=bin\SmokeBuild\
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Running WTEditor.Avalonia smoke tests...'
dotnet test $testProject --no-restore --disable-build-servers --verbosity normal -p:BaseOutputPath=bin\SmokeTests\
exit $LASTEXITCODE
