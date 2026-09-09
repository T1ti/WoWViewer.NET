using System.Numerics;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Editing;

public readonly record struct TerrainBrushSample(
    ADTVertex[] Vertices,
    int VertexIndex,
    float CurrentHeight,
    float Amount,
    float Radius,
    float FlattenHeight,
    EditAction Action,
    float? NeighborhoodHeight = null);

/// <summary>
/// Mode-specific terrain behavior. Traversal, falloff, upload, dirty state,
/// and undo remain in the shared brush pipeline.
/// </summary>
public abstract class TerrainBrushTool
{
    public abstract TerrainBrushMode Mode { get; }
    public abstract Vector4 PreviewColor { get; }

    public virtual int GetPassCount(int requestedIterations) => 1;

    public abstract float Apply(in TerrainBrushSample sample);
}

public sealed class SculptTerrainBrushTool : TerrainBrushTool
{
    public override TerrainBrushMode Mode => TerrainBrushMode.Sculpt;
    public override Vector4 PreviewColor => new(0.2f, 0.85f, 1f, 1f);

    public override float Apply(in TerrainBrushSample sample) =>
        sample.CurrentHeight +
        (sample.Action == EditAction.Negative ? -sample.Amount : sample.Amount);
}

public sealed class SmoothTerrainBrushTool : TerrainBrushTool
{
    private const int VerticesPerTerrainChunk = 145;
    private const int MaximumSmoothIterations = 8;
    private const float MaximumNeighborhoodRadius = 12f;

    public override TerrainBrushMode Mode => TerrainBrushMode.Smooth;
    public override Vector4 PreviewColor => new(0.35f, 1f, 0.55f, 1f);

    public override int GetPassCount(int requestedIterations) =>
        Math.Clamp(requestedIterations, 1, MaximumSmoothIterations);

    public override float Apply(in TerrainBrushSample sample)
    {
        if (sample.NeighborhoodHeight is { } average)
            return sample.CurrentHeight +
                (average - sample.CurrentHeight) * Math.Clamp(sample.Amount, 0f, 1f);
        var chunkStart = sample.VertexIndex / VerticesPerTerrainChunk * VerticesPerTerrainChunk;
        var chunkEnd = Math.Min(chunkStart + VerticesPerTerrainChunk, sample.Vertices.Length);
        var center = sample.Vertices[sample.VertexIndex].Position;
        var neighborhoodRadius = MathF.Min(sample.Radius, MaximumNeighborhoodRadius);
        var neighborhoodRadiusSquared = neighborhoodRadius * neighborhoodRadius;
        var total = 0f;
        var count = 0;

        for (var index = chunkStart; index < chunkEnd; index++)
        {
            var candidate = sample.Vertices[index].Position;
            var deltaX = center.X - candidate.X;
            var deltaY = center.Y - candidate.Y;
            if ((deltaX * deltaX) + (deltaY * deltaY) <= neighborhoodRadiusSquared)
            {
                total += candidate.Z;
                count++;
            }
        }

        if (count == 0)
            return sample.CurrentHeight;

        return sample.CurrentHeight +
               (total / count - sample.CurrentHeight) * Math.Clamp(sample.Amount, 0f, 1f);
    }
}

public sealed class FlattenTerrainBrushTool : TerrainBrushTool
{
    public override TerrainBrushMode Mode => TerrainBrushMode.Flatten;
    public override Vector4 PreviewColor => new(0.75f, 0.55f, 1f, 1f);

    public override float Apply(in TerrainBrushSample sample) =>
        sample.CurrentHeight +
        (sample.FlattenHeight - sample.CurrentHeight) * Math.Clamp(sample.Amount, 0f, 1f);
}

/// <summary>
/// Central registry for terrain operations. Adding a terrain operation means
/// registering one tool here; the spatial and mutation pipeline stays shared.
/// </summary>
public static class TerrainBrushTools
{
    private static readonly IReadOnlyDictionary<TerrainBrushMode, TerrainBrushTool> Registered =
        new Dictionary<TerrainBrushMode, TerrainBrushTool>
        {
            [TerrainBrushMode.Sculpt] = new SculptTerrainBrushTool(),
            [TerrainBrushMode.Smooth] = new SmoothTerrainBrushTool(),
            [TerrainBrushMode.Flatten] = new FlattenTerrainBrushTool()
        };

    public static TerrainBrushTool Get(TerrainBrushMode mode) =>
        Registered.TryGetValue(mode, out var tool)
            ? tool
            : throw new ArgumentOutOfRangeException(nameof(mode), mode, "No terrain brush tool is registered.");
}
