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
    private int _spatialCount;
    private bool _hasBillboardBones;
    private readonly Dictionary<M2AnimationFrameKey, M2AnimationDrawGroup> _sharedGroupsByFrame = [];
    private readonly Dictionary<M2AnimationPose, M2AnimationDrawGroup> _groupsByPose = new(ReferenceEqualityComparer.Instance);
    private readonly List<M2AnimationDrawGroup> _drawGroups = [];
    private readonly Stack<M2AnimationDrawGroup> _availableGroups = [];
    private readonly Dictionary<int, M2AnimationPose> _lastLivePoses = [];
    private readonly Dictionary<int, M2AnimationPose> _frozenPoses = [];
    private readonly Dictionary<int, M2AnimationPose> _initialSharedPoses = [];
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
        for (var index = 0; index < visibleIndices.Count; index++)
            _retainedVisibleIndices.Add(visibleIndices[index]);
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
            _initialSharedPoses.Clear();
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
        _sharedGroupsByFrame.Clear();
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
        var defaultSequenceIndex = animation.DefaultSequenceIndex;
        if (preserveLastPose)
        {
            for (var visibleIndex = 0; visibleIndex < visibleIndices.Count; visibleIndex++)
            {
                var instanceIndex = visibleIndices[visibleIndex];
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
                    GetInitialPose(animation, submeshes, cameraView,
                        instanceIndex, defaultSequenceIndex));
            }
        }

        _lastLivePoses.Clear();
        AnimationCache.BeginFrame(animation, sceneTimeMilliseconds, liveIndices.Count > 0);
        liveCursor = 0;
        for (var visibleIndex = 0; visibleIndex < visibleIndices.Count; visibleIndex++)
        {
            var instanceIndex = visibleIndices[visibleIndex];
            if (liveCursor >= liveIndices.Count || liveIndices[liveCursor] != instanceIndex)
            {
                var frozenPose = preserveLastPose
                    ? _frozenPoses[instanceIndex]
                    : GetInitialPose(animation, submeshes, cameraView,
                        instanceIndex, defaultSequenceIndex);
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
                animation, sceneTimeMilliseconds, defaultSequenceIndex);
            M2AnimationDrawGroup group;
            if (animation.HasBillboardBones)
            {
                // A billboard pose is unique to its placement and camera orientation.
                var key = new M2AnimationPoseKey(frame, instanceIndex,
                    WorldRigidMatrices[instanceIndex] * cameraView);
                group = RentGroup(AnimationCache.GetPose(animation, key, submeshes, true));
            }
            else if (!_sharedGroupsByFrame.TryGetValue(frame, out group!))
            {
                var key = M2AnimationPoseKey.Shared(frame);
                var pose = AnimationCache.GetPose(animation, key, submeshes, true);
                group = RentGroup(pose);
                _sharedGroupsByFrame.Add(frame, group);
            }
            group.AddInstance(instanceIndex);
            _lastLivePoses[instanceIndex] = group.Pose!;
        }
        _hasFrozenDrawGroups = liveIndices.Count == 0;
        _lastFrozenGroupsPreserveLastPose = preserveLastPose;
        _lastFrozenVisibleIndices.Clear();
        if (_hasFrozenDrawGroups)
        {
            for (var index = 0; index < visibleIndices.Count; index++)
                _lastFrozenVisibleIndices.Add(visibleIndices[index]);
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
        Matrix4x4 cameraView, int instanceIndex, int defaultSequenceIndex)
    {
        var sequence = Instances[instanceIndex].AnimationState.GetFrameKey(
            animation, 0, defaultSequenceIndex).SequenceIndex;
        if (!animation.HasBillboardBones &&
            _initialSharedPoses.TryGetValue(sequence, out var sharedPose))
            return sharedPose;

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
        var pose = _initialPoseCache.GetPose(animation, initialKey, submeshes, true)!;
        if (!animation.HasBillboardBones)
            _initialSharedPoses.Add(sequence, pose);
        return pose;
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

        var hasBillboardBones = model.animation?.HasBillboardBones == true;
        if (_isBuilt &&
            _spatialCount == Instances.Count &&
            _hasBillboardBones == hasBillboardBones &&
            WorldBounds.Length >= Instances.Count &&
            WorldMatrices.Length >= Instances.Count &&
            _worldTransformRevisions.Length >= Instances.Count &&
            (!hasBillboardBones || WorldRigidMatrices.Length >= Instances.Count))
        {
            return true;
        }

        _lastLivePoses.Clear();
        _frozenPoses.Clear();
        _initialSharedPoses.Clear();
        _distantBillboardKeys.Clear();
        _initialPoseCache.BeginFrame(null, 0, true);
        _hasFrozenDrawGroups = false;
        // Placements stream in and out one at a time. Keep the high-water
        // capacity so an invalidation does not allocate every spatial array.
        if (WorldBounds.Length < Instances.Count)
        {
            var capacity = Math.Max(Instances.Count, Math.Max(4, WorldBounds.Length * 2));
            Array.Resize(ref _worldTransformRevisions, capacity);
            var bounds = WorldBounds;
            Array.Resize(ref bounds, capacity);
            WorldBounds = bounds;
            var matrices = WorldMatrices;
            Array.Resize(ref matrices, capacity);
            WorldMatrices = matrices;
        }
        if (hasBillboardBones && WorldRigidMatrices.Length < Instances.Count)
        {
            var matrices = WorldRigidMatrices;
            Array.Resize(ref matrices, WorldBounds.Length);
            WorldRigidMatrices = matrices;
        }
        _hasBillboardBones = hasBillboardBones;
        var localBounds = new BoundingSphere(model.boundingBox.Center, model.boundingRadius);
        for (var index = 0; index < Instances.Count; index++)
            UpdateSpatialData(index, Instances[index], localBounds);

        _spatialCount = Instances.Count;
        _isBuilt = true;
        return true;
    }

    public void RefreshSpatialData(int index, M2Container instance, in ParsedDoodadBatch model)
    {
        if (_worldTransformRevisions[index] == instance.TransformRevision)
            return;
        UpdateSpatialData(index, instance,
            new BoundingSphere(model.boundingBox.Center, model.boundingRadius));
    }

    private void UpdateSpatialData(int index, M2Container instance, BoundingSphere localBounds)
    {
        var matrix = instance.GetModelMatrix();
        WorldMatrices[index] = matrix;
        if (_hasBillboardBones)
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
