using System.Numerics;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Objects;

namespace WoWRenderLib.DX11.Managers;

public partial class SceneManager
{
    public ObjectSelection Selection { get; } = new();
    internal ObjectGizmoController ObjectGizmo { get; }
    public Container3D? SelectedObject
    {
        get => Selection.Primary;
        set => Selection.Select(value);
    }

    public bool UpdateObjectGizmo(InputFrame input, Camera camera, int width, int height, bool lockWmoScale) =>
        ObjectGizmo.Update(input, Selection, camera, width, height, lockWmoScale);

    public ObjectTransformEdit? TakeCompletedObjectEdit() => ObjectGizmo.TakeCompletedEdit();
    public void CancelObjectManipulation() => ObjectGizmo.Cancel();

    internal void SelectObjectsInScreenRectangle(ScreenSelectionRequest request, Camera camera, Vector2 viewport)
    {
        lock (SceneObjectLock)
            ScreenObjectSelection.Apply(SceneObjects, Selection, request, camera, viewport,
                RenderM2, RenderWMO, ModelRenderDistance);
    }

    public void ApplyObjectEdit(ObjectTransformEdit edit, bool useAfter)
    {
        ObjectGizmo.Cancel();
        foreach (var entry in edit.Entries)
        {
            // History can outlive a map or streamed tile. Never edit detached/replaced containers.
            if (SceneObjects.Contains(entry.Target))
                ApplyObjectTransform(entry.Target, useAfter ? entry.After : entry.Before);
        }
    }

    private void ApplyObjectTransform(Container3D item, PlacementTransform transform)
    {
        if (PlacementTransform.Capture(item) == transform)
            return;
        item.Position = transform.Position;
        item.Rotation = transform.Rotation;
        item.Scale = transform.Scale;
        MarkTileBoundsDirty((int)item.ParentTileIndex);
        if (item is M2Container m2 && m2InstancePackets.TryGetValue(m2.FileDataId, out var packet))
            packet.Invalidate();
        else if (item is WMOContainer)
            foreach (var childPacket in m2InstancePackets.Values)
                childPacket.Invalidate();
    }
}
