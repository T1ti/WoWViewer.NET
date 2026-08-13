using System.Numerics;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Raycasting;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// Dense retained spatial data for placements sharing one immutable M2 mesh.
/// Scene containers remain the editor-facing objects; the render hot path scans
/// these contiguous arrays and only returns to containers for selection state.
/// </summary>
internal sealed class M2InstancePacket(List<M2Container> instances)
{
    private bool _isBuilt;

    public List<M2Container> Instances { get; } = instances;
    public BoundingSphere[] WorldBounds { get; private set; } = [];
    public Matrix4x4[] WorldMatrices { get; private set; } = [];

    public bool EnsureSpatialData(in ParsedDoodadBatch model)
    {
        if (model.fileDataID == 0 ||
            Instances.Count == 0 ||
            Instances[0].FileDataId != model.fileDataID)
        {
            return false;
        }

        if (_isBuilt &&
            WorldBounds.Length == Instances.Count &&
            WorldMatrices.Length == Instances.Count)
        {
            return true;
        }

        WorldBounds = new BoundingSphere[Instances.Count];
        WorldMatrices = new Matrix4x4[Instances.Count];
        var localBounds = new BoundingSphere(model.boundingBox.Center, model.boundingRadius);
        for (var index = 0; index < Instances.Count; index++)
        {
            var matrix = Instances[index].GetModelMatrix();
            WorldMatrices[index] = matrix;
            WorldBounds[index] = BoundingSphere.Transform(localBounds, matrix);
            Instances[index].CachedBoundingSphere = WorldBounds[index];
        }

        _isBuilt = true;
        return true;
    }

    public void Invalidate() => _isBuilt = false;
}
