using System.Numerics;
using System.Runtime.CompilerServices;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>Retains transformed WMO liquid bounds without retaining unloaded WMO instances.</summary>
internal sealed class WorldLiquidBoundsCache
{
    private sealed class OwnerBounds
    {
        public readonly Dictionary<int, GroupBounds> Groups = [];
    }

    private sealed class GroupBounds
    {
        public ParsedWorldLiquidBatch[]? SourceBatches;
        public Matrix4x4 Model;
        public WorldLiquidCullBounds[] Bounds = [];
    }

    private readonly ConditionalWeakTable<object, OwnerBounds> _owners = new();

    public WorldLiquidCullBounds[] GetOrUpdate(
        object owner,
        int groupIndex,
        ParsedWorldLiquidBatch[] batches,
        Matrix4x4 model)
    {
        var groups = _owners.GetValue(owner, static _ => new OwnerBounds()).Groups;
        if (!groups.TryGetValue(groupIndex, out var group))
        {
            group = new GroupBounds();
            groups.Add(groupIndex, group);
        }

        if (ReferenceEquals(group.SourceBatches, batches) && group.Model == model)
            return group.Bounds;

        if (group.Bounds.Length != batches.Length)
            group.Bounds = new WorldLiquidCullBounds[batches.Length];

        for (var index = 0; index < batches.Length; index++)
            group.Bounds[index] = WorldLiquidCullBounds.Transform(batches[index].Bounds, model);

        group.SourceBatches = batches;
        group.Model = model;
        return group.Bounds;
    }
}
