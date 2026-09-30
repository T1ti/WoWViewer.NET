using System.Numerics;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>12340 portal projection (0x7A7210, 0x7A72A0, 0x7A85E0).
/// Reusable placement scratch; native clip-volume occlusion is a separate consumer.</summary>
internal sealed class Wrath335PortalProjection
{
    private readonly Vector3[] polygonA = new Vector3[32];
    private readonly Vector3[] polygonB = new Vector3[32];
    private readonly Vector4[] planes = new Vector4[5];
    private Matrix4x4 model;
    private Matrix4x4 viewProjection;

    public void Prepare(in Matrix4x4 modelMatrix, in Matrix4x4 worldViewProjection)
    {
        model = modelMatrix;
        viewProjection = worldViewProjection;
        var x = new Vector4(viewProjection.M11, viewProjection.M21, viewProjection.M31, viewProjection.M41);
        var y = new Vector4(viewProjection.M12, viewProjection.M22, viewProjection.M32, viewProjection.M42);
        var z = new Vector4(viewProjection.M13, viewProjection.M23, viewProjection.M33, viewProjection.M43);
        var w = new Vector4(viewProjection.M14, viewProjection.M24, viewProjection.M34, viewProjection.M44);
        // 0x983E70: top, bottom, left, right, far; near is plane 5.
        // 0x6BF370/0x6A9B40 establish the internal -1..1 to DX9 0..1 depth mapping.
        planes[0] = Normalize(w - y);
        planes[1] = Normalize(w + y);
        planes[2] = Normalize(w + x);
        planes[3] = Normalize(w - x);
        planes[4] = Normalize(w - z);
    }

    public WmoPortalRect Project(in WmoPortal portal, Vector3 eyeLocal,
        Vector3 offset = default, bool allowEyeContainment = true)
    {
        ProjectCore(portal, eyeLocal, [], out var rect, offset, allowEyeContainment);
        return rect;
    }

    // 0x7A87C7..0x7A87DC stores divided X/Y but undivided internal clip Z.
    // The render-view consumer needs the polygon, not just its bounds.
    public int ProjectPolygon(in WmoPortal portal, Vector3 eyeLocal,
        Span<Vector3> projected, out WmoPortalRect rect, Vector3 offset)
        => ProjectCore(portal, eyeLocal, projected, out rect, offset, false);

    private int ProjectCore(in WmoPortal portal, Vector3 eyeLocal,
        Span<Vector3> projected, out WmoPortalRect rect, Vector3 offset,
        bool allowEyeContainment)
    {
        rect = default;
        if (portal.Vertices is not { Length: >= 3 } vertices)
            return 0;
        if (allowEyeContainment && ContainsEye(portal, eyeLocal))
        {
            rect = WmoPortalRect.Full;
            return 0;
        }

        var count = Math.Min(vertices.Length, 12);
        var source = polygonA;
        var destination = polygonB;
        for (var i = 0; i < count; i++)
            source[i] = Vector3.Transform(vertices[i] + offset, model);
        foreach (var plane in planes)
        {
            count = ClipAgainstPlane(source.AsSpan(0, count), destination, plane);
            if (count < 3)
                return 0;
            (source, destination) = (destination, source);
        }
        if (!projected.IsEmpty && projected.Length < count)
            return 0;

        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);
        for (var i = 0; i < count; i++)
        {
            var clip = Vector4.Transform(new Vector4(source[i], 1f), viewProjection);
            var divisor = Math.Max(clip.W, 0.0001f);
            var p = new Vector2(clip.X / divisor, clip.Y / divisor);
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y))
                return 0;
            if (!projected.IsEmpty)
                // Undo DX9/DX11's 0..1 depth conversion to the client's
                // internal -1..1 projection (0x6BF370, 0x6A9B40).
                projected[i] = new(p, 2f * clip.Z - clip.W);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        rect = new(min.X, min.Y, max.X, max.Y);
        return count;
    }

    internal static double SignedDistance(in WmoPortal portal, Vector3 point) =>
        (double)portal.Normal.Y * point.Y + (double)portal.Normal.Z * point.Z +
        (double)portal.Normal.X * point.X + portal.Distance;

    internal static bool ContainsEye(in WmoPortal portal, Vector3 eye)
    {
        var distance = SignedDistance(portal, eye);
        return distance > -(double)0.01f && distance < (double)0.01f &&
            Wrath335WmoViewerQuery.PointInPortal(eye, portal.Vertices, portal.Normal);
    }

    // 0x7A72A0 classifies in extended precision, stores float distances, and
    // emits current -> next edges. On-plane vertices emit once, with no extra intersection.
    internal static int ClipAgainstPlane(ReadOnlySpan<Vector3> source,
        Span<Vector3> destination, Vector4 plane)
    {
        if (source.IsEmpty || source.Length > 32)
            return 0;
        Span<float> distances = stackalloc float[32];
        Span<byte> classes = stackalloc byte[32];
        for (var i = 0; i < source.Length; i++)
        {
            var v = source[i];
            var d = (double)v.X * plane.X + (double)v.Z * plane.Z + (double)v.Y * plane.Y + plane.W;
            distances[i] = (float)d;
            classes[i] = d > (double)0.0001f ? (byte)1 : d < -(double)0.0001f ? (byte)2 : (byte)0;
        }
        var count = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (classes[i] != 2)
            {
                if (count >= destination.Length)
                    return 0; // Malformed polygon exceeds the native 32-vertex scratch.
                destination[count++] = source[i];
            }
            var next = (i + 1) % source.Length;
            if (classes[i] == 0 || classes[next] == 0 || classes[i] == classes[next])
                continue;
            if (count >= destination.Length)
                return 0;
            var fraction = (double)distances[i] / ((double)distances[i] - distances[next]);
            var a = source[i];
            var b = source[next];
            destination[count++] = new(
                (float)(((double)b.X - a.X) * fraction + a.X),
                (float)(((double)b.Y - a.Y) * fraction + a.Y),
                (float)(((double)b.Z - a.Z) * fraction + a.Z));
        }
        return count;
    }

    private static Vector4 Normalize(Vector4 plane)
    {
        var length = Math.Sqrt((double)plane.X * plane.X + (double)plane.Y * plane.Y + (double)plane.Z * plane.Z);
        return length > 0 ? new((float)(plane.X / length), (float)(plane.Y / length),
            (float)(plane.Z / length), (float)(plane.W / length)) : plane;
    }
}
