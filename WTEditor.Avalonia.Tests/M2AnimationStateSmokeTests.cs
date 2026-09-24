using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Structs;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class M2AnimationStateSmokeTests
{
    [TestMethod]
    public void HiddenPlacementsNeverBuildAnimationPosesAndStaticDrawsReuseCulledIndices()
    {
        var packet = new M2InstancePacket([]);
        var animation = CreateAnimation();
        var groups = packet.BuildAnimationGroups(animation, [], 250, []);

        Assert.AreEqual(0, groups.Count);
        Assert.AreEqual(0, packet.AnimationCache.CachedPoseCount);

        IReadOnlyList<int> visibleIndices = new[] { 2, 4 };
        var staticGroups = packet.GetStaticDrawGroups(visibleIndices);
        Assert.AreEqual(1, staticGroups.Count);
        Assert.AreSame(visibleIndices, staticGroups[0].Indices);
        Assert.IsNull(staticGroups[0].Pose);
        Assert.AreEqual(0, packet.AnimationCache.CachedPoseCount);
    }

    [TestMethod]
    public void PlacementSequenceAndTimeSelectIndependentAnimationFrames()
    {
        var animation = CreateAnimation();
        var first = new M2InstanceAnimationState();
        var second = new M2InstanceAnimationState
        {
            SequenceIndex = 0,
            TimeOffsetMilliseconds = 100
        };

        Assert.AreEqual(1, animation.DefaultSequenceIndex);
        Assert.AreEqual(new M2AnimationFrameKey(1, 250), first.GetFrameKey(animation, 250));
        Assert.AreEqual(new M2AnimationFrameKey(0, 350), second.GetFrameKey(animation, 250));
    }

    [TestMethod]
    public void PoseCacheReusesSameFrameAndNeverEvaluatesWhileDisabled()
    {
        var animation = CreateAnimation();
        var batch = new Submesh
        {
            colorIndex = 0,
            textureWeightIndex = -1,
            textureTransformIndex1 = -1,
            textureTransformIndex2 = -1
        };
        var batches = new[] { batch };
        var cache = new M2AnimationPoseCache();
        var firstKey = new M2InstanceAnimationState().GetFrameKey(animation, 250);

        cache.BeginFrame(animation, 250, false);
        Assert.IsNull(cache.GetPose(animation, firstKey, batches, false));

        cache.BeginFrame(animation, 250, true);
        var first = cache.GetPose(animation, firstKey, batches, true);
        Assert.IsNotNull(first);
        Assert.AreEqual(2.5f, first.BonePalette![0].M41, 0.0001f);
        Assert.AreEqual(0.875f, first.Materials[0].Color.W, 0.0001f);
        Assert.AreSame(first, cache.GetPose(animation, firstKey, batches, true));

        cache.BeginFrame(animation, 250, false);
        Assert.AreSame(first, cache.GetPose(animation, firstKey, batches, false));
        Assert.IsNull(cache.GetPose(
            animation, new M2AnimationFrameKey(1, 350), batches, false));

        cache.BeginFrame(animation, 350, true);
        var next = cache.GetPose(
            animation, new M2AnimationFrameKey(1, 350), batches, true);
        Assert.IsNotNull(next);
        Assert.AreEqual(3.5f, next.BonePalette![0].M41, 0.0001f);
    }

    private static M2Animation CreateAnimation()
    {
        static M2Track<T> Track<T>(params M2Timeline<T>[] timelines) => new()
        {
            Interpolation = 1,
            GlobalSequence = -1,
            Timelines = timelines
        };

        return new M2Animation
        {
            Sequences =
            [
                new M2Sequence(1000, 0, AnimationId: 1),
                new M2Sequence(1000, 0, AnimationId: 0)
            ],
            GlobalLoops = [],
            HasAnimatedBones = true,
            Bones =
            [
                new M2Bone(-1, 0x200, Vector3.Zero,
                    Track(
                        new M2Timeline<Vector3>([0], [Vector3.Zero]),
                        new M2Timeline<Vector3>([0, 1000],
                            [Vector3.Zero, new Vector3(10, 0, 0)])),
                    Track<Quaternion>(),
                    Track<Vector3>())
            ],
            Colors =
            [
                new M2ColorAnimation(
                    Track<Vector3>(),
                    Track(
                        new M2Timeline<float>([0], [1f]),
                        new M2Timeline<float>([0, 1000], [1f, 0.5f])))
            ]
        };
    }
}
