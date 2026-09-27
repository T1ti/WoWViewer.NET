using System.Numerics;
using System.Runtime.CompilerServices;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Editing;

/// <summary>Shared, weakly cached local-space bounds for small contiguous mesh blocks.</summary>
internal sealed class ScreenSelectionMesh
{
    private const int IndicesPerBlock = 64 * 3;
    private static readonly ConditionalWeakTable<Vector3[], ConditionalWeakTable<ushort[], ScreenSelectionMesh>> Cache = new();
    private readonly BoundingBox[] _blocks;
    private readonly Vector3[] _vertices;
    private readonly ushort[] _indices;

    private ScreenSelectionMesh(Vector3[] vertices, ushort[] indices)
    {
        _vertices = vertices;
        _indices = indices;
        _blocks = new BoundingBox[(indices.Length + IndicesPerBlock - 1) / IndicesPerBlock];
        for (var block = 0; block < _blocks.Length; block++)
        {
            var min = new Vector3(float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity);
            var end = Math.Min(indices.Length, (block + 1) * IndicesPerBlock);
            for (var index = block * IndicesPerBlock; index + 2 < end; index += 3)
            {
                if (!ValidTriangle(index)) continue;
                for (var corner = 0; corner < 3; corner++)
                {
                    var vertex = vertices[indices[index + corner]];
                    min = Vector3.Min(min, vertex);
                    max = Vector3.Max(max, vertex);
                }
            }
            _blocks[block] = new(min, max);
        }
    }

    public static ScreenSelectionMesh Get(Vector3[] vertices, ushort[] indices)
    {
        var meshes = Cache.GetValue(vertices, static _ => new());
        return meshes.TryGetValue(indices, out var mesh) ? mesh : Create(meshes, vertices, indices);
    }

    // Keep the capturing factory off the warmed query path.
    private static ScreenSelectionMesh Create(ConditionalWeakTable<ushort[], ScreenSelectionMesh> meshes,
        Vector3[] vertices, ushort[] indices) => meshes.GetValue(indices, key => new(vertices, key));

    public bool Intersects(in ScreenSelectionVolume volume)
    {
        for (var block = 0; block < _blocks.Length; block++)
        {
            if (!volume.Intersects(_blocks[block])) continue;
            var end = Math.Min(_indices.Length, (block + 1) * IndicesPerBlock);
            for (var index = block * IndicesPerBlock; index + 2 < end; index += 3)
                if (ValidTriangle(index) && volume.IntersectsTriangle(
                    _vertices[_indices[index]], _vertices[_indices[index + 1]], _vertices[_indices[index + 2]]))
                    return true;
        }
        return false;
    }

    private bool ValidTriangle(int index)
    {
        for (var corner = 0; corner < 3; corner++)
        {
            var vertexIndex = _indices[index + corner];
            if (vertexIndex >= _vertices.Length) return false;
            var vertex = _vertices[vertexIndex];
            if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y) || !float.IsFinite(vertex.Z)) return false;
        }
        return true;
    }
}
