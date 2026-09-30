using System.Numerics;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

internal readonly record struct Wrath335ExteriorGroupSeed(int GroupIndex, int Bucket);

/// <summary>12340 depth-bucket order for one placement's exterior group seeds.
/// Scene-wide source queues, unbucketed exceptions and terrain clipping remain separate.</summary>
internal sealed class Wrath335ExteriorGroupOrder
{
    private const int BucketCount = 64;
    private Wrath335ExteriorGroupSeed[] seeds = [];
    private int count;
    private static readonly IComparer<Wrath335ExteriorGroupSeed> Order =
        Comparer<Wrath335ExteriorGroupSeed>.Create(static (left, right) =>
        {
            var bucket = left.Bucket.CompareTo(right.Bucket);
            // 0x7BDE9A..0x7BDFD1 creates groups in ascending index order;
            // 0x6DEDAC..0x6DEDBB appends, despite the guessed "Head" name.
            return bucket != 0 ? bucket : left.GroupIndex.CompareTo(right.GroupIndex);
        });

    public ReadOnlySpan<Wrath335ExteriorGroupSeed> Seeds => seeds.AsSpan(0, count);

    public void Build(ReadOnlySpan<WorldModelGroupBatches> groups, ReadOnlySpan<bool> enabled,
        in Matrix4x4 model, Vector3 eye, Vector3 cameraForward, bool depthSorted = true)
    {
        count = 0;
        if (seeds.Length < groups.Length)
            seeds = new Wrath335ExteriorGroupSeed[groups.Length];
        var plane = HorizontalDepthPlane(eye, cameraForward);
        for (var index = 0; index < groups.Length; index++)
        {
            // 0x792AED selects MOGI exterior/always-draw, not loaded MOGP.
            if (!enabled[index] || (groups[index].mogiFlags & 0x10008) == 0)
                continue;
            // 0x792AFF/0x792B07: runtime bit 0x400 bypasses bucket classification
            // while a viewer placement is active; retain source append order.
            if (!depthSorted)
            {
                seeds[count++] = new(index, -1);
                continue;
            }
            var bounds = WorldBounds(groups[index].mogiBoundingBox, model);
            if (TryGetBucket(bounds, cameraForward, plane, out var bucket))
                seeds[count++] = new(index, bucket);
        }
        if (depthSorted)
            Array.Sort(seeds, 0, count, Order);
    }

    internal static Vector4 HorizontalDepthPlane(Vector3 eye, Vector3 forward)
    {
        // 0x7954E1..0x79563E: normalize the full direction, then its XY
        // projection only when squared horizontal length is >0.0001.
        var length = Math.Sqrt((double)forward.X * forward.X +
            (double)forward.Y * forward.Y + (double)forward.Z * forward.Z);
        if (!double.IsFinite(length) || length == 0d)
            return Vector4.Zero;
        var x = forward.X / length;
        var y = forward.Y / length;
        var horizontalSquared = x * x + y * y;
        if (horizontalSquared > 0.0001f)
        {
            var inverse = 1d / Math.Sqrt(horizontalSquared);
            x *= inverse;
            y *= inverse;
        }
        return new((float)x, (float)y, 0f, (float)-(x * eye.X + y * eye.Y));
    }

    internal static bool TryGetBucket(in BoundingBox bounds, Vector3 forward,
        Vector4 horizontalPlane, out int bucket)
    {
        // 0x790650 chooses min when target >= eye (including equality).
        var corner = new Vector3(forward.X >= 0f ? bounds.Min.X : bounds.Max.X,
            forward.Y >= 0f ? bounds.Min.Y : bounds.Max.Y,
            forward.Z >= 0f ? bounds.Min.Z : bounds.Max.Z);
        var depth = (double)corner.X * horizontalPlane.X +
            (double)corner.Y * horizontalPlane.Y +
            (double)corner.Z * horizontalPlane.Z + horizontalPlane.W;
        bucket = 0;
        if (!double.IsFinite(depth))
            return false;
        if (depth > 0d)
        {
            // 0x792B8E..0x792BA9 stores float(depth * 0.03f), subtracts
            // 0.5, then FISTP. Startup 0x406D86 selects nearest rounding;
            // exact integer quotients retain the native ties-to-even quirk.
            var scaled = (float)(depth * 0.03f);
            var rounded = Math.Round((double)scaled - 0.5d, MidpointRounding.ToEven);
            if (rounded >= BucketCount)
                return false;
            bucket = (int)rounded;
        }
        return true;
    }

    internal static BoundingBox WorldBounds(in BoundingBox local, in Matrix4x4 model)
    {
        // 0x7BDF80/0x7BDF94 and 0x7B669A/0x7B66AB use transformed MOGI AABBs.
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (var corner = 0; corner < 8; corner++)
        {
            var point = Vector3.Transform(new((corner & 1) == 0 ? local.Min.X : local.Max.X,
                (corner & 2) == 0 ? local.Min.Y : local.Max.Y,
                (corner & 4) == 0 ? local.Min.Z : local.Max.Z), model);
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        return new(min, max);
    }
}
