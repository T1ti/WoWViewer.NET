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
        var groups = packet.BuildAnimationGroups(animation, [], 250, Matrix4x4.Identity, []);

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
        var firstKey = M2AnimationPoseKey.Shared(
            new M2InstanceAnimationState().GetFrameKey(animation, 250));

        cache.BeginFrame(animation, 250, false);
        Assert.IsNull(cache.GetPose(animation, firstKey, batches, false));

        cache.BeginFrame(animation, 250, true);
        var first = cache.GetPose(animation, firstKey, batches, true);
        Assert.IsNotNull(first);
        Assert.AreEqual(2.5f, first.BonePalette![0].M41, 0.0001f);
        Assert.AreEqual(0.875f, first.Materials[0].Color.W, 0.0001f);
        Assert.AreSame(first, cache.GetPose(animation, firstKey, batches, true));

        cache.BeginFrame(animation, 250, false);
        Assert.IsNull(cache.GetPose(animation, firstKey, batches, false));
        Assert.IsNull(cache.GetPose(
            animation, M2AnimationPoseKey.Shared(new M2AnimationFrameKey(1, 350)), batches, false));

        cache.BeginFrame(animation, 250, true);
        Assert.AreSame(first, cache.GetPose(animation, firstKey, batches, true));

        cache.BeginFrame(animation, 350, true);
        var next = cache.GetPose(
            animation, M2AnimationPoseKey.Shared(new M2AnimationFrameKey(1, 350)), batches, true);
        Assert.IsNotNull(next);
        Assert.AreEqual(3.5f, next.BonePalette![0].M41, 0.0001f);
    }

    [TestMethod]
    public void BillboardBonesFaceTheViewAndKeepTheirPivotAndChildren()
    {
        var animation = CreateBillboardAnimation();
        Span<Matrix4x4> palette = stackalloc Matrix4x4[2];
        animation.Evaluate(0, 0, palette);
        Assert.AreEqual(Matrix4x4.Identity, palette[0]);

        animation.Evaluate(0, 0, palette, Matrix4x4.Identity);
        var identityFacing = Vector3.TransformNormal(Vector3.UnitX, palette[0]);
        Assert.IsTrue(Vector3.Distance(identityFacing, -Vector3.UnitZ) < 0.0001f);
        Assert.IsTrue(Vector3.Distance(
            Vector3.Transform(Vector3.UnitX, palette[0]), Vector3.UnitX) < 0.0001f);
        Assert.AreEqual(palette[0], palette[1]);

        var rotatedView = Matrix4x4.CreateRotationY(MathF.PI / 2);
        animation.Evaluate(0, 0, palette, rotatedView);
        var modelFacing = Vector3.TransformNormal(Vector3.UnitX, palette[0]);
        var viewFacing = Vector3.TransformNormal(modelFacing, rotatedView);
        Assert.IsTrue(Vector3.Distance(viewFacing, -Vector3.UnitZ) < 0.0001f);
        Assert.IsTrue(Vector3.Distance(modelFacing, identityFacing) > 0.5f);
        Assert.IsTrue(Vector3.Distance(
            Vector3.Transform(Vector3.UnitX, palette[0]), Vector3.UnitX) < 0.0001f);
    }

    [TestMethod]
    public void BillboardPalettesArePlacementSpecificWhileMaterialsAreShared()
    {
        var animation = CreateBillboardAnimation();
        var cache = new M2AnimationPoseCache();
        var frame = new M2AnimationFrameKey(0, 250);
        var batches = new[] { new Submesh { colorIndex = -1, textureWeightIndex = -1 } };
        cache.BeginFrame(animation, 250, true);

        var first = cache.GetPose(animation,
            new M2AnimationPoseKey(frame, 0, Matrix4x4.Identity), batches, true);
        var second = cache.GetPose(animation,
            new M2AnimationPoseKey(frame, 1, Matrix4x4.CreateRotationY(MathF.PI / 2)),
            batches, true);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreNotSame(first, second);
        Assert.AreSame(first.Materials, second.Materials);
        Assert.AreEqual(2, cache.CachedPoseCount);
        Assert.AreNotEqual(first.BonePalette![0], second.BonePalette![0]);
    }

    [TestMethod]
    public void CylindricalBillboardsLockTheirAuthoredAxis()
    {
        var view = Matrix4x4.CreateRotationZ(0.35f) * Matrix4x4.CreateRotationY(0.4f);
        foreach (var (flag, lockedAxis) in new (uint Flag, int Axis)[]
                 { (0x10, 0), (0x20, 1), (0x40, 2) })
        {
            var animation = CreateBillboardAnimation(flag);
            Span<Matrix4x4> palette = stackalloc Matrix4x4[2];
            animation.Evaluate(0, 0, palette, view);
            var viewSpace = palette[0] * view;
            var expectedAxis = Vector3.Normalize(GetBasis(view, lockedAxis));
            var actualAxis = Vector3.Normalize(GetBasis(viewSpace, lockedAxis));

            Assert.IsTrue(Vector3.Distance(expectedAxis, actualAxis) < 0.0001f);
            Assert.IsTrue(Vector3.Distance(
                Vector3.Transform(Vector3.UnitX, palette[0]), Vector3.UnitX) < 0.0001f);
            Assert.AreEqual(palette[0], palette[1]);
        }
    }

    [TestMethod]
    public void AnimatedSphericalBillboardUsesItsLocalRotation()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4);
        var animation = CreateBillboardAnimation(0x8, rotation);
        Span<Matrix4x4> palette = stackalloc Matrix4x4[2];
        animation.Evaluate(0, 0, palette, Matrix4x4.Identity);

        var localX = GetBasis(Matrix4x4.CreateFromQuaternion(rotation), 0);
        var expectedX = new Vector3(localX.Y, localX.Z, -localX.X);
        Assert.IsTrue(Vector3.Distance(expectedX,
            GetBasis(palette[0], 0)) < 0.0001f);
        Assert.IsTrue(Vector3.Distance(
            Vector3.Transform(Vector3.UnitX, palette[0]), Vector3.UnitX) < 0.0001f);
    }

    [TestMethod]
    public void TrackSamplingMatchesWispAtKeyBoundariesAndForUnsupportedCurves()
    {
        var stepped = new M2Track<float>
        {
            Interpolation = 0,
            GlobalSequence = -1,
            Timelines = [new M2Timeline<float>([0, 1000], [1f, 0f])]
        };
        var sequence = new M2Sequence(1000, 1);
        Assert.AreEqual(1f, stepped.Sample(0, sequence, [],
            500, 0f, float.Lerp));
        Assert.AreEqual(0f, stepped.Sample(0, sequence, [],
            1000, 0f, float.Lerp));

        var hermite = new M2Track<float>
        {
            Interpolation = 2,
            GlobalSequence = -1,
            Timelines = [new M2Timeline<float>([0, 1000], [1f, 0f])]
        };
        Assert.AreEqual(0.5f, hermite.Sample(0, sequence, [],
            500, 0f, float.Lerp));

        var missingGlobalLoop = new M2Track<float>
        {
            Interpolation = 1,
            GlobalSequence = 7,
            Timelines = [new M2Timeline<float>([0, 1000], [0f, 1f])]
        };
        Assert.AreEqual(0.5f, missingGlobalLoop.Sample(
            0, new M2Sequence(2000, 0), [], 1500, 0f, float.Lerp));
    }

    [TestMethod]
    public void FullDaySkyboxClockUsesLightingTimeAndOtherSkyboxesUseSceneTime()
    {
        var animation = CreateAnimation();
        Assert.AreEqual(500L, SkyboxAnimationClock.GetTimeMilliseconds(animation, 0x1, 123, 1440));
        Assert.AreEqual(0L, SkyboxAnimationClock.GetTimeMilliseconds(animation, 0x1, 123, 2880));
        Assert.AreEqual(999L, SkyboxAnimationClock.GetTimeMilliseconds(animation, 0x1, 123, -1));
        Assert.AreEqual(123L, SkyboxAnimationClock.GetTimeMilliseconds(animation, 0x2, 123, 1440));

        var aliased = new M2Animation
        {
            Bones = [],
            GlobalLoops = [],
            Sequences =
            [
                new M2Sequence(0, 0x40, AliasNext: 1, AnimationId: 0),
                new M2Sequence(2000, 0, AnimationId: 1)
            ]
        };
        Assert.AreEqual(1000L, SkyboxAnimationClock.GetTimeMilliseconds(aliased, 0x1, 123, 1440));
    }

    private static Vector3 GetBasis(Matrix4x4 matrix, int axis) => axis switch
    {
        0 => new Vector3(matrix.M11, matrix.M12, matrix.M13),
        1 => new Vector3(matrix.M21, matrix.M22, matrix.M23),
        _ => new Vector3(matrix.M31, matrix.M32, matrix.M33)
    };

    private static M2Animation CreateBillboardAnimation(
        uint billboardFlag = 0x8, Quaternion? localRotation = null)
    {
        var translation = new M2Track<Vector3>
        {
            Interpolation = 0, GlobalSequence = -1, Timelines = []
        };
        var rotation = new M2Track<Quaternion>
        {
            Interpolation = 0, GlobalSequence = -1,
            Timelines = localRotation is { } value
                ? [new M2Timeline<Quaternion>([0], [value])]
                : []
        };
        return new M2Animation
        {
            Sequences = [new M2Sequence(1000, 0)],
            GlobalLoops = [],
            HasAnimatedBones = true,
            HasBillboardBones = true,
            Bones =
            [
                new M2Bone(-1, billboardFlag | (localRotation.HasValue ? 0x280u : 0u), Vector3.UnitX,
                    translation, rotation, translation),
                new M2Bone(0, 0, Vector3.Zero,
                    translation, rotation, translation)
            ]
        };
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
