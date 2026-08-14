namespace WoWRenderLib.DX11.Renderer;

internal sealed class WmoVisibilityBatch
{
    public bool[] GroupMask { get; private set; } = [];
    public List<int> InstanceIndices { get; } = [];

    public void Begin(ReadOnlySpan<bool> mask)
    {
        if (GroupMask.Length != mask.Length)
            GroupMask = new bool[mask.Length];
        mask.CopyTo(GroupMask);
        InstanceIndices.Clear();
    }

    public bool Matches(ReadOnlySpan<bool> mask) =>
        mask.SequenceEqual(GroupMask);
}
