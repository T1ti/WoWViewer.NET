param(
    [Parameter(Mandatory = $true)]
    [string]$ShaderRoot,
    [string]$OutputDirectory = '',
    [ValidateSet('SM3', 'AllDx9')]
    [string]$Profile = 'SM3',
    [switch]$Force,
    [switch]$VerifyHashes
)

$ErrorActionPreference = 'Stop'
$taskProject = Join-Path $PSScriptRoot 'ClientShaderTools\ClientShaderTools.csproj'
$taskOutputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $PSScriptRoot '..\artifacts\client-335-shaders'
} else { $OutputDirectory }
$taskArguments = @('--shader-root', $ShaderRoot, '--output', $taskOutputDirectory, '--profile', $Profile)
if ($Force) { $taskArguments += '--force' }
if ($VerifyHashes) { $taskArguments += '--verify-hashes' }

dotnet run --project $taskProject --configuration Release --no-launch-profile --verbosity quiet -- @taskArguments
exit $LASTEXITCODE
