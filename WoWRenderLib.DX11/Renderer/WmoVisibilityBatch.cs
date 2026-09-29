using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

internal sealed class WmoVisibilityBatch
{
    public bool[] GroupMask { get; private set; } = [];
    public bool[] BatchMask { get; private set; } = [];
    public bool[] PropagatedMask { get; private set; } = [];
    public Wrath335FogState? InteriorFog { get; private set; }
    public List<int> InstanceIndices { get; } = [];

    public void Begin(ReadOnlySpan<bool> mask, ReadOnlySpan<bool> batchMask,
        ReadOnlySpan<bool> propagatedMask,
        Wrath335FogState? interiorFog = null)
    {
        if (GroupMask.Length != mask.Length)
            GroupMask = new bool[mask.Length];
        mask.CopyTo(GroupMask);
        if (BatchMask.Length != batchMask.Length)
            BatchMask = new bool[batchMask.Length];
        batchMask.CopyTo(BatchMask);
        if (PropagatedMask.Length != propagatedMask.Length)
            PropagatedMask = new bool[propagatedMask.Length];
        propagatedMask.CopyTo(PropagatedMask);
        InteriorFog = interiorFog;
        InstanceIndices.Clear();
    }

    public bool Matches(ReadOnlySpan<bool> mask, ReadOnlySpan<bool> batchMask,
        ReadOnlySpan<bool> propagatedMask,
        Wrath335FogState? interiorFog = null) =>
        mask.SequenceEqual(GroupMask) && batchMask.SequenceEqual(BatchMask) &&
        propagatedMask.SequenceEqual(PropagatedMask) &&
        InteriorFog == interiorFog;
}
