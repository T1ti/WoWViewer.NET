using System.Numerics;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

internal readonly record struct Wrath335ViewerLiquid(ushort TypeId, float Depth)
{
    internal Wrath335M2WaterPassOrder PassOrder => Wrath335M2WaterQueues.PassOrder(TypeId);
}

/// <summary>12340 liquid grid predicates and bilinear query, before any draw clipping.</summary>
internal static class Wrath335LiquidQuery
{
    private const float TerrainInverseStep = .23999999463558197f;
    private const float WmoInverseStep = .24000000953674316f;
    private const float TerrainOrigin = 17066.666015625f;
    private const float Epsilon = .009999999776482582f;

    internal static bool TryQuery(ReadOnlySpan<WorldLiquidQueryGrid> grids, Vector3 point,
        out Wrath335ViewerLiquid liquid, float terrainHeight = -10000, bool checkTerrainHeight = false)
    {
        foreach (var grid in grids)
            if (TryHeight(grid, point, out var height) &&
                (grid.IsWmo || !checkTerrainHeight || (double)point.Z + Epsilon > terrainHeight))
            {
                liquid = new(grid.LiquidTypeId, height - point.Z);
                return true; // Native layer/list order, not nearest surface.
            }
        liquid = default;
        return false;
    }

    internal static bool TryHeight(WorldLiquidQueryGrid grid, Vector3 point, out float height)
    {
        height = 0;
        if (grid.LiquidTypeId == 0 || !float.IsFinite(point.X) ||
            !float.IsFinite(point.Y) || !float.IsFinite(point.Z)) return false;
        if (grid.IsWmo && (grid.Width == 0 || grid.Height == 0))
        {
            height = float.MaxValue; // 0x7C84E6: liquid occupies the whole group.
            return true;
        }
        int x, y;
        float u, v;
        if (grid.IsWmo)
        {
            var gx = ((double)point.X - grid.Origin.X) * WmoInverseStep;
            var gy = (float)(((double)point.Y - grid.Origin.Y) * WmoInverseStep);
            x = (int)Math.Floor(gx); y = (int)Math.Floor(gy);
            u = (float)((float)gx - (double)x); v = (float)(gy - (double)y);
        }
        else
        {
            // 0x7A0829..0x7A0868: axes swap/decrease, float coordinate spills,
            // then FISTP(coord-.5) with round-to-nearest (ties to even).
            var gx = (float)(((double)TerrainOrigin - point.Y) * TerrainInverseStep);
            var gy = (float)(((double)TerrainOrigin - point.X) * TerrainInverseStep);
            var ix = (int)Math.Round((double)gx - .5);
            var iy = (int)Math.Round((double)gy - .5);
            var chunkX = (int)Math.Round(((double)TerrainOrigin - grid.Origin.Y) * TerrainInverseStep / 8);
            var chunkY = (int)Math.Round(((double)TerrainOrigin - grid.Origin.X) * TerrainInverseStep / 8);
            if ((ix >> 3) != chunkX || (iy >> 3) != chunkY) return false;
            x = (ix & 7) - grid.XOffset; y = (iy & 7) - grid.YOffset;
            u = (float)(gx - (double)ix); v = (float)(gy - (double)iy);
        }
        if ((uint)x >= (uint)grid.Width || (uint)y >= (uint)grid.Height) return false;
        var tile = y * grid.Width + x;
        if (grid.IsWmo)
        {
            if ((uint)tile >= (uint)grid.Tiles.Length || (grid.Tiles[tile] & 15) == 15) return false;
        }
        else if (grid.Tiles.Length != 0 &&
            ((tile >> 3) >= grid.Tiles.Length || (grid.Tiles[tile >> 3] & (1 << (tile & 7))) == 0))
            return false;
        var top = y * grid.VertexStride + x;
        var bottom = top + grid.VertexStride;
        if (grid.VertexStride <= x + 1 || (uint)(bottom + 1) >= (uint)grid.Heights.Length) return false;
        var a = grid.Heights[top] + ((double)grid.Heights[top + 1] - grid.Heights[top]) * u;
        var b = grid.Heights[bottom] + ((double)grid.Heights[bottom + 1] - grid.Heights[bottom]) * u;
        var surface = a + (b - a) * v;
        if (!double.IsFinite(surface)) return false;
        height = (float)surface;
        // WMO compares the extended interpolation before the float store;
        // terrain GetHeight stores first and its caller applies the tolerance.
        return grid.IsWmo ? surface + ((grid.TypeFlags & 4) != 0 ? Epsilon : 0) > point.Z
            : (double)height + Epsilon > point.Z;
    }
}
