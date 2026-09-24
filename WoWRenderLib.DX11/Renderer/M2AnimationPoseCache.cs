using System.Numerics;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>Evaluated states shared by placements of the same immutable M2.</summary>
internal sealed class M2AnimationPoseCache
{
    private readonly Dictionary<M2AnimationPoseKey, M2AnimationPose> _poses = [];
    private readonly Stack<M2AnimationPose> _availablePoses = [];
    private readonly Dictionary<M2AnimationFrameKey, M2AnimatedMaterial[]> _materialFrames = [];
    private readonly Stack<M2AnimatedMaterial[]> _availableMaterials = [];
    private M2Animation? _animation;
    private long _sceneTimeMilliseconds = long.MinValue;

    internal int CachedPoseCount => _poses.Count;

    public void BeginFrame(M2Animation? animation, long sceneTimeMilliseconds, bool animate)
    {
        if (!ReferenceEquals(_animation, animation) ||
            (animate && _sceneTimeMilliseconds != sceneTimeMilliseconds))
        {
            foreach (var pose in _poses.Values)
                _availablePoses.Push(pose);
            _poses.Clear();
            foreach (var materials in _materialFrames.Values)
                _availableMaterials.Push(materials);
            _materialFrames.Clear();
        }
        _animation = animation;
        if (animate)
            _sceneTimeMilliseconds = sceneTimeMilliseconds;
    }

    public M2AnimationPose? GetPose(
        M2Animation animation,
        M2AnimationPoseKey key,
        Submesh[] submeshes,
        bool animate)
    {
        if (!animate)
            return null;
        if (_poses.TryGetValue(key, out var pose))
            return pose;

        pose = _availablePoses.Count > 0
            ? _availablePoses.Pop()
            : new M2AnimationPose();
        if (animation.HasAnimatedBones)
        {
            pose.BonePalette ??= new Matrix4x4[M2Animation.MaxGpuBones];
            Array.Fill(pose.BonePalette, Matrix4x4.Identity);
            if (animation.HasBillboardBones)
                animation.Evaluate(key.Frame.SequenceIndex, key.Frame.TimeMilliseconds,
                    pose.BonePalette, key.ModelToView);
            else
                animation.Evaluate(key.Frame.SequenceIndex, key.Frame.TimeMilliseconds,
                    pose.BonePalette);
        }
        else
        {
            pose.BonePalette = null;
        }

        if (!_materialFrames.TryGetValue(key.Frame, out var materials))
        {
            materials = _availableMaterials.Count > 0
                ? _availableMaterials.Pop()
                : [];
            if (materials.Length != submeshes.Length)
                materials = new M2AnimatedMaterial[submeshes.Length];
            for (var i = 0; i < submeshes.Length; i++)
                materials[i] = animation.EvaluateMaterial(
                    submeshes[i], key.Frame.SequenceIndex, key.Frame.TimeMilliseconds);
            _materialFrames.Add(key.Frame, materials);
        }
        pose.Materials = materials;
        pose.Version++;
        _poses.Add(key, pose);
        return pose;
    }
}

internal readonly record struct M2AnimationPoseKey(
    M2AnimationFrameKey Frame,
    int InstanceIndex,
    Matrix4x4 ModelToView)
{
    public static M2AnimationPoseKey Shared(M2AnimationFrameKey frame) =>
        new(frame, -1, Matrix4x4.Identity);
}

internal sealed class M2AnimationPose
{
    public long Version { get; set; }
    public Matrix4x4[]? BonePalette { get; set; }
    public M2AnimatedMaterial[] Materials { get; set; } = [];
}
