param(
    [ValidateRange(0, 600)]
    [double] $MinimumLoadSeconds = 15,

    [ValidateRange(1, 10000)]
    [int] $StableFrames = 120,

    [ValidateRange(0, 60)]
    [double] $WarmupSeconds = 2,

    [ValidateRange(1, 600)]
    [double] $CaptureSeconds = 10,

    [ValidateRange(10, 3600)]
    [int] $BenchmarkTimeoutSeconds = 600,

    [ValidateRange(20, 3660)]
    [int] $ProcessTimeoutSeconds = 660,

    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'WTEditor.Avalonia\WTEditor.Avalonia.csproj'
$executablePath = Join-Path $repositoryRoot 'WTEditor.Avalonia\bin\Release\net10.0\WTEditor.Avalonia.exe'
$runtimeDirectory = Join-Path $env:LOCALAPPDATA 'WTEditor\BenchmarkRuntime'
[IO.Directory]::CreateDirectory($runtimeDirectory) | Out-Null

if (-not $NoBuild -or -not (Test-Path -LiteralPath $executablePath)) {
    & dotnet build $projectPath -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE."
    }
}

$environmentValues = @{
    WTEDITOR_BENCHMARK = '1'
    WTEDITOR_BENCHMARK_MINIMUM_LOAD_SECONDS = $MinimumLoadSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    WTEDITOR_BENCHMARK_STABLE_FRAMES = $StableFrames.ToString([Globalization.CultureInfo]::InvariantCulture)
    WTEDITOR_BENCHMARK_WARMUP_SECONDS = $WarmupSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    WTEDITOR_BENCHMARK_CAPTURE_SECONDS = $CaptureSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    WTEDITOR_BENCHMARK_TIMEOUT_SECONDS = $BenchmarkTimeoutSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
}
$savedEnvironment = @{}
$runId = [Guid]::NewGuid().ToString('N')
$stdoutPath = Join-Path ([IO.Path]::GetTempPath()) "wteditor-benchmark-$runId.log"
$stderrPath = Join-Path ([IO.Path]::GetTempPath()) "wteditor-benchmark-$runId.err.log"
$process = $null

try {
    foreach ($entry in $environmentValues.GetEnumerator()) {
        $existing = [Environment]::GetEnvironmentVariable($entry.Key, 'Process')
        $savedEnvironment[$entry.Key] = $existing
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }

    $process = Start-Process `
        -FilePath $executablePath `
        -WorkingDirectory $runtimeDirectory `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -PassThru

    if (-not $process.WaitForExit($ProcessTimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force
        throw "Benchmark process exceeded the $ProcessTimeoutSeconds second process timeout. Logs: $stdoutPath and $stderrPath"
    }
    $process.WaitForExit()

    # Redirected output can finish flushing just after the process handle signals.
    # Poll the small marker file briefly instead of racing that asynchronous flush.
    $resultLine = $null
    $markerDeadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        if (Test-Path -LiteralPath $stdoutPath) {
            $resultLine = Get-Content -LiteralPath $stdoutPath |
                Where-Object { $_ -like 'WTEDITOR_BENCHMARK_RESULT=*' } |
                Select-Object -Last 1
        }
        if (-not $resultLine) {
            Start-Sleep -Milliseconds 100
        }
    } while (-not $resultLine -and [DateTime]::UtcNow -lt $markerDeadline)

    if (-not $resultLine) {
        $stderr = if (Test-Path -LiteralPath $stderrPath) {
            [string](Get-Content -LiteralPath $stderrPath -Raw)
        } else {
            ''
        }
        throw "Benchmark failed with exit code $($process.ExitCode). $($stderr.Trim()) Logs: $stdoutPath and $stderrPath"
    }

    $resultPath = $resultLine.Substring('WTEDITOR_BENCHMARK_RESULT='.Length)
    if (-not (Test-Path -LiteralPath $resultPath)) {
        throw "Benchmark reported a capture that does not exist: $resultPath"
    }

    Write-Output $resultPath
    Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
}
finally {
    foreach ($entry in $savedEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
}
