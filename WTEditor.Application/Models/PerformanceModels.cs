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

public sealed record FrameTimingCategory(
    string Name,
    string Description,
    string Color,
    int Order);

public static class FrameTimingCategoryCatalog
{
    private static readonly IReadOnlyDictionary<string, FrameTimingCategory> Categories =
        new Dictionary<string, FrameTimingCategory>(StringComparer.Ordinal)
        {
            ["World streaming (CPU)"] = Category("World streaming (CPU)", "CPU time spent advancing world-tile streaming and its per-frame loading work.", "#FFD166", 0),
            ["Resource upload submission (CPU)"] = Category("Resource upload submission (CPU)", "CPU time spent preparing and submitting resource uploads to D3D11.", "#F78C6B", 1),
            ["Tile hierarchy culling (CPU)"] = Category("Tile hierarchy culling (CPU)", "CPU time spent finding terrain and world-model tile roots that can contribute to this view.", "#C4A77D", 2),
            ["WMO culling (CPU)"] = Category("WMO culling (CPU)", "CPU visibility work for WMO groups, including frustum, distance, portal, and related candidate tests.", "#76B7FF", 3),
            ["WMO command submission (CPU)"] = Category("WMO command submission (CPU)", "CPU time preparing WMO constant buffers, bindings, and draw commands after culling.", "#245A9B", 4),
            ["M2 culling (CPU)"] = Category("M2 culling (CPU)", "CPU visibility work for M2 instances, including frustum, distance, portal, and screen-size tests.", "#94DCCB", 5),
            ["M2 animations (CPU)"] = Category("M2 animations (CPU)", "CPU time evaluating M2 bone palettes and material animations before their draw commands are submitted.", "#4EB6A7", 6),
            ["M2 command submission (CPU)"] = Category("M2 command submission (CPU)", "CPU time preparing M2 instance data, material state, bindings, and draw commands after animation and culling.", "#1F7169", 7),
            ["Terrain culling (CPU)"] = Category("Terrain culling (CPU)", "CPU visibility and level-of-detail selection work for terrain chunks.", "#B8DF78", 8),
            ["Terrain command submission (CPU)"] = Category("Terrain command submission (CPU)", "CPU time preparing terrain buffers, material state, and draw commands after culling.", "#5F8F2E", 9),
            ["Liquids culling (CPU)"] = Category("Liquids culling (CPU)", "CPU visibility and batch selection work for ADT and visible WMO liquid surfaces.", "#8BD7EA", 10),
            ["Liquids command submission (CPU)"] = Category("Liquids command submission (CPU)", "CPU time preparing and submitting liquid surface draw commands.", "#2D7895", 11),
            ["Scene setup (CPU)"] = Category("Scene setup (CPU)", "CPU scene setup before pass culling: camera matrices, lighting, shader state, and shared render state.", "#B6C0CC", 12),
            ["Debug command submission (CPU)"] = Category("Debug command submission (CPU)", "CPU time submitting optional bounds and selection-debug geometry.", "#778899", 13),
            ["Other scene work (CPU)"] = Category("Other scene work (CPU)", "Remaining SceneRenderTime not covered by named categories; typically sky rendering, renderer bookkeeping, and small uninstrumented gaps.", "#566574", 14),
            ["Other frame work (CPU)"] = Category("Other frame work (CPU)", "CPU time outside scene rendering and streaming, including input, editor update work, and render-loop overhead.", "#5EA1FF", 15),
            ["Resource uploads (GPU timeline)"] = Category("Resource uploads (GPU timeline)", "GPU command-stream time between the upload timestamps; this is not CPU upload preparation time.", "#FF9F43", 20),
            ["World span (GPU timeline)"] = Category("World span (GPU timeline)", "GPU draw timeline used when detailed pass timestamps are disabled; it covers the opaque scene draw span.", "#FF5DA2", 21),
            ["WMO span (GPU timeline)"] = Category("WMO span (GPU timeline)", "GPU timestamp span covering WMO draw commands.", "#3E7FC1", 22),
            ["M2 span (GPU timeline)"] = Category("M2 span (GPU timeline)", "GPU timestamp span covering M2 draw commands; CPU animation evaluation is tracked separately.", "#2E9A8F", 23),
            ["Terrain span (GPU timeline)"] = Category("Terrain span (GPU timeline)", "GPU timestamp span covering terrain draw commands.", "#6E9B35", 24),
            ["Liquids span (GPU timeline)"] = Category("Liquids span (GPU timeline)", "GPU timestamp span covering liquid draw commands.", "#287E9A", 25),
            ["Debug span (GPU timeline)"] = Category("Debug span (GPU timeline)", "GPU timestamp span covering optional bounds and selection-debug draw commands.", "#B89CFF", 26),
            ["Other GPU timeline"] = Category("Other GPU timeline", "GPU frame time outside resource uploads and the sampled WMO, M2, terrain, liquid, and debug spans; it includes sky work, command-stream gaps, and any uninstrumented GPU work.", "#B455D4", 27),
            ["Wait for viewport texture"] = Category("Wait for viewport texture", "Presentation wait for the viewport texture consumer; this is separate from GPU command-stream timing.", "#607080", 28)
        };

    public static FrameTimingCategory Get(string? name) =>
        name != null && Categories.TryGetValue(name, out var category)
            ? category
            : new FrameTimingCategory(
                name ?? "Unknown timing category",
                "Timing category reported by the renderer.",
                "#718090",
                int.MaxValue);

    public static string Describe(string? name) => Get(name).Description;
    public static string ColorFor(string? name) => Get(name).Color;
    public static int OrderOf(string? name) => Get(name).Order;

    public static IReadOnlyList<FrameTimingStep> OrderSteps(
        IEnumerable<FrameTimingStep> steps,
        FrameTimingDomain? domain = null)
    {
        ArgumentNullException.ThrowIfNull(steps);

        return steps
            .Where(step => (!domain.HasValue || step.Domain == domain) &&
                           double.IsFinite(step.DurationMilliseconds) &&
                           step.DurationMilliseconds > 0)
            .GroupBy(step => new { step.Domain, step.Name })
            .Select(group => new FrameTimingStep(
                group.Key.Name,
                group.Sum(step => step.DurationMilliseconds),
                group.Key.Domain))
            .OrderBy(step => OrderOf(step.Name))
            .ThenBy(step => step.Name, StringComparer.Ordinal)
            .ThenBy(step => step.Domain)
            .ToArray();
    }

    private static FrameTimingCategory Category(
        string name,
        string description,
        string color,
        int order) => new(name, description, color, order);
}

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

public sealed record AssetPipelineProfile(
    int Pending,
    int Active,
    long Completed,
    long Skipped,
    long Failed,
    double LastProcessingMilliseconds,
    double MaximumProcessingMilliseconds);

public sealed record AssetStreamingProfile(
    AssetPipelineProfile Adt,
    AssetPipelineProfile Blp,
    AssetPipelineProfile M2,
    AssetPipelineProfile Wmo)
{
    private static readonly AssetPipelineProfile EmptyPipeline = new(0, 0, 0, 0, 0, 0, 0);

    public static AssetStreamingProfile Empty { get; } = new(
        EmptyPipeline,
        EmptyPipeline,
        EmptyPipeline,
        EmptyPipeline);

    public long Skipped => Adt.Skipped + Blp.Skipped + M2.Skipped + Wmo.Skipped;
    public long Failed => Adt.Failed + Blp.Failed + M2.Failed + Wmo.Failed;
    public double MaximumProcessingMilliseconds => Math.Max(
        Math.Max(Adt.MaximumProcessingMilliseconds, Blp.MaximumProcessingMilliseconds),
        Math.Max(M2.MaximumProcessingMilliseconds, Wmo.MaximumProcessingMilliseconds));
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
    public double StreamingBudgetMilliseconds { get; init; }
    public int UploadedResources { get; init; }
    public CullingMetrics Culling { get; init; } = new(0, 0, 0, 0, 0, 0);
    public RenderWorkloadMetrics RenderWorkload { get; init; } = RenderWorkloadMetrics.Empty;
    public AssetStreamingProfile AssetStreaming { get; init; } = AssetStreamingProfile.Empty;
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
