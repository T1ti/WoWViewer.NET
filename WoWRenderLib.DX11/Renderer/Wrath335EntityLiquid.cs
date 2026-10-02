using System.Numerics;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

internal readonly record struct Wrath335EntityLiquidLink(bool Terrain, bool Available,
    ReadOnlyMemory<WorldLiquidQueryGrid> Grids, Matrix4x4 Placement)
{
    internal static Wrath335EntityLiquidLink ForTerrain(WorldLiquidQueryGrid[] grids) =>
        new(true, true, grids, Matrix4x4.Identity);
}

/// <summary>12340 static-entity +0x7C/+0x80 query state, in linked-node order.</summary>
internal struct Wrath335EntityLiquidCache
{
    internal uint Flags;
    internal float Height;
    internal float SunDiffuseMultiplier;
    internal readonly bool Cached => (Flags & 0x80) != 0;

    // 0x7B7011: re-enabling a doodad clears both liquid bits and validity.
    internal void Invalidate() => Flags &= ~0xE0u;

    internal void Query(Vector3 origin, BoundingBox worldBounds,
        ReadOnlySpan<Wrath335EntityLiquidLink> links)
    {
        if (Cached) return;
        // 0x7C2404..0x7C240C: clear the side bits, retain unrelated state.
        Flags = (Flags & ~0x60u) | 0x80;
        var sample = new Vector3(origin.X, origin.Y, worldBounds.Min.Z);
        foreach (var link in links)
        {
            if (link.Terrain)
            {
                // 0x7C2550: the first terrain node runs the global terrain
                // query, without a floor check, and ends traversal even on miss.
                Flags |= 0x80;
                if (TryHeight(link.Grids.Span, sample, out var height))
                {
                    Flags &= ~8u;
                    SunDiffuseMultiplier = 1;
                    Hit(height, worldBounds.Max.Z);
                }
                return;
            }
            if (!link.Available)
            {
                Flags &= ~0x80u; // 0x7C246B: retry unless a later hit/terrain completes.
                continue;
            }
            if (!Matrix4x4.Invert(link.Placement, out var inverse)) continue;
            var local = Vector3.Transform(sample, inverse);
            if (!TryHeight(link.Grids.Span, local, out var localHeight)) continue;
            var surface = Vector3.Transform(new(local.X, local.Y, localHeight), link.Placement);
            Hit(surface.Z, worldBounds.Max.Z);
            return;
        }
    }

    private void Hit(float height, float maximumZ)
    {
        Height = height;
        Flags = (Flags & ~0x40u) | 0xA0;
        if (height <= maximumZ) Flags |= 0x40; // Inclusive at 0x7C2508 / 0x7C2599.
    }

    private static bool TryHeight(ReadOnlySpan<WorldLiquidQueryGrid> grids,
        Vector3 sample, out float height)
    {
        foreach (var grid in grids)
            if (Wrath335LiquidQuery.TryHeight(grid, sample, out height)) return true;
        height = 0;
        return false;
    }

    internal readonly Wrath335M2EntityWaterLighting Lighting(uint retainedFlags = 0,
        Vector4 retainedPlane = default) =>
        Wrath335M2WaterQueues.EntityLighting(Flags, Height, retainedFlags, retainedPlane);
}

internal readonly record struct Wrath335M2MeshWater(Wrath335M2WaterSelection Selection, Vector4 WorldPlane)
{
    internal bool Includes(Wrath335M2QueueMask side) =>
        side == Wrath335M2QueueMask.AboveWater ? Selection.Above : Selection.Below;

    // 0x81FB10: element flag 2 enables the plane; M2 pass 2 negates all four components.
    internal Vector4 ClipPlane(Wrath335M2QueueMask side) => Selection.Above && Selection.Below
        ? (side == Wrath335M2QueueMask.BelowWater ? -WorldPlane : WorldPlane) : default;
}

// 0x7834E5..0x7834F3 copies the last render query during update. 0x79A888
// refreshes the viewer later, before the outer pass-order branch at 0x4F915B.
internal readonly record struct Wrath335LiquidSceneState(Wrath335ViewerLiquid Viewer, ushort SceneLiquidTypeId)
{
    internal Wrath335LiquidSceneState Advance(Wrath335ViewerLiquid viewer) => new(viewer, Viewer.TypeId);
}
