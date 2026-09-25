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
    private readonly Dictionary<M2AnimationPose, M2AnimationDrawGroup> _groupsByPose = new(ReferenceEqualityComparer.Instance);
    private readonly List<M2AnimationDrawGroup> _drawGroups = [];
    private readonly Stack<M2AnimationDrawGroup> _availableGroups = [];
    private readonly Dictionary<int, M2AnimationPose> _lastLivePoses = [];
    private readonly Dictionary<int, M2AnimationPose> _frozenPoses = [];
    private readonly Dictionary<M2AnimationPose, M2AnimationPose> _snapshotsBySource = new(ReferenceEqualityComparer.Instance);
    private readonly M2AnimationPoseCache _initialPoseCache = new();
    private readonly Dictionary<int, M2AnimationPoseKey> _distantBillboardKeys = [];
    private readonly List<int> _lastFrozenVisibleIndices = [];
    private bool _hasFrozenDrawGroups;
    private bool _lastFrozenGroupsPreserveLastPose;
    private M2Animation? _poseAnimation;
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
        => BuildAnimationGroups(animation, submeshes, sceneTimeMilliseconds,
            cameraView, visibleIndices, visibleIndices, true);

    /// <summary>
    /// Animate near placements. A paused viewport retains its last pose; placements
    /// beyond the animation distance share cached frame-zero poses for instancing.
    /// </summary>
    public IReadOnlyList<M2AnimationDrawGroup> BuildAnimationGroups(
        M2Animation animation,
        Submesh[] submeshes,
        long sceneTimeMilliseconds,
        Matrix4x4 cameraView,
        IReadOnlyList<int> visibleIndices,
        IReadOnlyList<int> liveIndices,
        bool preserveLastPose = true)
    {
        if (!ReferenceEquals(_poseAnimation, animation))
        {
            _lastLivePoses.Clear();
            _frozenPoses.Clear();
            _distantBillboardKeys.Clear();
            _hasFrozenDrawGroups = false;
            _poseAnimation = animation;
        }
        if (liveIndices.Count == 0 && _hasFrozenDrawGroups &&
            _lastFrozenGroupsPreserveLastPose == preserveLastPose &&
            visibleIndices.Count == _lastFrozenVisibleIndices.Count)
        {
            var sameVisibleSet = true;
            for (var i = 0; i < visibleIndices.Count; i++)
            {
                if (visibleIndices[i] == _lastFrozenVisibleIndices[i])
                    continue;
                sameVisibleSet = false;
                break;
            }
            if (sameVisibleSet)
                return _drawGroups;
        }
        _groupsByFrame.Clear();
        _groupsByPose.Clear();
        foreach (var group in _drawGroups)
        {
            group.Reset(null);
            _availableGroups.Push(group);
        }
        _drawGroups.Clear();

        _initialPoseCache.BeginFrame(animation, 0, true);
        _snapshotsBySource.Clear();
        if (!preserveLastPose)
            _frozenPoses.Clear();

        // Capture paused poses before the live cache recycles last frame's poses.
        // liveIndices is an ordered subset of visibleIndices.
        var liveCursor = 0;
        if (preserveLastPose)
        {
            foreach (var instanceIndex in visibleIndices)
            {
                if (liveCursor < liveIndices.Count && liveIndices[liveCursor] == instanceIndex)
                {
                    liveCursor++;
                    continue;
                }

                if (_frozenPoses.ContainsKey(instanceIndex))
                    continue;
                if (_lastLivePoses.TryGetValue(instanceIndex, out var lastPose))
                {
                    if (!_snapshotsBySource.TryGetValue(lastPose, out var snapshot))
                    {
                        snapshot = CopyPose(lastPose);
                        _snapshotsBySource.Add(lastPose, snapshot);
                    }
                    _frozenPoses.Add(instanceIndex, snapshot);
                    continue;
                }

                _frozenPoses.Add(instanceIndex,
                    GetInitialPose(animation, submeshes, cameraView, instanceIndex));
            }
        }

        _lastLivePoses.Clear();
        AnimationCache.BeginFrame(animation, sceneTimeMilliseconds, liveIndices.Count > 0);
        liveCursor = 0;
        foreach (var instanceIndex in visibleIndices)
        {
            if (liveCursor >= liveIndices.Count || liveIndices[liveCursor] != instanceIndex)
            {
                var frozenPose = preserveLastPose
                    ? _frozenPoses[instanceIndex]
                    : GetInitialPose(animation, submeshes, cameraView, instanceIndex);
                if (!_groupsByPose.TryGetValue(frozenPose, out var frozenGroup))
                {
                    frozenGroup = RentGroup(frozenPose);
                    _groupsByPose.Add(frozenPose, frozenGroup);
                }
                frozenGroup.AddInstance(instanceIndex);
                continue;
            }
            liveCursor++;
            _frozenPoses.Remove(instanceIndex);
            if (_distantBillboardKeys.Remove(instanceIndex, out var distantKey))
                _initialPoseCache.RemovePose(distantKey);
            var frame = Instances[instanceIndex].AnimationState.GetFrameKey(
                animation, sceneTimeMilliseconds);
            var key = animation.HasBillboardBones
                ? new M2AnimationPoseKey(frame, instanceIndex,
                    WorldRigidMatrices[instanceIndex] * cameraView)
                : M2AnimationPoseKey.Shared(frame);
            if (!_groupsByFrame.TryGetValue(key, out var group))
            {
                var pose = AnimationCache.GetPose(animation, key, submeshes, true);
                group = RentGroup(pose);
                _groupsByFrame.Add(key, group);
            }
            group.AddInstance(instanceIndex);
            _lastLivePoses[instanceIndex] = group.Pose!;
        }
        _hasFrozenDrawGroups = liveIndices.Count == 0;
        _lastFrozenGroupsPreserveLastPose = preserveLastPose;
        _lastFrozenVisibleIndices.Clear();
        if (_hasFrozenDrawGroups)
        {
            foreach (var index in visibleIndices)
                _lastFrozenVisibleIndices.Add(index);
        }
        return _drawGroups;
    }

    private M2AnimationDrawGroup RentGroup(M2AnimationPose? pose)
    {
        var group = _availableGroups.Count > 0
            ? _availableGroups.Pop()
            : new M2AnimationDrawGroup();
        group.Reset(pose);
        _drawGroups.Add(group);
        return group;
    }

    private M2AnimationPose GetInitialPose(M2Animation animation, Submesh[] submeshes,
        Matrix4x4 cameraView, int instanceIndex)
    {
        var sequence = Instances[instanceIndex].AnimationState.GetFrameKey(animation, 0).SequenceIndex;
        var initialFrame = new M2AnimationFrameKey(sequence, 0);
        var initialKey = M2AnimationPoseKey.Shared(initialFrame);
        if (animation.HasBillboardBones)
        {
            if (_distantBillboardKeys.TryGetValue(instanceIndex, out var retainedKey))
            {
                if (retainedKey.Frame == initialFrame)
                    initialKey = retainedKey;
                else
                {
                    _initialPoseCache.RemovePose(retainedKey);
                    _distantBillboardKeys.Remove(instanceIndex);
                }
            }
            if (initialKey.InstanceIndex < 0)
            {
                initialKey = new M2AnimationPoseKey(initialFrame, instanceIndex,
                    WorldRigidMatrices[instanceIndex] * cameraView);
                _distantBillboardKeys[instanceIndex] = initialKey;
            }
        }
        return _initialPoseCache.GetPose(animation, initialKey, submeshes, true)!;
    }

    private static M2AnimationPose CopyPose(M2AnimationPose source) => new()
    {
        Version = source.Version,
        BonePalette = source.BonePalette is { } bones ? (Matrix4x4[])bones.Clone() : null,
        Materials = (M2AnimatedMaterial[])source.Materials.Clone()
    };

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

        _lastLivePoses.Clear();
        _frozenPoses.Clear();
        _distantBillboardKeys.Clear();
        _initialPoseCache.BeginFrame(null, 0, true);
        _hasFrozenDrawGroups = false;
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
