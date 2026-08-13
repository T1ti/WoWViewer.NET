namespace WTEditor.Application.Models;

public enum FrameTimingDomain
{
    Cpu,
    Gpu,
    Presentation
}

public enum PerformanceBottleneck
{
    Unknown,
    Balanced,
    Cpu,
    Gpu
}

public sealed record FrameTimingStep(
    string Name,
    double DurationMilliseconds,
    FrameTimingDomain Domain = FrameTimingDomain.Cpu);

public sealed record CullingMetrics(
    int VisibleTerrainChunks,
    int CandidateTerrainChunks,
    int VisibleWorldModels,
    int CandidateWorldModels,
    int VisibleDoodads,
    int CandidateDoodads);

public sealed record FrameProfileSnapshot(
    long FrameNumber,
    DateTimeOffset CapturedAt,
    double FrameIntervalMilliseconds,
    double CpuFrameMilliseconds,
    double? GpuFrameMilliseconds,
    IReadOnlyList<FrameTimingStep> Steps,
    int DrawCalls,
    int SubmittedVertices,
    int PendingAssetOperations)
{
    public double EngineFrameMilliseconds { get; init; }
    public int UploadedResources { get; init; }
    public CullingMetrics Culling { get; init; } = new(0, 0, 0, 0, 0, 0);

    public PerformanceBottleneck Bottleneck =>
        PerformanceAnalyzer.Classify(CpuFrameMilliseconds, GpuFrameMilliseconds);

    public double FramesPerSecond =>
        FrameIntervalMilliseconds > 0 ? 1_000d / FrameIntervalMilliseconds : 0d;
}

public static class PerformanceAnalyzer
{
    public static PerformanceBottleneck Classify(
        double cpuMilliseconds,
        double? gpuMilliseconds,
        double dominanceThreshold = 1.15d)
    {
        if (cpuMilliseconds <= 0 || gpuMilliseconds is null or <= 0)
            return PerformanceBottleneck.Unknown;

        if (cpuMilliseconds > gpuMilliseconds.Value * dominanceThreshold)
            return PerformanceBottleneck.Cpu;

        if (gpuMilliseconds.Value > cpuMilliseconds * dominanceThreshold)
            return PerformanceBottleneck.Gpu;

        return PerformanceBottleneck.Balanced;
    }

    public static PerformanceBottleneck ClassifyRecent(
        IEnumerable<FrameProfileSnapshot> samples,
        int maximumSamples = 30)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumSamples, 1);
        var comparable = samples
            .Where(sample => sample.GpuFrameMilliseconds is > 0 && sample.CpuFrameMilliseconds > 0)
            .TakeLast(maximumSamples)
            .ToArray();

        if (comparable.Length == 0)
            return PerformanceBottleneck.Unknown;

        return Classify(
            comparable.Average(sample => sample.CpuFrameMilliseconds),
            comparable.Average(sample => sample.GpuFrameMilliseconds!.Value));
    }
}
