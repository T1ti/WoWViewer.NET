using System.Numerics;
using System.Runtime.InteropServices;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

internal readonly record struct Wrath335ClipVolume(int FirstPlane, int PlaneCount);

/// <summary>12340 map-specific CPU occluder volumes (0x7CD850/0x7CD4E0).
/// Separate from the terrain clip buffer and the GPU portal view-volume pass.</summary>
internal sealed class Wrath335ClipVolumes
{
    private readonly List<Vector4> planes = [];
    private readonly List<Wrath335ClipVolume> volumes = [];
    private readonly Vector4[] cameraPlanes = new Vector4[6];
    private readonly Vector3[] clipped = new Vector3[32];
    public ReadOnlySpan<Vector4> Planes => CollectionsMarshal.AsSpan(planes);
    public ReadOnlySpan<Wrath335ClipVolume> Volumes => CollectionsMarshal.AsSpan(volumes);

    public void Clear()
    {
        planes.Clear();
        volumes.Clear();
    }

    public void Prepare(int mapId, Vector3 eye, Vector3 cameraForward,
        in Matrix4x4 viewProjection, float exteriorDistance, bool shadowOnly = false) =>
        Prepare(Wrath335StaticOccluders.Entries, mapId, eye, cameraForward,
            viewProjection, exteriorDistance, shadowOnly);

    internal void Prepare(ReadOnlySpan<Wrath335StaticOccluder> sources, int mapId,
        Vector3 eye, Vector3 cameraForward, in Matrix4x4 viewProjection,
        float exteriorDistance, bool shadowOnly = false)
    {
        Clear();
        SetCameraPlanes(viewProjection);
        var forward = NormalizeDirection(cameraForward);
        foreach (var source in sources)
        {
            // 0x7CD8B6/0x7CD8C1: map equality, optional bit-2 subset, full base frustum.
            if (source.MapId != mapId || (shadowOnly && (source.Flags & 2) == 0) ||
                !IntersectsCamera(source.Bounds))
                continue;
            AddPolygon(eye, source.Vertices, forward, exteriorDistance, (source.Flags & 1) == 0);
        }
    }

    internal void AddPolygon(Vector3 eye, ReadOnlySpan<Vector3> polygon, Vector3 forward,
        float exteriorDistance, bool clipAtExteriorDistance)
    {
        if (polygon.Length < 3 || polygon.Length > 16)
            return;
        var first = planes.Count;
        var source = polygon;
        var clippedBranch = clipAtExteriorDistance && exteriorDistance > 0.000001d;
        if (clippedBranch)
        {
            // 0x7CD55F..0x7CD621 keeps the positive side beyond the exterior distance.
            // It uses the full normalized direction, not the horizontal bucket plane.
            var distance = -((forward.Z * (double)exteriorDistance + eye.Z) * forward.Z +
                forward.Y * (forward.Y * (double)exteriorDistance + eye.Y) +
                forward.X * (forward.X * (double)exteriorDistance + eye.X));
            var count = Wrath335PortalProjection.ClipAgainstPlane(polygon, clipped, new(forward, (float)distance));
            if (count < 3)
                return;
            source = clipped.AsSpan(0, count);
        }
        for (var index = 0; index < source.Length; index++)
        {
            var a = source[index];
            var b = source[(index + 1) % source.Length];
            if (clippedBranch)
            {
                Cross(a, b, eye, out var x, out var y, out var z);
                var squared = y * y + z * z + x * x;
                // 0x7CD6CB excludes tiny side planes only on the distance-clipped path.
                if (squared <= (double)0.0001f)
                    continue;
                var inverse = 1d / Math.Sqrt(squared);
                AddPlane(a, x * inverse, (float)y * inverse, (float)z * inverse);
            }
            else
                planes.Add(Facet(a, b, eye)); // Native unconditional normalization, including degeneracy.
        }
        if (planes.Count == first)
            return;
        // The base facet comes from the ORIGINAL first three vertices, even after clipping.
        var cap = Facet(polygon[0], polygon[1], polygon[2]);
        planes.Add(cap);
        if (Distance(cap, eye) < 0d)
        {
            var all = CollectionsMarshal.AsSpan(planes);
            for (var index = first; index < all.Length; index++)
                all[index] = -all[index];
        }
        volumes.Add(new(first, planes.Count - first));
    }

    /// <summary>0x7CCFA0: all points must be nonpositive for every plane of ONE volume.</summary>
    public bool ContainsPolygon(ReadOnlySpan<Vector3> points)
    {
        foreach (var volume in Volumes)
        {
            var inside = true;
            foreach (var plane in Planes.Slice(volume.FirstPlane, volume.PlaneCount))
            {
                foreach (var point in points)
                    if (Distance(plane, point) > 0d)
                    {
                        inside = false;
                        break;
                    }
                if (!inside)
                    break;
            }
            if (inside)
                return true;
        }
        return false;
    }

    /// <summary>0x7CCE00: the sphere must fit behind every normalized plane of one volume.</summary>
    public bool ContainsSphere(Vector4 sphere)
    {
        foreach (var volume in Volumes)
        {
            var inside = true;
            foreach (var plane in Planes.Slice(volume.FirstPlane, volume.PlaneCount))
                if (Distance(plane, new(sphere.X, sphere.Y, sphere.Z)) + sphere.W > 0d)
                {
                    inside = false;
                    break;
                }
            if (inside)
                return true;
        }
        return false;
    }

    internal static Vector4 GroupSphere(in BoundingBox rootBounds, in Matrix4x4 model)
    {
        // 0x7AE6D2..0x7AE713 stores the MOGI midpoint and half-diagonal radius.
        // 0x7BDF5D/0x7BDF6E transforms the center in-place; radius is not rebuilt
        // from the rotated world AABB or scaled by this source writer.
        var center = new Vector3((float)(((double)rootBounds.Max.X + rootBounds.Min.X) * 0.5d),
            (float)(((double)rootBounds.Max.Y + rootBounds.Min.Y) * 0.5d),
            (float)(((double)rootBounds.Max.Z + rootBounds.Min.Z) * 0.5d));
        var x = (double)rootBounds.Max.X - center.X;
        var y = (double)rootBounds.Max.Y - center.Y;
        var z = (double)rootBounds.Max.Z - center.Z;
        return new(Vector3.Transform(center, model), (float)Math.Sqrt(x * x + (y * y + z * z)));
    }

    private void AddPlane(Vector3 point, double x, double y, double z) =>
        planes.Add(new((float)x, (float)y, (float)z,
            (float)-(y * point.Y + z * point.Z + x * point.X)));

    internal static Vector4 Facet(Vector3 a, Vector3 b, Vector3 c)
    {
        // 0x7912F9..0x791320 stores the cross product before normalizing.
        Cross(a, b, c, out var x, out var y, out var z);
        x = (float)x;
        y = (float)y;
        z = (float)z;
        var inverse = 1d / Math.Sqrt(z * z + y * y + x * x);
        x *= inverse;
        y *= inverse;
        z *= inverse;
        return new((float)x, (float)y, (float)z, (float)-(z * a.Z + x * a.X + y * a.Y));
    }

    private static void Cross(Vector3 a, Vector3 b, Vector3 c, out double x, out double y, out double z)
    {
        var cx = (double)c.X - a.X;
        var cy = (double)c.Y - a.Y;
        var cz = (double)c.Z - a.Z;
        var bx = (double)b.X - a.X;
        var by = (double)b.Y - a.Y;
        var bz = (double)b.Z - a.Z;
        x = by * cz - bz * cy;
        y = bz * cx - cz * bx;
        z = cy * bx - cx * by;
    }

    internal static Vector3 NormalizeDirection(Vector3 direction)
    {
        var squared = (double)direction.X * direction.X +
            (double)direction.Y * direction.Y + (double)direction.Z * direction.Z;
        return squared > 0d && double.IsFinite(squared)
            ? direction / (float)Math.Sqrt(squared) : direction;
    }

    private static double Distance(Vector4 plane, Vector3 point) =>
        (double)point.Z * plane.Z + (double)point.X * plane.X + (double)point.Y * plane.Y + plane.W;

    private void SetCameraPlanes(in Matrix4x4 matrix)
    {
        var x = new Vector4(matrix.M11, matrix.M21, matrix.M31, matrix.M41);
        var y = new Vector4(matrix.M12, matrix.M22, matrix.M32, matrix.M42);
        var z = new Vector4(matrix.M13, matrix.M23, matrix.M33, matrix.M43);
        var w = new Vector4(matrix.M14, matrix.M24, matrix.M34, matrix.M44);
        cameraPlanes[0] = w - y;
        cameraPlanes[1] = w + y;
        cameraPlanes[2] = w + x;
        cameraPlanes[3] = w - x;
        cameraPlanes[4] = w - z;
        cameraPlanes[5] = z;
        for (var index = 0; index < cameraPlanes.Length; index++)
        {
            var plane = cameraPlanes[index];
            var length = Math.Sqrt((double)plane.X * plane.X +
                (double)plane.Y * plane.Y + (double)plane.Z * plane.Z);
            if (length > 0d)
                cameraPlanes[index] = new((float)(plane.X / length), (float)(plane.Y / length),
                    (float)(plane.Z / length), (float)(plane.W / length));
        }
    }

    private bool IntersectsCamera(in BoundingBox bounds)
    {
        foreach (var plane in cameraPlanes)
        {
            var corner = new Vector3(plane.X < 0f ? bounds.Min.X : bounds.Max.X,
                plane.Y < 0f ? bounds.Min.Y : bounds.Max.Y,
                plane.Z < 0f ? bounds.Min.Z : bounds.Max.Z);
            if (Distance(plane, corner) < -(double)0.019444443f) // 0x983A35 native tolerance.
                return false;
        }
        return true;
    }
}
