namespace WoWRenderLib.DX11.Renderer;

internal sealed class WmoVisibilityBatch
{
    public bool[] GroupMask { get; private set; } = [];
    public bool[] BatchMask { get; private set; } = [];
    public List<int> InstanceIndices { get; } = [];

    public void Begin(ReadOnlySpan<bool> mask, ReadOnlySpan<bool> batchMask)
    {
        if (GroupMask.Length != mask.Length)
            GroupMask = new bool[mask.Length];
        mask.CopyTo(GroupMask);
        if (BatchMask.Length != batchMask.Length)
            BatchMask = new bool[batchMask.Length];
        batchMask.CopyTo(BatchMask);
        InstanceIndices.Clear();
    }

    public bool Matches(ReadOnlySpan<bool> mask, ReadOnlySpan<bool> batchMask) =>
        mask.SequenceEqual(GroupMask) && batchMask.SequenceEqual(BatchMask);
}
