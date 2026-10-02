using System.Runtime.InteropServices;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// Retained CPU preparation scratch for admitted native elements. Mesh/effect
/// producers supply resolved keys, eligibility and water masks. No GPU submission
/// is performed. Texture identity storage is borrowed until Sort completes.
/// AdditiveGroup is per-sort scratch, not persistent above/below queue metadata.
/// </summary>
internal sealed class Wrath335M2ElementQueues
{
    private readonly List<Wrath335M2ElementSortData> elements = [];
    private readonly List<int> opaque = [];
    private readonly List<int> above = [];
    private readonly List<int> below = [];
    internal IReadOnlyList<Wrath335M2ElementSortData> Elements => elements;
    internal IReadOnlyList<int> Opaque => opaque;
    internal IReadOnlyList<int> AboveWater => above;
    internal IReadOnlyList<int> BelowWater => below;
    internal uint AdditiveParticleCount { get; private set; }

    internal void BeginFrame()
    {
        elements.Clear();
        opaque.Clear();
        above.Clear();
        below.Clear();
        AdditiveParticleCount = 0;
    }

    internal void Add(in Wrath335M2ElementSortData element, Wrath335M2QueueMask queues)
    {
        if (queues == Wrath335M2QueueMask.None) return;
        var index = elements.Count;
        elements.Add(element);
        if ((queues & Wrath335M2QueueMask.Opaque) != 0) opaque.Add(index);
        if ((queues & Wrath335M2QueueMask.AboveWater) != 0) above.Add(index);
        if ((queues & Wrath335M2QueueMask.BelowWater) != 0) below.Add(index);
        // 0x8219A8..0x8219C5: count admitted resolved blend 3/10 particle
        // elements scene-wide, including children. Cache 0x100 does not alter it.
        if (element.Kind == Wrath335M2ElementKind.ParticleRun && element.ParticleBlendMode is 3 or 10)
            AdditiveParticleCount++;
    }

    internal void Sort(uint cacheFlags)
    {
        var table = CollectionsMarshal.AsSpan(elements);
        Wrath335M2ElementOrdering.Sort(CollectionsMarshal.AsSpan(opaque), table, false);
        var groupShaders = (cacheFlags & 0x4000) != 0;
        var regroup = (cacheFlags & 0x80) != 0;
        var forceParticleAdditive = (cacheFlags & 0x100) != 0;
        Wrath335M2ElementOrdering.SortTransparentQueue(CollectionsMarshal.AsSpan(above), table,
            groupShaders, regroup, AdditiveParticleCount, forceParticleAdditive);
        Wrath335M2ElementOrdering.SortTransparentQueue(CollectionsMarshal.AsSpan(below), table,
            groupShaders, regroup, AdditiveParticleCount, forceParticleAdditive);
    }
}
