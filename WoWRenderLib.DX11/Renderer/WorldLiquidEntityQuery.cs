using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// Retains liquid classifications per placement. Loaded terrain generations and
/// MODR owner links are independent of liquid draw visibility and editor group masks.
/// Spatial links to neighboring WMO placements remain a separate scene-link port.
/// </summary>
internal sealed class WorldLiquidEntityQuery
{
    private ConditionalWeakTable<M2Container, Entry> entries = new();
    private ConditionalWeakTable<M2InstancePacket, PacketWater> packets = new();
    private readonly List<TerrainSource> terrainSources = [];
    private readonly List<WorldLiquidQueryGrid> terrainGridScratch = [];
    private readonly List<Wrath335EntityLiquidLink> links = [];
    private WorldLiquidQueryGrid[] terrainGrids = [];
    private ulong terrainGeneration;

    private readonly record struct TerrainSource(ADTContainer Tile, WorldLiquidQueryGrid[]? Grids);
    private sealed class PacketWater { internal Wrath335M2MeshWater[] Values = []; }
    private sealed class Entry
    {
        internal bool Initialized;
        internal Matrix4x4 World, OwnerWorld;
        internal Submesh[]? Model;
        internal WMOContainer? Owner;
        internal WorldModelGroupBatches[]? Groups;
        internal int DoodadIndex;
        internal Wrath335M2Bounds Bounds;
        internal ulong TerrainGeneration;
        internal Wrath335EntityLiquidCache Cache;
    }

    internal void Clear()
    {
        entries = new();
        packets = new();
        terrainSources.Clear();
        terrainGrids = [];
        terrainGeneration++;
    }

    internal void BeginFrame(IReadOnlyList<ADTContainer> terrain)
    {
        var changed = false;
        var sourceIndex = 0;
        for (var index = 0; index < terrain.Count; index++)
        {
            var tile = terrain[index];
            if (!tile.IsLoaded) continue;
            var source = new TerrainSource(tile, tile.Terrain.worldLiquid.queryGrids);
            if (sourceIndex >= terrainSources.Count)
            {
                terrainSources.Add(source);
                changed = true;
            }
            else if (terrainSources[sourceIndex] != source)
            {
                terrainSources[sourceIndex] = source;
                changed = true;
            }
            sourceIndex++;
        }
        if (sourceIndex < terrainSources.Count)
        {
            terrainSources.RemoveRange(sourceIndex, terrainSources.Count - sourceIndex);
            changed = true;
        }
        if (!changed) return;
        terrainGeneration++;
        terrainGridScratch.Clear();
        foreach (var source in terrainSources)
            if (source.Grids != null) terrainGridScratch.AddRange(source.Grids);
        terrainGrids = terrainGridScratch.ToArray(); // Once per loaded CPU grid generation.
    }

    internal Wrath335M2MeshWater[]? Prepare(M2InstancePacket packet, in ParsedDoodadBatch model,
        IReadOnlyList<M2AnimationDrawGroup> groups, in Matrix4x4 view,
        bool sceneBelowSide, bool splitCrossingModels = true)
    {
        if (model.wrath335Bounds is not { } bounds) return null;
        var water = packets.GetValue(packet, static _ => new());
        if (water.Values.Length < packet.Instances.Count)
            Array.Resize(ref water.Values, Math.Max(packet.Instances.Count, water.Values.Length * 2));
        foreach (var group in groups)
            foreach (var index in group.Indices)
            {
                var instance = packet.Instances[index];
                var world = packet.WorldMatrices[index];
                var owner = instance.ParentWMO;
                var ownerModel = owner?.GetWMO() ?? default;
                var ownerWorld = owner?.GetModelMatrix() ?? Matrix4x4.Identity;
                var entry = entries.GetValue(instance, static _ => new());
                if (!entry.Initialized || entry.World != world || entry.OwnerWorld != ownerWorld ||
                    !ReferenceEquals(entry.Model, model.submeshes) || !ReferenceEquals(entry.Owner, owner) ||
                    !ReferenceEquals(entry.Groups, ownerModel.groupBatches) ||
                    entry.Bounds != bounds || entry.DoodadIndex != instance.WmoDoodadIndex || entry.TerrainGeneration != terrainGeneration)
                {
                    // Editor transform/resource changes invalidate its retained query.
                    // Native explicit re-enable uses the same mask at 0x7B7011.
                    entry.Cache.Invalidate();
                    entry.Initialized = true;
                    entry.World = world;
                    entry.OwnerWorld = ownerWorld;
                    entry.Model = model.submeshes;
                    entry.Owner = owner;
                    entry.Groups = ownerModel.groupBatches;
                    entry.DoodadIndex = instance.WmoDoodadIndex;
                    entry.Bounds = bounds;
                    entry.TerrainGeneration = terrainGeneration;
                }
                if (!entry.Cache.Cached)
                {
                    links.Clear();
                    if (owner != null && (ownerModel.rootWMOFileDataID != owner.FileDataId ||
                        ownerModel.groupBatches is not { Length: > 0 }))
                        links.Add(new(false, false, default, ownerWorld));
                    else if (owner != null && ownerModel.wrath335 && ownerModel.groupBatches is { } owners)
                    {
                        // 0x7BF7EC -> 0x7C0750 inserts each MODR owner at the
                        // entity list head. Our loader visits groups in source order.
                        for (var ownerIndex = owners.Length - 1; ownerIndex >= 0; ownerIndex--)
                        {
                            var ownerGroup = owners[ownerIndex];
                            if ((uint)instance.WmoDoodadIndex > ushort.MaxValue || Array.IndexOf(ownerGroup.doodadReferences ?? [],
                                (ushort)instance.WmoDoodadIndex) < 0) continue;
                            var grids = ownerGroup.liquid.queryGrids;
                            links.Add(new(false, grids != null, grids, ownerWorld));
                        }
                    }
                    else links.Add(Wrath335EntityLiquidLink.ForTerrain(terrainGrids));
                    var worldBounds = BoundingBox.Transform(new(bounds.Minimum, bounds.Maximum), world);
                    entry.Cache.Query(new(world.M41, world.M42, world.M43), worldBounds,
                        CollectionsMarshal.AsSpan(links));
                }
                var lighting = entry.Cache.Lighting();
                var modelToView = world * view;
                var selection = Wrath335M2WaterQueues.SelectModel(lighting.Flags,
                    bounds.Minimum, bounds.Maximum, bounds.Radius, modelToView,
                    Wrath335M2WaterQueues.PlaneToView(lighting.Plane, view),
                    splitCrossingModels, sceneBelowSide);
                water.Values[index] = new(selection, lighting.Plane);
            }
        return water.Values;
    }
}
