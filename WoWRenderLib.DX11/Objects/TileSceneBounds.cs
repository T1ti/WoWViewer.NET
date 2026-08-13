using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Objects;

/// <summary>
/// Conservative world-space bounds for one loaded ADT and every scene object
/// owned by it. A missing child bound makes the aggregate unusable for culling:
/// rejecting a tile with an incomplete aggregate could hide valid geometry.
/// </summary>
public sealed class TileSceneBounds : IDisposable
{
    private readonly HashSet<Container3D> _objects = [];
    private BoundingBox? _terrainBounds;
    private BoundingBox _combinedBounds;
    private bool _dirty = true;

    public TileSceneBounds(MapTile tile)
    {
        Tile = tile;
    }

    public MapTile Tile { get; }
    public uint RootAdtFileDataId { get; private set; }
    public bool IsDirty => _dirty;
    public int ObjectCount => _objects.Count;
    public bool IsCoarseCulledThisFrame { get; internal set; }

    public void SetTerrain(uint rootAdtFileDataId, BoundingBox terrainBounds)
    {
        RootAdtFileDataId = rootAdtFileDataId;
        _terrainBounds = terrainBounds;
        MarkDirty();
    }

    public void AddObject(Container3D sceneObject)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);
        if (!_objects.Add(sceneObject))
            return;

        MarkDirty();
    }

    public void RemoveObject(Container3D sceneObject)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);
        if (!_objects.Remove(sceneObject))
            return;

        MarkDirty();
    }

    public void MarkDirty() => _dirty = true;

    public bool TryGetCombinedBounds(out BoundingBox bounds)
    {
        if (!_dirty)
        {
            bounds = _combinedBounds;
            return true;
        }

        if (!_terrainBounds.HasValue)
        {
            bounds = default;
            return false;
        }

        var combined = _terrainBounds.Value;
        foreach (var sceneObject in _objects)
        {
            var objectBounds = sceneObject.GetBoundingBox();
            if (!objectBounds.HasValue)
            {
                // Keep the aggregate dirty so it retries after asynchronous
                // resource loading has made the object's bounds available.
                bounds = default;
                return false;
            }

            combined = Union(combined, objectBounds.Value);
        }

        _combinedBounds = combined;
        _dirty = false;
        bounds = combined;
        return true;
    }

    public void Dispose()
    {
        _objects.Clear();
        _terrainBounds = null;
        _dirty = true;
        IsCoarseCulledThisFrame = false;
    }

    private static BoundingBox Union(BoundingBox left, BoundingBox right) => new(
        System.Numerics.Vector3.Min(left.Min, right.Min),
        System.Numerics.Vector3.Max(left.Max, right.Max));
}
