using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>Runtime state at the query boundary, separate from MODF file flags.</summary>
internal struct WmoViewerPlacementState
{
    public uint RuntimeFlags { get; private set; }
    public BoundingBox? FileBounds { get; private set; }

    public void InitializeFilePlacement(BoundingBox? bounds)
    {
        // 0x7BF4FC clears runtime +0x0C; MODF +0x38 is not copied there.
        RuntimeFlags = 0;
        FileBounds = bounds;
    }

    public void TransformChanged()
    {
        // 0x7B64FC sets +0x0C bit 0x400 before rebuilding transformed bounds.
        RuntimeFlags |= Wrath335ViewerPlacementSelection.UpdatedTransform;
        FileBounds = null;
    }
}
