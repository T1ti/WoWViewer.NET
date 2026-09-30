using System.Numerics;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>12340 CPU terrain horizon: 0x78F6A0/0x78F900 producers and 0x78FDC0 box test.</summary>
internal sealed class Wrath335TerrainClipBuffer
{
    internal const int ColumnCount = 384;
    private readonly float[] heights = new float[ColumnCount];
    private readonly bool[] protectedColumns = new bool[ColumnCount];
    private Matrix4x4 projection;
    public bool Active { get; private set; }

    public void Clear()
    {
        // 0x79A951..0x79A96D clears flags and fills C9742400 (-1000000).
        Array.Fill(heights, -1_000_000f);
        Array.Clear(protectedColumns);
        Active = false;
    }

    public void Begin(Vector3 eye, Vector3 forward, in Matrix4x4 cameraProjection, bool enabled = true)
    {
        var full = Wrath335ClipVolumes.NormalizeDirection(forward);
        var horizontal = Wrath335ExteriorGroupOrder.HorizontalDepthPlane(eye, full);
        var right = new Vector3(horizontal.Y, -horizontal.X, 0f);
        var direction = new Vector3(horizontal.X, horizontal.Y, 0f);
        // 0x795B82..0x795C58: flattened SG-compatible view, then the captured projection.
        var view = new Matrix4x4(right.X, 0f, direction.X, 0f,
            right.Y, 0f, direction.Y, 0f, 0f, 1f, 0f, 0f,
            -Vector3.Dot(right, eye), -eye.Z, horizontal.W, 1f);
        BeginProjected(view * cameraProjection, full.Z, enabled);
    }

    internal void BeginProjected(in Matrix4x4 worldProjection, float normalizedPitch, bool enabled = true)
    {
        Clear();
        projection = worldProjection;
        Active = enabled && normalizedPitch >= -0.9f && normalizedPitch <= 0.9f;
    }

    public bool ContainsBox(in BoundingBox bounds, byte flags = 1)
    {
        if (!Active)
            return false;
        var minX = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = -float.MaxValue;
        for (var corner = 0; corner < 8; corner++)
        {
            var point = Transform(new((corner & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                (corner & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                (corner & 4) == 0 ? bounds.Min.Z : bounds.Max.Z));
            // Argument 1 at both WMO sites does NOT bypass this 50-unit gate; bit 8 does.
            if ((flags & 8) == 0 && point.Z < 50f)
                return false;
            var x = point.X / (double)point.Z;
            var y = point.Y / (double)point.Z;
            if (!double.IsFinite(x) || !double.IsFinite(y))
                return false;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            if (y > maxY)
                maxY = (float)y;
        }
        if (!TryColumn(minX, out var first) || !TryColumn(maxX, out var last))
            return false;
        last++; // 0x78FF38: consumer extends the right edge by one column.
        if (first >= ColumnCount || last < 0)
            return false;
        first = Math.Max(first, 0);
        last = Math.Min(last, ColumnCount - 1);
        for (var column = first; column <= last; column++)
            if (maxY > heights[column])
                return false;
        return true;
    }

    public void UpdateTerrainEdges(ReadOnlySpan<ADTVertex> vertices, in Matrix4x4 model,
        Vector3 forward, bool hasHoles)
    {
        if (!Active || vertices.Length < 145)
            return;
        Span<Vector3> edge = stackalloc Vector3[9];
        // 0x7CFB2D/0x7CFB8C selects the two nine-vertex camera-facing outer edges.
        var row = forward.X < 0f ? 136 : 0;
        for (var index = 0; index < edge.Length; index++)
            edge[index] = Vector3.Transform(vertices[row + index].Position, model);
        UpdatePolyline(edge, hasHoles, false);
        var column = forward.Y < 0f ? 8 : 0;
        for (var index = 0; index < edge.Length; index++)
            edge[index] = Vector3.Transform(vertices[column + 17 * index].Position, model);
        UpdatePolyline(edge, hasHoles, false);
    }

    // 0x7CC880 -> 0x78F900: protected horizon lines survive later terrain-hole erasure.
    internal void UpdateProtectedLine(Vector3 a, Vector3 b) => UpdatePolyline([a, b], false, true);

    private void UpdatePolyline(ReadOnlySpan<Vector3> points, bool hasHoles, bool protect)
    {
        if (!Active || points.Length < 2)
            return;
        var previous = Project(points[0]);
        for (var index = 1; index < points.Length; index++)
        {
            var current = Project(points[index]);
            // Hole erasure deliberately omits the endpoint-depth gate (0x78F775).
            if ((hasHoles || (previous.Z >= 0.027777778f && current.Z >= 0.027777778f)) &&
                TryColumn(previous.X, out var first) && TryColumn(current.X, out var last))
            {
                if (first > last)
                    (first, last) = (last, first);
                first = Math.Max(first, 0);
                last = Math.Min(last, ColumnCount - 1);
                var height = MathF.Min(previous.Y, current.Y);
                for (var column = first; column <= last; column++)
                {
                    if (hasHoles)
                    {
                        if (!protectedColumns[column])
                            heights[column] = -1_000_001f;
                    }
                    else
                    {
                        if (protect)
                            protectedColumns[column] = true;
                        if (height > heights[column])
                            heights[column] = height;
                    }
                }
            }
            previous = current;
        }
    }

    private Vector3 Project(Vector3 point)
    {
        point = Transform(point);
        var inverse = 1d / point.Z;
        return new((float)(point.X * inverse), (float)(point.Y * inverse), point.Z);
    }

    private Vector3 Transform(Vector3 point) => new(
        (float)((double)point.X * projection.M11 + (double)point.Y * projection.M21 +
            (double)point.Z * projection.M31 + projection.M41),
        (float)((double)point.X * projection.M12 + (double)point.Y * projection.M22 +
            (double)point.Z * projection.M32 + projection.M42),
        (float)((double)point.X * projection.M13 + (double)point.Y * projection.M23 +
            (double)point.Z * projection.M33 + projection.M43));

    internal static bool TryColumn(double x, out int column)
    {
        // 0x78F78C/0x78FF06 stages float(x*64), subtracts 0.5, then nearest-even FISTP.
        var value = Math.Round((double)(float)(x * 64d) - 0.5d, MidpointRounding.ToEven);
        column = 0;
        if (!double.IsFinite(value) || value < int.MinValue + ColumnCount || value > int.MaxValue - ColumnCount)
            return false;
        column = (int)value + 192;
        return true;
    }
}
