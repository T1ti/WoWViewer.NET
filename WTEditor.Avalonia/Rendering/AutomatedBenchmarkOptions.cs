using System;
using WTEditor.Application.Models;

namespace WTEditor.Avalonia.Rendering;

public sealed record AutomatedBenchmarkOptions(
    bool Enabled,
    TimeSpan MinimumLoadDuration,
    int StableFrameCount,
    TimeSpan WarmupDuration,
    TimeSpan CaptureDuration,
    TimeSpan Timeout)
{
    public static AutomatedBenchmarkOptions Current { get; } = FromEnvironment();

    public static AutomatedBenchmarkOptions FromEnvironment(
        Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        return new AutomatedBenchmarkOptions(
            ReadBoolean(getEnvironmentVariable, "WTEDITOR_BENCHMARK"),
            TimeSpan.FromSeconds(ReadDouble(
                getEnvironmentVariable,
                "WTEDITOR_BENCHMARK_MINIMUM_LOAD_SECONDS",
                defaultValue: 15,
                minimum: 0,
                maximum: 600)),
            ReadInteger(
                getEnvironmentVariable,
                "WTEDITOR_BENCHMARK_STABLE_FRAMES",
                defaultValue: 120,
                minimum: 1,
                maximum: 10_000),
            TimeSpan.FromSeconds(ReadDouble(
                getEnvironmentVariable,
                "WTEDITOR_BENCHMARK_WARMUP_SECONDS",
                defaultValue: 2,
                minimum: 0,
                maximum: 60)),
            TimeSpan.FromSeconds(ReadDouble(
                getEnvironmentVariable,
                "WTEDITOR_BENCHMARK_CAPTURE_SECONDS",
                defaultValue: 10,
                minimum: 1,
                maximum: 600)),
            TimeSpan.FromSeconds(ReadDouble(
                getEnvironmentVariable,
                "WTEDITOR_BENCHMARK_TIMEOUT_SECONDS",
                defaultValue: 600,
                minimum: 10,
                maximum: 3_600)));
    }

    private static bool ReadBoolean(Func<string, string?> read, string name)
    {
        var value = read(name);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadInteger(
        Func<string, string?> read,
        string name,
        int defaultValue,
        int minimum,
        int maximum) =>
        int.TryParse(read(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : defaultValue;

    private static double ReadDouble(
        Func<string, string?> read,
        string name,
        double defaultValue,
        double minimum,
        double maximum) =>
        double.TryParse(
            read(name),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? Math.Clamp(value, minimum, maximum)
            : defaultValue;
}

public enum AutomatedBenchmarkAction
{
    None,
    StartCapture,
    Timeout
}

/// <summary>
/// Detects a steady-state world without coupling the benchmark policy to the renderer.
/// A workload must be non-empty, resource processing must be idle, and all workload
/// counters must remain identical for a consecutive frame window.
/// </summary>
public sealed class AutomatedBenchmarkCoordinator
{
    private readonly AutomatedBenchmarkOptions _options;
    private DateTimeOffset? _startedAt;
    private WorkloadSignature? _lastSignature;
    private bool _finished;

    public AutomatedBenchmarkCoordinator(AutomatedBenchmarkOptions options)
    {
        _options = options;
    }

    public int StableFrames { get; private set; }
    public TimeSpan Elapsed { get; private set; }

    public AutomatedBenchmarkAction Observe(FrameProfileSnapshot snapshot)
    {
        if (_finished || !_options.Enabled)
            return AutomatedBenchmarkAction.None;

        _startedAt ??= snapshot.CapturedAt;
        Elapsed = snapshot.CapturedAt - _startedAt.Value;
        if (Elapsed >= _options.Timeout)
        {
            _finished = true;
            return AutomatedBenchmarkAction.Timeout;
        }

        var signature = WorkloadSignature.From(snapshot);
        var hasWorldWorkload =
            snapshot.DrawCalls > 0 &&
            snapshot.Culling.CandidateTerrainChunks +
            snapshot.Culling.CandidateWorldModels +
            snapshot.Culling.CandidateDoodads > 0;
        var isIdle = snapshot.PendingAssetOperations == 0 && snapshot.UploadedResources == 0;
        var loadDelayElapsed = Elapsed >= _options.MinimumLoadDuration;

        if (!hasWorldWorkload || !isIdle || !loadDelayElapsed)
        {
            StableFrames = 0;
            _lastSignature = signature;
            return AutomatedBenchmarkAction.None;
        }

        StableFrames = _lastSignature == signature ? StableFrames + 1 : 1;
        _lastSignature = signature;
        if (StableFrames < _options.StableFrameCount)
            return AutomatedBenchmarkAction.None;

        _finished = true;
        return AutomatedBenchmarkAction.StartCapture;
    }

    private readonly record struct WorkloadSignature(
        int DrawCalls,
        long SubmittedIndices,
        int CandidateTerrainChunks,
        int CandidateWorldModels,
        int CandidateDoodads,
        int VisibleTerrainChunks,
        int VisibleWorldModels,
        int VisibleDoodads)
    {
        public static WorkloadSignature From(FrameProfileSnapshot snapshot) => new(
            snapshot.DrawCalls,
            snapshot.SubmittedIndices,
            snapshot.Culling.CandidateTerrainChunks,
            snapshot.Culling.CandidateWorldModels,
            snapshot.Culling.CandidateDoodads,
            snapshot.Culling.VisibleTerrainChunks,
            snapshot.Culling.VisibleWorldModels,
            snapshot.Culling.VisibleDoodads);
    }
}
