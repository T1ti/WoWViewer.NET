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
    private readonly Dictionary<M2AnimationPoseKey, M2AnimationDrawGroup> _groupsByFrame = [];
    private readonly List<M2AnimationDrawGroup> _drawGroups = [];
    private readonly Stack<M2AnimationDrawGroup> _availableGroups = [];
    private readonly M2AnimationDrawGroup[] _staticDrawGroups = [new()];
    private readonly List<int> _retainedVisibleIndices = [];

    public M2AnimationPoseCache AnimationCache { get; } = new();

    /// <summary>Render the culled placements without touching animation state.</summary>
    public IReadOnlyList<M2AnimationDrawGroup> GetStaticDrawGroups(IReadOnlyList<int> visibleIndices)
    {
        _staticDrawGroups[0].UseVisibleIndices(visibleIndices);
        return _staticDrawGroups;
    }

    /// <summary>Keep the culling result alive through both scene draw phases.</summary>
    public IReadOnlyList<M2AnimationDrawGroup> RetainStaticDrawGroups(IReadOnlyList<int> visibleIndices)
    {
        _retainedVisibleIndices.Clear();
        foreach (var index in visibleIndices)
            _retainedVisibleIndices.Add(index);
        return GetStaticDrawGroups(_retainedVisibleIndices);
    }

    public IReadOnlyList<M2AnimationDrawGroup> BuildAnimationGroups(
        M2Animation animation,
        Submesh[] submeshes,
        long sceneTimeMilliseconds,
        Matrix4x4 cameraView,
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
            var frame = Instances[instanceIndex].AnimationState.GetFrameKey(
                animation, sceneTimeMilliseconds);
            var key = animation.HasBillboardBones
                ? new M2AnimationPoseKey(frame, instanceIndex,
                    WorldRigidMatrices[instanceIndex] * cameraView)
                : M2AnimationPoseKey.Shared(frame);
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
    public Matrix4x4[] WorldRigidMatrices { get; private set; } = [];
    private ulong[] _worldTransformRevisions = [];

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
            WorldMatrices.Length == Instances.Count &&
            _worldTransformRevisions.Length == Instances.Count &&
            (model.animation?.HasBillboardBones != true ||
             WorldRigidMatrices.Length == Instances.Count))
        {
            return true;
        }

        WorldBounds = new BoundingSphere[Instances.Count];
        WorldMatrices = new Matrix4x4[Instances.Count];
        _worldTransformRevisions = new ulong[Instances.Count];
        WorldRigidMatrices = model.animation?.HasBillboardBones == true
            ? new Matrix4x4[Instances.Count]
            : [];
        var localBounds = new BoundingSphere(model.boundingBox.Center, model.boundingRadius);
        for (var index = 0; index < Instances.Count; index++)
            UpdateSpatialData(index, localBounds);

        _isBuilt = true;
        return true;
    }

    public void RefreshSpatialData(int index, in ParsedDoodadBatch model)
    {
        if (_worldTransformRevisions[index] == Instances[index].TransformRevision)
            return;
        UpdateSpatialData(index, new BoundingSphere(model.boundingBox.Center, model.boundingRadius));
    }

    private void UpdateSpatialData(int index, BoundingSphere localBounds)
    {
        var instance = Instances[index];
        var matrix = instance.GetModelMatrix();
        WorldMatrices[index] = matrix;
        if (WorldRigidMatrices.Length != 0)
        {
            WorldRigidMatrices[index] = Matrix4x4.Decompose(
                matrix, out _, out var rotation, out _)
                ? Matrix4x4.CreateFromQuaternion(rotation)
                : Matrix4x4.Identity;
        }
        WorldBounds[index] = BoundingSphere.Transform(localBounds, matrix);
        instance.CachedBoundingSphere = WorldBounds[index];
        _worldTransformRevisions[index] = instance.TransformRevision;
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
