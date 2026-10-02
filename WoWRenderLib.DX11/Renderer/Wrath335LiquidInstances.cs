using System.Runtime.CompilerServices;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// Liquid instances belong to placements and a loaded batch generation. Shared
/// WMO geometry does not share an instance identity between placements. Tokens
/// preserve lifetimes, not reference-process allocator address order.
/// </summary>
internal sealed class Wrath335LiquidInstances
{
    private sealed class Placement
    {
        internal readonly ConditionalWeakTable<ParsedWorldLiquidBatch[], Generation> Generations = new();
    }
    private sealed class Generation(uint[] tokens)
    {
        internal readonly uint[] Tokens = tokens;
    }
    private readonly ConditionalWeakTable<object, Placement> _placements = new();
    private uint _lastToken;

    internal uint Get(object placement, ParsedWorldLiquidBatch[] batches, int batch)
    {
        var owner = _placements.GetValue(placement, static _ => new());
        if (!owner.Generations.TryGetValue(batches, out var generation))
        {
            var tokens = new uint[batches.Length];
            for (var i = 0; i < tokens.Length; i++)
            {
                _lastToken = checked(_lastToken + 4);
                tokens[i] = _lastToken;
            }
            generation = new(tokens);
            owner.Generations.Add(batches, generation);
        }
        return generation.Tokens[batch];
    }

    // 0x8A20CD..0x8A20E4, settings +0x360 copied from LiquidMaterial.flags & 1.
    internal static WorldLiquidDrawPhase Phase(WorldLiquidMaterialDescriptor material) =>
        material.Wrath335 != null && (material.WmoMaterialFlags & 1) == 0
            ? WorldLiquidDrawPhase.Early : WorldLiquidDrawPhase.Late;

    internal void Clear() => _placements.Clear();
}
