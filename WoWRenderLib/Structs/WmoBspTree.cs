namespace WoWRenderLib.Structs;

/// <summary>Decoded MOBN node. Child indices are unsigned; 0xFFFF is absent.</summary>
public readonly record struct WmoBspNode(
    ushort Flags, ushort NegativeChild, ushort PositiveChild,
    ushort FaceCount, uint FaceStart, float PlaneDistance);

/// <summary>
/// Owned CPU data for the 3.3.5 camera query. Face references retain MOBR order,
/// duplicates, and their original MOPY/MOVI face identity.
/// </summary>
public sealed class WmoBspTree
{
    private const int DigestFaceLimit = 300;
    private const int DigestVertexLimit = 450;

    public WmoBspNode[] Nodes { get; }
    public ushort[] FaceReferences { get; }
    public ushort[] Indices { get; }
    public byte[] FaceFlags { get; }
    public bool[] DigestEligibleLeaves { get; }

    public WmoBspTree(WmoBspNode[] nodes, ushort[] faceReferences,
        ushort[] indices, byte[] faceFlags)
    {
        Nodes = nodes;
        FaceReferences = faceReferences;
        Indices = indices;
        FaceFlags = faceFlags;
        DigestEligibleLeaves = new bool[nodes.Length];
        // BuildRayCullBspLeafTriCache preserves MOBR order and falls back to
        // raw faces above 300 faces or 450 unique vertex indices. Recover that
        // eligibility once while preparing CPU data, before any frame queries.
        var vertexIndices = new HashSet<ushort>();
        for (var nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            var node = nodes[nodeIndex];
            if ((node.Flags & 4) == 0 || node.FaceCount > DigestFaceLimit ||
                node.FaceStart > faceReferences.Length ||
                node.FaceCount > faceReferences.Length - node.FaceStart)
                continue;
            vertexIndices.Clear();
            var valid = true;
            for (var index = 0; index < node.FaceCount; index++)
            {
                var face = faceReferences[(int)node.FaceStart + index];
                if (face >= indices.Length / 3)
                {
                    valid = false;
                    break;
                }
                for (var corner = 0; corner < 3; corner++)
                    vertexIndices.Add(indices[face * 3 + corner]);
                if (vertexIndices.Count > DigestVertexLimit)
                {
                    valid = false;
                    break;
                }
            }
            DigestEligibleLeaves[nodeIndex] = valid;
        }
    }
}
