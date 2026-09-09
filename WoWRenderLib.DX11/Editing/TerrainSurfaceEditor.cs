using System.Numerics;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Editing;

/// <summary>Edits welded world-space samples from immutable passes, including tile seams.</summary>
public static class TerrainSurfaceEditor
{
    public sealed record Surface(ADTVertex[] Vertices, int[] Indices, Matrix4x4 Model);
    // ADT duplicates have small floating point differences at shared edges.
    private static (int X, int Y) Key(Vector3 p) =>
        ((int)MathF.Round(p.X * 64), (int)MathF.Round(p.Y * 64));

    public static bool Apply(IReadOnlyList<Surface> surfaces, Vector3 center,
        BrushInput brush, TerrainBrushInput input, float deltaTime)
    {
        var points = new Dictionary<(int, int), Vector3>();
        foreach (var surface in surfaces)
            foreach (var vertex in surface.Vertices)
            {
                var p = Vector3.Transform(vertex.Position, surface.Model);
                points.TryAdd(Key(p), p);
            }

        var tool = TerrainBrushTools.Get(input.ToolMode);
        var changed = new HashSet<(int, int)>();
        var neighborhood = MathF.Min(brush.Radius, 12f);
        for (var pass = 0; pass < tool.GetPassCount(input.SmoothIterations); pass++)
        {
            // Spatial buckets avoid scanning an entire tile for every brush sample.
            var buckets = new Dictionary<(int, int), List<Vector3>>();
            foreach (var p in points.Values)
            {
                var cell = ((int)MathF.Floor(p.X / 12), (int)MathF.Floor(p.Y / 12));
                if (!buckets.TryGetValue(cell, out var list)) buckets[cell] = list = [];
                list.Add(p);
            }
            var updates = new Dictionary<(int, int), Vector3>();
            foreach (var (key, p) in points)
            {
                var influence = BrushMath.CalculateInfluence(p.X - center.X, p.Y - center.Y, brush);
                if (influence <= 0) continue;
                var average = p.Z;
                if (input.ToolMode == TerrainBrushMode.Smooth)
                {
                    double sum = 0;
                    var count = 0;
                    var cx = (int)MathF.Floor(p.X / 12);
                    var cy = (int)MathF.Floor(p.Y / 12);
                    for (var x = cx - 1; x <= cx + 1; x++)
                        for (var y = cy - 1; y <= cy + 1; y++)
                            if (buckets.TryGetValue((x, y), out var candidates))
                                foreach (var q in candidates)
                                    if (Vector2.DistanceSquared(new(p.X, p.Y), new(q.X, q.Y)) <= neighborhood * neighborhood)
                                    { sum += q.Z; count++; }
                    if (count > 0) average = (float)(sum / count);
                }
                var height = tool.Apply(new TerrainBrushSample([], 0, p.Z,
                    Math.Clamp(input.Speed, 0.1f, 50f) * deltaTime * influence,
                    brush.Radius, input.FlattenHeight, input.Action, average));
                if (MathF.Abs(height - p.Z) < 0.0001f) continue;
                updates[key] = new(p.X, p.Y, height);
                changed.Add(key);
            }
            foreach (var (key, p) in updates) points[key] = p;
        }
        if (changed.Count == 0) return false;
        foreach (var surface in surfaces)
        {
            Matrix4x4.Invert(surface.Model, out var inverse);
            for (var i = 0; i < surface.Vertices.Length; i++)
            {
                var p = Vector3.Transform(surface.Vertices[i].Position, surface.Model);
                var key = Key(p);
                if (changed.Contains(key))
                {
                    p.Z = points[key].Z;
                    surface.Vertices[i].Position = Vector3.Transform(p, inverse);
                }
            }
        }
        RebuildNormals(surfaces, changed);
        return true;
    }

    private static void RebuildNormals(IReadOnlyList<Surface> surfaces, HashSet<(int, int)> changed)
    {
        var affected = new HashSet<(int, int)>();
        var sums = new Dictionary<(int, int), Vector3>();
        foreach (var surface in surfaces)
            for (var i = 0; i + 2 < surface.Indices.Length; i += 3)
            {
                var a = surface.Indices[i]; var b = surface.Indices[i + 1]; var c = surface.Indices[i + 2];
                if ((uint)a >= surface.Vertices.Length || (uint)b >= surface.Vertices.Length || (uint)c >= surface.Vertices.Length)
                    continue;
                var p = Vector3.Transform(surface.Vertices[a].Position, surface.Model);
                var q = Vector3.Transform(surface.Vertices[b].Position, surface.Model);
                var r = Vector3.Transform(surface.Vertices[c].Position, surface.Model);
                var ka = Key(p); var kb = Key(q); var kc = Key(r);
                var normal = Vector3.Cross(q - p, r - p);
                if (normal.Z < 0) normal = -normal;
                sums[ka] = sums.GetValueOrDefault(ka) + normal;
                sums[kb] = sums.GetValueOrDefault(kb) + normal;
                sums[kc] = sums.GetValueOrDefault(kc) + normal;
                if (changed.Contains(ka) || changed.Contains(kb) || changed.Contains(kc))
                { affected.Add(ka); affected.Add(kb); affected.Add(kc); }
            }
        foreach (var surface in surfaces)
            for (var i = 0; i < surface.Vertices.Length; i++)
            {
                var key = Key(Vector3.Transform(surface.Vertices[i].Position, surface.Model));
                if (affected.Contains(key) && sums.TryGetValue(key, out var normal) && normal.LengthSquared() > 1e-12f)
                    surface.Vertices[i].Normal = Vector3.Normalize(
                        Vector3.TransformNormal(normal, Matrix4x4.Transpose(surface.Model)));
            }
    }
}
