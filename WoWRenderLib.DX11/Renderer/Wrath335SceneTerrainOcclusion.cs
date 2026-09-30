using System.Numerics;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>Feeds terrain edges after each 12340 depth band's WMO visits, as at 0x79A836.</summary>
internal sealed class Wrath335SceneTerrainOcclusion
{
    public Wrath335TerrainClipBuffer Buffer { get; } = new();
    private readonly List<Chunk> sources = [];
    private readonly List<Pending> pending = [];
    private int sourceIndex;
    private Matrix4x4 viewProjection;
    private WmoPortalRect exteriorRect;
    private Wrath335ClipVolumes? volumes;
    private float minimumDistance, maximumDistance;
    private static readonly IComparer<Chunk> Order = Comparer<Chunk>.Create(static (a, b) =>
    {
        var depth = a.Bucket.CompareTo(b.Bucket);
        return depth != 0 ? depth : b.Arrival.CompareTo(a.Arrival); // 0x792DE6 links to the head.
    });

    public void Clear()
    {
        Buffer.Clear();
        sources.Clear();
        pending.Clear();
        sourceIndex = 0;
        volumes = null;
    }

    public void Begin(IReadOnlyList<Container3D> objects, Vector3 eye, Vector3 forward,
        in Matrix4x4 projection, in Matrix4x4 camera, WmoPortalRect exterior,
        float exteriorDistance, bool hasPrimary, float farClip, Wrath335ClipVolumes clipVolumes)
    {
        Clear();
        Buffer.Begin(eye, forward, projection);
        if (!Buffer.Active)
            return;
        viewProjection = camera;
        exteriorRect = exterior;
        volumes = clipVolumes;
        minimumDistance = hasPrimary ? (float)((double)exteriorDistance + 33.333332f) : -10_000f;
        maximumDistance = (float)((double)farClip - 33.333332f);
        var depth = Wrath335ExteriorGroupOrder.HorizontalDepthPlane(eye, forward);
        var full = Wrath335ClipVolumes.NormalizeDirection(forward);
        var arrival = 0;
        foreach (var obj in objects)
        {
            if (obj is not ADTContainer adt || !adt.IsLoaded || !adt.Terrain.usesLegacyLighting ||
                adt.Terrain.vertices is not { Length: >= 145 } vertices ||
                adt.Terrain.chunkBounds is not { Length: > 0 } bounds ||
                adt.Terrain.chunkHoleMasks is not { Length: > 0 } holes)
                continue;
            var model = adt.GetModelMatrix();
            if (!Matrix4x4.Invert(model, out var inverse))
                continue;
            var localForward = Vector3.TransformNormal(forward, inverse);
            var count = Math.Min(bounds.Length, Math.Min(holes.Length, vertices.Length / 145));
            for (var index = 0; index < count; index++)
            {
                var world = Wrath335ExteriorGroupOrder.WorldBounds(bounds[index], model);
                if (!WmoPortalVisibility.IntersectsRect(world, camera, WmoPortalRect.Full))
                    continue;
                // 0x7C3EDA picks from [0,8,136,144] using strict target>eye signs.
                var nearVertex = (localForward.X > 0f ? 136 : 0) + (localForward.Y > 0f ? 8 : 0);
                var point = Vector3.Transform(vertices[index * 145 + nearVertex].Position, model);
                if (!Wrath335ExteriorGroupOrder.TryGetBucket(new(point, point), forward, depth, out var bucket) ||
                    bucket >= 63) // 0x799D65 does not enlist producers from the last band.
                    continue;
                var farVertex = (localForward.X < 0f ? 136 : 0) + (localForward.Y < 0f ? 8 : 0);
                point = Vector3.Transform(vertices[index * 145 + farVertex].Position, model);
                var hasHoles = holes[index] != 0;
                var updateBucket = bucket;
                if (!hasHoles && !Wrath335ExteriorGroupOrder.TryGetBucket(new(point, point), forward, depth,
                        out updateBucket))
                    continue;
                var nearest = new Vector3(forward.X >= 0f ? world.Min.X : world.Max.X,
                    forward.Y >= 0f ? world.Min.Y : world.Max.Y,
                    forward.Z >= 0f ? world.Min.Z : world.Max.Z);
                var distance = (float)((double)(nearest.X - eye.X) * full.X +
                    (double)(nearest.Y - eye.Y) * full.Y + (double)(nearest.Z - eye.Z) * full.Z);
                sources.Add(new(vertices, index, model, localForward, world, hasHoles,
                    bucket, updateBucket, distance, arrival++));
            }
        }
        sources.Sort(Order);
    }

    public void BeginBucket(int bucket)
    {
        // 0x79A7F2 culls terrain against earlier bands before the current WMO consumer.
        while (sourceIndex < sources.Count && sources[sourceIndex].Bucket == bucket)
        {
            var index = sourceIndex++;
            var chunk = sources[index];
            if (!WmoPortalVisibility.IntersectsRect(chunk.Bounds, viewProjection, exteriorRect) ||
                volumes!.ContainsSphere(Wrath335ClipVolumes.GroupSphere(chunk.Bounds, Matrix4x4.Identity)) ||
                Buffer.ContainsBox(chunk.Bounds, 0))
                continue;
            pending.Add(new(index, chunk.UpdateBucket));
        }
    }

    public void EndBucket(int bucket)
    {
        // 0x793798..0x793881 processes intact chunks first, then erases for holes.
        // Pending links are prepended, so reverse enqueue order is significant.
        for (var phase = 0; phase < 2; phase++)
            for (var index = pending.Count - 1; index >= 0; index--)
            {
                var item = pending[index];
                var chunk = sources[item.Source];
                if (item.Bucket != bucket || chunk.HasHoles != (phase == 1))
                    continue;
                if (chunk.HasHoles || (chunk.Distance > minimumDistance && chunk.Distance < maximumDistance))
                    Buffer.UpdateTerrainEdges(chunk.Vertices.AsSpan(chunk.Index * 145, 145),
                        chunk.Model, chunk.LocalForward, chunk.HasHoles);
                pending.RemoveAt(index);
            }
    }

    private readonly record struct Pending(int Source, int Bucket);
    private readonly record struct Chunk(ADTVertex[] Vertices, int Index, Matrix4x4 Model,
        Vector3 LocalForward, BoundingBox Bounds, bool HasHoles, int Bucket, int UpdateBucket,
        float Distance, int Arrival);
}
