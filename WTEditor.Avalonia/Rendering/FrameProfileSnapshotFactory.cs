using WTEditor.Application.Models;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Streaming;

namespace WTEditor.Avalonia.Rendering;

/// <summary>
/// Converts renderer counters into the immutable profiling snapshot consumed by
/// performance tooling. Keeping this mapping outside the viewport control makes
/// additions to renderer telemetry local and testable.
/// </summary>
internal static class FrameProfileSnapshotFactory
{
    public static FrameProfileSnapshot Create(
        long frameNumber,
        DateTimeOffset timestamp,
        double deltaSeconds,
        double inputMilliseconds,
        double engineFrameMilliseconds,
        double streamingBudgetMilliseconds,
        int viewportWidth,
        int viewportHeight,
        RendererStats stats)
    {
        var gpuUploadMilliseconds = stats.GpuUploadTimeMs ?? 0;
        var gpuWorldModelMilliseconds = stats.GpuWorldModelTimeMs ?? 0;
        var gpuDoodadMilliseconds = stats.GpuDoodadTimeMs ?? 0;
        var gpuTerrainMilliseconds = stats.GpuTerrainTimeMs ?? 0;
        var gpuLiquidMilliseconds = stats.GpuLiquidTimeMs ?? 0;
        var gpuDebugMilliseconds = stats.GpuDebugTimeMs ?? 0;
        var hasDetailedGpuTiming =
            stats.GpuWorldModelTimeMs.HasValue ||
            stats.GpuDoodadTimeMs.HasValue ||
            stats.GpuTerrainTimeMs.HasValue ||
            stats.GpuLiquidTimeMs.HasValue ||
            stats.GpuDebugTimeMs.HasValue;
        var gpuOtherMilliseconds = Math.Max(
            0,
            (stats.GpuFrameTimeMs ?? 0) - gpuUploadMilliseconds -
            (hasDetailedGpuTiming
                ? gpuWorldModelMilliseconds + gpuDoodadMilliseconds +
                  gpuTerrainMilliseconds + gpuLiquidMilliseconds + gpuDebugMilliseconds
                : stats.GpuDrawTimeMs ?? 0));
        var profiledSceneCpuMilliseconds =
            stats.SceneSetupTimeMs +
            stats.TileHierarchyCullingTimeMs +
            stats.WmoCullingTimeMs + stats.WmoSubmissionTimeMs +
            stats.M2CullingTimeMs + stats.M2AnimationTimeMs + stats.M2SubmissionTimeMs +
            stats.TerrainCullingTimeMs + stats.TerrainSubmissionTimeMs +
            stats.LiquidCullingTimeMs + stats.LiquidSubmissionTimeMs +
            stats.DebugSubmissionTimeMs;
        var otherSceneWorkMilliseconds = Math.Max(
            0,
            stats.SceneRenderTimeMs - profiledSceneCpuMilliseconds);
        var steps = new List<FrameTimingStep>
        {
            new("World streaming (CPU)", stats.TileUpdateTimeMs),
            new("Resource upload submission (CPU)", stats.AssetUploadTimeMs),
            new("Tile hierarchy culling (CPU)", stats.TileHierarchyCullingTimeMs),
            new("WMO culling (CPU)", stats.WmoCullingTimeMs),
            new("WMO command submission (CPU)", stats.WmoSubmissionTimeMs),
            new("M2 culling (CPU)", stats.M2CullingTimeMs),
            new("M2 animations (CPU)", stats.M2AnimationTimeMs),
            new("M2 command submission (CPU)", stats.M2SubmissionTimeMs),
            new("Terrain culling (CPU)", stats.TerrainCullingTimeMs),
            new("Terrain command submission (CPU)", stats.TerrainSubmissionTimeMs),
            new("Liquids culling (CPU)", stats.LiquidCullingTimeMs),
            new("Liquids command submission (CPU)", stats.LiquidSubmissionTimeMs),
            new("Scene setup (CPU)", stats.SceneSetupTimeMs),
            new("Debug command submission (CPU)", stats.DebugSubmissionTimeMs),
            new("Other scene work (CPU)", otherSceneWorkMilliseconds),
            new(
                "Other frame work (CPU)",
                inputMilliseconds + stats.UpdateTimeMs + stats.RenderOverheadTimeMs),
            new(
                "Resource uploads (GPU timeline)",
                gpuUploadMilliseconds,
                FrameTimingDomain.Gpu)
        };

        if (hasDetailedGpuTiming)
        {
            steps.Add(new(
                "WMO span (GPU timeline)",
                gpuWorldModelMilliseconds,
                FrameTimingDomain.Gpu));
            steps.Add(new(
                "M2 span (GPU timeline)",
                gpuDoodadMilliseconds,
                FrameTimingDomain.Gpu));
            steps.Add(new(
                "Terrain span (GPU timeline)",
                gpuTerrainMilliseconds,
                FrameTimingDomain.Gpu));
            steps.Add(new(
                "Liquids span (GPU timeline)",
                gpuLiquidMilliseconds,
                FrameTimingDomain.Gpu));
            steps.Add(new(
                "Debug span (GPU timeline)",
                gpuDebugMilliseconds,
                FrameTimingDomain.Gpu));
        }
        else
        {
            steps.Add(new(
                "World span (GPU timeline)",
                stats.GpuDrawTimeMs ?? 0,
                FrameTimingDomain.Gpu));
        }

        steps.Add(new("Other GPU timeline", gpuOtherMilliseconds, FrameTimingDomain.Gpu));

        return new FrameProfileSnapshot(
            frameNumber,
            timestamp,
            deltaSeconds * 1_000d,
            inputMilliseconds + stats.CpuFrameTimeMs,
            stats.GpuFrameTimeMs,
            steps,
            (int)stats.DrawCalls,
            checked((long)stats.SubmittedIndexCount),
            stats.PendingAssetOperations)
        {
            EngineFrameMilliseconds = engineFrameMilliseconds,
            StreamingBudgetMilliseconds = streamingBudgetMilliseconds,
            UploadedResources = stats.UploadedResources,
            ViewportWidth = viewportWidth,
            ViewportHeight = viewportHeight,
            Culling = CreateCullingMetrics(stats),
            RenderWorkload = CreateRenderWorkloadMetrics(stats),
            AssetStreaming = new AssetStreamingProfile(
                ToProfile(stats.AssetStreaming.Adt),
                ToProfile(stats.AssetStreaming.Blp),
                ToProfile(stats.AssetStreaming.M2),
                ToProfile(stats.AssetStreaming.Wmo))
        };
    }

    private static CullingMetrics CreateCullingMetrics(RendererStats stats) => new(
        stats.VisibleTerrainChunks,
        stats.CandidateTerrainChunks,
        stats.VisibleWorldModels,
        stats.CandidateWorldModels,
        stats.VisibleDoodads,
        stats.CandidateDoodads,
        stats.SizeCulledWorldModels,
        stats.SizeCulledDoodads,
        stats.FarLodTerrainChunks,
        stats.CandidateTiles,
        stats.CoarseCulledTiles,
        stats.PortalCulledWmoGroups,
        stats.PortalCulledDoodads,
        stats.TraversedWmoPortalReferences);

    private static RenderWorkloadMetrics CreateRenderWorkloadMetrics(RendererStats stats) => new(
        new RenderPassMetrics[]
        {
            new(
                "World models (WMO)",
                stats.WmoCullingTimeMs,
                stats.WmoSubmissionTimeMs,
                stats.GpuWorldModelTimeMs,
                (int)stats.WmoDrawCalls,
                (int)stats.WmoSubmittedInstances,
                "instance-batch submissions",
                checked((long)stats.WmoSubmittedIndices)),
            new(
                "Doodads (M2)",
                stats.M2CullingTimeMs,
                stats.M2SubmissionTimeMs,
                stats.GpuDoodadTimeMs,
                (int)stats.M2DrawCalls,
                (int)stats.M2SubmittedInstances,
                "instance-batch submissions",
                checked((long)stats.M2SubmittedIndices)),
            new(
                "Terrain (ADT)",
                stats.TerrainCullingTimeMs,
                stats.TerrainSubmissionTimeMs,
                stats.GpuTerrainTimeMs,
                (int)stats.TerrainDrawCalls,
                (int)stats.TerrainSubmittedChunks,
                "visible chunks",
                checked((long)stats.TerrainSubmittedIndices)),
            new(
                "Liquids (MH2O)",
                stats.LiquidCullingTimeMs,
                stats.LiquidSubmissionTimeMs,
                null,
                (int)stats.LiquidDrawCalls,
                stats.VisibleLiquidBatches,
                "visible batches",
                checked((long)stats.LiquidSubmittedIndices))
        },
        (int)stats.InstanceBufferMapCalls,
        (int)stats.ConstantBufferUpdates,
        (int)stats.TextureBindingCalls,
        (int)stats.BlendStateBindings,
        (int)stats.VertexBufferBindings,
        (int)stats.IndexBufferBindings);

    private static AssetPipelineProfile ToProfile(AssetPipelineMetrics metrics) => new(
        metrics.Pending,
        metrics.Active,
        metrics.Completed,
        metrics.Skipped,
        metrics.Failed,
        metrics.LastProcessingMilliseconds,
        metrics.MaximumProcessingMilliseconds);
}
