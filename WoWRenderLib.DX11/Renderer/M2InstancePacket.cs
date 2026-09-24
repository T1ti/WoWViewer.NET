using System.Numerics;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// Dense retained spatial data for placements sharing one immutable M2 mesh.
/// Scene containers remain the editor-facing objects; the render hot path scans
/// these contiguous arrays and only returns to containers for selection state.
/// </summary>
internal sealed class M2InstancePacket(List<M2Container> instances)
{
    private bool _isBuilt;
    private readonly Dictionary<M2AnimationFrameKey, M2AnimationDrawGroup> _groupsByFrame = [];
    private readonly List<M2AnimationDrawGroup> _drawGroups = [];
    private readonly Stack<M2AnimationDrawGroup> _availableGroups = [];
    private readonly M2AnimationDrawGroup[] _staticDrawGroups = [new()];

    public M2AnimationPoseCache AnimationCache { get; } = new();

    /// <summary>Render the culled placements without touching animation state.</summary>
    public IReadOnlyList<M2AnimationDrawGroup> GetStaticDrawGroups(IReadOnlyList<int> visibleIndices)
    {
        _staticDrawGroups[0].UseVisibleIndices(visibleIndices);
        return _staticDrawGroups;
    }

    public IReadOnlyList<M2AnimationDrawGroup> BuildAnimationGroups(
        M2Animation animation,
        Submesh[] submeshes,
        long sceneTimeMilliseconds,
        IReadOnlyList<int> visibleIndices)
    {
        AnimationCache.BeginFrame(animation, sceneTimeMilliseconds, true);
        _groupsByFrame.Clear();
        foreach (var group in _drawGroups)
        {
            group.Reset(null);
            _availableGroups.Push(group);
        }
        _drawGroups.Clear();

        foreach (var instanceIndex in visibleIndices)
        {
            var key = Instances[instanceIndex].AnimationState.GetFrameKey(
                animation, sceneTimeMilliseconds);
            if (!_groupsByFrame.TryGetValue(key, out var group))
            {
                var pose = AnimationCache.GetPose(animation, key, submeshes, true);
                group = _availableGroups.Count > 0
                    ? _availableGroups.Pop()
                    : new M2AnimationDrawGroup();
                group.Reset(pose);
                _groupsByFrame.Add(key, group);
                _drawGroups.Add(group);
            }
            group.AddInstance(instanceIndex);
        }
        return _drawGroups;
    }

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

internal sealed class M2AnimationDrawGroup
{
    private readonly List<int> _ownedIndices = [];

    public M2AnimationPose? Pose { get; private set; }
    public IReadOnlyList<int> Indices { get; private set; }

    public M2AnimationDrawGroup() => Indices = _ownedIndices;

    public void Reset(M2AnimationPose? pose)
    {
        Pose = pose;
        _ownedIndices.Clear();
        Indices = _ownedIndices;
    }

    public void AddInstance(int index) => _ownedIndices.Add(index);

    public void UseVisibleIndices(IReadOnlyList<int> visibleIndices)
    {
        Pose = null;
        Indices = visibleIndices;
    }
}
