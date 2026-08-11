$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testProject = Join-Path $repoRoot 'WTEditor.Avalonia.Tests\WTEditor.Avalonia.Tests.csproj'
$editorProject = Join-Path $repoRoot 'WTEditor.Avalonia\WTEditor.Avalonia.csproj'

Write-Host 'Restoring smoke-test dependencies...'
dotnet restore $testProject
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Building WTEditor.Avalonia...'
dotnet build $editorProject --no-restore -p:BaseOutputPath=bin\SmokeBuild\
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Running WTEditor.Avalonia smoke tests...'
dotnet test $testProject --no-restore --verbosity normal -p:BaseOutputPath=bin\SmokeTests\
exit $LASTEXITCODE
