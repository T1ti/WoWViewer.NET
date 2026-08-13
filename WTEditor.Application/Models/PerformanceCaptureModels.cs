using System.Numerics;

namespace WTEditor.Application.Models;

public sealed record PerformanceDistribution(
    int SampleCount,
    double Mean,
    double Median,
    double P95,
    double P99,
    double Maximum);

public sealed record PerformanceCaptureContext(
    string Scenario,
    int ViewportWidth,
    int ViewportHeight,
    Vector3 CameraPosition,
    Vector3 CameraDirection,
    bool RenderTerrain,
    bool RenderWorldModels,
    bool RenderDoodads,
    bool DetailedGpuPassTiming,
    float TerrainRenderDistance,
    float ModelRenderDistance,
    int TileLoadingDistance)
{
    public string BuildConfiguration { get; init; } = "Unknown";
    public bool D3D11DebugLayerEnabled { get; init; }
    public float MinimumModelScreenSizePixels { get; init; }
    public float TerrainLodTransitionPixels { get; init; }
}

public sealed record PerformanceStepSummary(
    string Name,
    FrameTimingDomain Domain,
    PerformanceDistribution DurationMilliseconds);

public sealed record PerformanceCaptureSummary(
    PerformanceDistribution CpuFrameMilliseconds,
    PerformanceDistribution GpuFrameMilliseconds,
    PerformanceDistribution EngineFrameMilliseconds,
    PerformanceDistribution FrameIntervalMilliseconds,
    IReadOnlyList<PerformanceStepSummary> Steps);

public sealed record PerformanceCaptureFile(
    int FormatVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    PerformanceCaptureContext Context,
    PerformanceCaptureSummary Summary,
    IReadOnlyList<FrameProfileSnapshot> Samples);

public static class PerformanceCaptureAnalyzer
{
    public static PerformanceCaptureFile Create(
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        PerformanceCaptureContext context,
        IReadOnlyList<FrameProfileSnapshot> samples)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
            throw new ArgumentException("A performance capture needs at least one sample.", nameof(samples));

        var stepSummaries = samples
            .SelectMany(sample => sample.Steps)
            .GroupBy(step => (step.Name, step.Domain))
            .OrderBy(group => group.Key.Domain)
            .ThenBy(group => group.Key.Name, StringComparer.Ordinal)
            .Select(group => new PerformanceStepSummary(
                group.Key.Name,
                group.Key.Domain,
                Summarize(group.Select(step => step.DurationMilliseconds))))
            .ToArray();

        return new PerformanceCaptureFile(
            2,
            startedAt,
            endedAt,
            context,
            new PerformanceCaptureSummary(
                Summarize(samples.Select(sample => sample.CpuFrameMilliseconds)),
                Summarize(samples.Where(sample => sample.GpuFrameMilliseconds.HasValue)
                    .Select(sample => sample.GpuFrameMilliseconds!.Value)),
                Summarize(samples.Select(sample => sample.EngineFrameMilliseconds)),
                Summarize(samples.Select(sample => sample.FrameIntervalMilliseconds)),
                stepSummaries),
            samples);
    }

    public static PerformanceDistribution Summarize(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var ordered = values
            .Where(double.IsFinite)
            .OrderBy(value => value)
            .ToArray();
        if (ordered.Length == 0)
            return new PerformanceDistribution(0, 0, 0, 0, 0, 0);

        return new PerformanceDistribution(
            ordered.Length,
            ordered.Average(),
            Percentile(ordered, 0.50d),
            Percentile(ordered, 0.95d),
            Percentile(ordered, 0.99d),
            ordered[^1]);
    }

    private static double Percentile(IReadOnlyList<double> ordered, double percentile)
    {
        if (ordered.Count == 1)
            return ordered[0];

        var position = Math.Clamp(percentile, 0d, 1d) * (ordered.Count - 1);
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);
        if (lowerIndex == upperIndex)
            return ordered[lowerIndex];

        var fraction = position - lowerIndex;
        return ordered[lowerIndex] + (ordered[upperIndex] - ordered[lowerIndex]) * fraction;
    }
}
