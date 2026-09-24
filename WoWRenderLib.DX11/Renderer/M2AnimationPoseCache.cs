using System.Numerics;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>Evaluated states shared by placements of the same immutable M2.</summary>
internal sealed class M2AnimationPoseCache
{
    private readonly Dictionary<M2AnimationFrameKey, M2AnimationPose> _poses = [];
    private readonly Stack<M2AnimationPose> _availablePoses = [];
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
        }
        _animation = animation;
        if (animate)
            _sceneTimeMilliseconds = sceneTimeMilliseconds;
    }

    public M2AnimationPose? GetPose(
        M2Animation animation,
        M2AnimationFrameKey key,
        Submesh[] submeshes,
        bool animate)
    {
        if (_poses.TryGetValue(key, out var pose))
            return pose;
        if (!animate)
            return null;

        pose = _availablePoses.Count > 0
            ? _availablePoses.Pop()
            : new M2AnimationPose();
        if (animation.HasAnimatedBones)
        {
            pose.BonePalette ??= new Matrix4x4[M2Animation.MaxGpuBones];
            Array.Fill(pose.BonePalette, Matrix4x4.Identity);
            animation.Evaluate(key.SequenceIndex, key.TimeMilliseconds, pose.BonePalette);
        }
        else
        {
            pose.BonePalette = null;
        }

        if (pose.Materials.Length != submeshes.Length)
            pose.Materials = new M2AnimatedMaterial[submeshes.Length];
        for (var i = 0; i < submeshes.Length; i++)
            pose.Materials[i] = animation.EvaluateMaterial(
                submeshes[i], key.SequenceIndex, key.TimeMilliseconds);
        pose.Version++;
        _poses.Add(key, pose);
        return pose;
    }
}

internal sealed class M2AnimationPose
{
    public long Version { get; set; }
    public Matrix4x4[]? BonePalette { get; set; }
    public M2AnimatedMaterial[] Materials { get; set; } = [];
}
