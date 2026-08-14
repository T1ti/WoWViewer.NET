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
    int CandidateDoodads,
    int SizeCulledWorldModels = 0,
    int SizeCulledDoodads = 0,
    int FarLodTerrainChunks = 0,
    int CandidateTiles = 0,
    int CoarseCulledTiles = 0,
    int PortalCulledWmoGroups = 0,
    int PortalCulledDoodads = 0,
    int TraversedWmoPortalReferences = 0)
{
    public int CulledTerrainChunks => Math.Max(0, CandidateTerrainChunks - VisibleTerrainChunks);
    public int CulledWorldModels => Math.Max(
        0,
        CandidateWorldModels - VisibleWorldModels - SizeCulledWorldModels);
    public int CulledDoodads => Math.Max(
        0,
        CandidateDoodads - VisibleDoodads - SizeCulledDoodads - PortalCulledDoodads);
}

public sealed record RenderPassMetrics(
    string Name,
    double CpuCullingMilliseconds,
    double CpuSubmissionMilliseconds,
    double? GpuMilliseconds,
    int DrawCalls,
    int SubmittedItems,
    string SubmittedItemLabel,
    long SubmittedIndices = 0)
{
    public long SubmittedTriangles => SubmittedIndices / 3;
}

public sealed record RenderWorkloadMetrics(
    IReadOnlyList<RenderPassMetrics> Passes,
    int InstanceBufferMaps,
    int ConstantBufferUpdates,
    int TextureBindingCalls,
    int BlendStateBindings,
    int VertexBufferBindings = 0,
    int IndexBufferBindings = 0)
{
    public static RenderWorkloadMetrics Empty { get; } = new(
        Array.Empty<RenderPassMetrics>(),
        0,
        0,
        0,
        0);
}

public sealed record FrameProfileSnapshot(
    long FrameNumber,
    DateTimeOffset CapturedAt,
    double FrameIntervalMilliseconds,
    double CpuFrameMilliseconds,
    double? GpuFrameMilliseconds,
    IReadOnlyList<FrameTimingStep> Steps,
    int DrawCalls,
    long SubmittedIndices,
    int PendingAssetOperations)
{
    public long SubmittedTriangles => SubmittedIndices / 3;
    public double EngineFrameMilliseconds { get; init; }
    public int UploadedResources { get; init; }
    public CullingMetrics Culling { get; init; } = new(0, 0, 0, 0, 0, 0);
    public RenderWorkloadMetrics RenderWorkload { get; init; } = RenderWorkloadMetrics.Empty;
    public int ViewportWidth { get; init; }
    public int ViewportHeight { get; init; }

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

    public static bool IsLikelyCpuSubmissionStarved(
        IEnumerable<FrameProfileSnapshot> samples,
        int maximumSamples = 30,
        double minimumCpuMilliseconds = 16.67d)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumSamples, 1);

        var comparable = samples
            .Where(sample => sample.GpuFrameMilliseconds is > 0 && sample.CpuFrameMilliseconds > 0)
            .TakeLast(maximumSamples)
            .ToArray();
        if (comparable.Length == 0)
            return false;

        var averageCpu = comparable.Average(sample => sample.CpuFrameMilliseconds);
        var averageGpuTimeline = comparable.Average(sample => sample.GpuFrameMilliseconds!.Value);
        var averageSubmission = comparable.Average(sample =>
            sample.RenderWorkload.Passes.Sum(pass => pass.CpuSubmissionMilliseconds));

        return averageCpu >= minimumCpuMilliseconds &&
               averageSubmission >= averageCpu * 0.65d &&
               averageGpuTimeline >= averageCpu * 0.70d &&
               averageGpuTimeline <= averageCpu * 1.10d;
    }
}
