using System.Numerics;
using System.Runtime.InteropServices;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

internal readonly record struct Wrath335SceneExteriorSeed(
    int PlacementIndex, int GroupIndex, BoundingBox Bounds, int Bucket, int Arrival);

/// <summary>12340 scene arrival lists, depth buckets and updated-placement exceptions.</summary>
internal sealed class Wrath335SceneExteriorGroups
{
    private readonly List<Wrath335SceneExteriorSeed> bucketed = [];
    private readonly List<Wrath335SceneExteriorSeed> unbucketed = [];
    private readonly List<Wrath335SceneExteriorSeed> ordered = [];
    private Vector3 forward;
    private Vector4 depthPlane;
    private BoundingBox? cameraBounds;
    private int arrival;
    private static readonly IComparer<Wrath335SceneExteriorSeed> Order =
        Comparer<Wrath335SceneExteriorSeed>.Create(static (a, b) =>
        {
            var bucket = a.Bucket.CompareTo(b.Bucket);
            return bucket != 0 ? bucket : a.Arrival.CompareTo(b.Arrival);
        });

    public ReadOnlySpan<Wrath335SceneExteriorSeed> Seeds => CollectionsMarshal.AsSpan(ordered);

    public void Begin(Vector3 eye, Vector3 cameraForward, in Matrix4x4 viewProjection)
    {
        bucketed.Clear();
        unbucketed.Clear();
        ordered.Clear();
        arrival = 0;
        forward = cameraForward;
        depthPlane = Wrath335ExteriorGroupOrder.HorizontalDepthPlane(eye, forward);
        cameraBounds = TryGetCameraBounds(viewProjection, out var bounds) ? bounds : null;
    }

    public void AddPlacement(int placementIndex, ReadOnlySpan<WorldModelGroupBatches> groups,
        ReadOnlySpan<bool> enabled, in Matrix4x4 model, uint runtimeFlags, bool prepared)
    {
        // 0x7B6110 requires prepared runtime 0x80, loaded groups and !0x20.
        // The editor supplies decoded availability; native streaming lifetimes remain open.
        if (!prepared || !Wrath335ViewerPlacementSelection.CanQuery(runtimeFlags))
            return;
        for (var index = 0; index < groups.Length; index++)
        {
            if (!enabled[index] || (groups[index].mogiFlags & 0x10008) == 0)
                continue;
            var bounds = Wrath335ExteriorGroupOrder.WorldBounds(groups[index].mogiBoundingBox, model);
            if (cameraBounds is { } aoi && !Overlaps(bounds, aoi))
                continue;
            if ((runtimeFlags & Wrath335ViewerPlacementSelection.UpdatedTransform) != 0)
                unbucketed.Add(new(placementIndex, index, bounds, -1, arrival++));
            else if (Wrath335ExteriorGroupOrder.TryGetBucket(bounds, forward, depthPlane, out var bucket))
                bucketed.Add(new(placementIndex, index, bounds, bucket, arrival++));
        }
    }

    public void Complete(bool hasPrimary, bool hasExterior)
    {
        ordered.Clear();
        if (hasExterior || !hasPrimary)
            ordered.AddRange(bucketed);
        if (!hasPrimary)
        {
            // 0x792BD0 appends rebucketed nodes after ordinary arrivals. Its first
            // >=64 result breaks the whole pass (0x792D0A), rather than skipping one.
            var rebucketArrival = arrival;
            foreach (var seed in unbucketed)
            {
                if (!Wrath335ExteriorGroupOrder.TryGetBucket(seed.Bounds, forward, depthPlane, out var bucket))
                    break;
                ordered.Add(seed with { Bucket = bucket, Arrival = rebucketArrival++ });
            }
        }
        ordered.Sort(Order);
        if (hasPrimary)
            ordered.AddRange(unbucketed);
    }

    public static bool AcceptUnbucketed(in BoundingBox bounds, bool hasExterior,
        ReadOnlySpan<BoundingBox> visibleBounds)
    {
        // 0x79A03A..0x79A0BA reads the live callback list, including earlier seeds.
        foreach (var visible in visibleBounds)
            if (Overlaps(bounds, visible))
                return true;
        return hasExterior;
    }

    internal static bool Overlaps(in BoundingBox a, in BoundingBox b) =>
        a.Max.X >= b.Min.X && a.Min.X <= b.Max.X &&
        a.Max.Y >= b.Min.Y && a.Min.Y <= b.Max.Y &&
        a.Max.Z >= b.Min.Z && a.Min.Z <= b.Max.Z;

    internal static bool TryGetCameraBounds(in Matrix4x4 viewProjection, out BoundingBox bounds)
    {
        // 0x79595C constructs 0xCD8F44 from all eight base world-frustum corners.
        // DX11 maps their depth endpoints to 0/1. Unsupported projections fail open.
        bounds = default;
        if (!Matrix4x4.Invert(viewProjection, out var inverse))
            return false;
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (var corner = 0; corner < 8; corner++)
        {
            var point = Vector4.Transform(new Vector4((corner & 1) == 0 ? -1f : 1f,
                (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? 0f : 1f, 1f), inverse);
            if (point.W == 0f || !float.IsFinite(point.W))
                return false;
            var world = new Vector3(point.X, point.Y, point.Z) / point.W;
            if (!float.IsFinite(world.X) || !float.IsFinite(world.Y) || !float.IsFinite(world.Z))
                return false;
            min = Vector3.Min(min, world);
            max = Vector3.Max(max, world);
        }
        bounds = new(min, max);
        return true;
    }
}

/// <summary>Shared 0x7AD1F0/0x7AD350 placement stamp; visibility survives stamp changes.</summary>
internal sealed class Wrath335PortalPlacementCache
{
    private WmoPortalVisibilityScratch? previous;
    public void Reset() => previous = null;
    public void Enter(WmoPortalVisibilityScratch scratch)
    {
        if (ReferenceEquals(previous, scratch))
            return;
        scratch.ProjectedPortals.AsSpan().Clear();
        scratch.EmittedPortalViews.AsSpan().Clear();
        previous = scratch;
    }
}
