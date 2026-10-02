using System.Numerics;
using WoWRenderLib.Raycasting;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>12340 loaded-definition enlistment (0x7998A0) and spatial admission (0x7987A0).
/// GPU fade submission, barriers and native streaming availability remain separate contracts.</summary>
internal sealed class Wrath335ExteriorDoodads
{
    private readonly List<Entry>[] buckets = Enumerable.Range(0, 64).Select(_ => new List<Entry>()).ToArray();
    private Vector4 depthPlane;
    private Wrath335DoodadFrustum frustum;
    private readonly record struct Entry(Wrath335WmoDoodadVisibility Visibility, int Doodad, BoundingSphere Sphere);

    public void Clear()
    {
        foreach (var bucket in buckets)
            bucket.Clear();
    }

    public void Begin(Vector3 eye, Vector3 forward, in Matrix4x4 viewProjection, WmoPortalRect exterior)
    {
        Clear();
        depthPlane = Wrath335ExteriorGroupOrder.HorizontalDepthPlane(eye, forward);
        frustum = Wrath335DoodadFrustum.Create(viewProjection, exterior);
    }

    public void Enlist(Wrath335WmoDoodadVisibility visibility, ReadOnlySpan<ushort> references, int groupBucket)
    {
        foreach (var doodad in references)
            if (visibility.TryGetExteriorSphere(doodad, out var sphere) &&
                TryGetBucket(sphere, depthPlane, groupBucket, out var bucket))
            {
                visibility.SetExteriorQueued(doodad, true); // def+0xAC: only one active intrusive link.
                buckets[bucket].Add(new(visibility, doodad, sphere)); // 0x6DED60 appends.
            }
    }

    public void Consume(int bucket, Wrath335ClipVolumes volumes, Wrath335TerrainClipBuffer terrain)
    {
        foreach (var entry in buckets[bucket])
        {
            entry.Visibility.SetExteriorQueued(entry.Doodad, false); // 0x79880B..0x798811 unlinks first.
            entry.Visibility.AdmitExterior(entry.Doodad, entry.Sphere, frustum, volumes, terrain,
                (float)((double)bucket * 33.333332f)); // 0x79A826 float store before category classification.
        }
        buckets[bucket].Clear();
    }

    internal static bool TryGetBucket(in BoundingSphere sphere, Vector4 plane, int groupBucket, out int bucket)
    {
        bucket = groupBucket;
        if ((uint)groupBucket >= 64)
            return false;
        var depth = (double)sphere.Center.Y * plane.Y + (double)sphere.Center.Z * plane.Z +
            (double)sphere.Center.X * plane.X + plane.W - sphere.Radius;
        if (!double.IsFinite(depth))
            return false;
        if (depth <= 0d)
            return true;
        var scaled = (float)(depth * 0.03f);
        var rounded = Math.Round((double)scaled - 0.5d, MidpointRounding.ToEven);
        // Native unsigned cutoff precedes the originating-group floor.
        if (rounded < 0d || rounded >= 64d)
            return false;
        bucket = Math.Max(groupBucket, (int)rounded);
        return true;
    }
}
