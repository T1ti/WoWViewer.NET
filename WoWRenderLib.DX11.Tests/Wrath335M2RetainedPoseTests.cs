using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335M2RetainedPoseTests
{
    [TestMethod]
    public void RetainedHighBoneMeshAndEmitterKeysEnterSharedQueuesAcrossFrames()
    {
        var animation = Animation(501, 500);
        var model = Model(animation, 500);
        var packet = Packet(model, Placement(7, Vector3.Zero));
        var queues = new Wrath335M2ElementQueues();
        var both = Wrath335M2QueueMask.AboveWater | Wrath335M2QueueMask.BelowWater;
        for (var frame = 0; frame < 2; frame++)
        {
            queues.BeginFrame();
            var pose = packet.BuildAnimationGroups(animation, model.submeshes,
                frame == 0 ? 250 : 500, Matrix4x4.Identity, [0])[0].Pose!;
            var mesh = Build(model, packet, pose);
            var route = Wrath335M2WaterQueues.RouteMesh(true, false, new(true, true));
            queues.Add(mesh with { Flags = mesh.Flags | (route.ClipWaterPlane ? 2u : 0u) }, route.Queues);
            var key = Wrath335M2ElementOrdering.ParticleDistance(new(0, 0, 5),
                pose.BoneModelMatrices[500] * packet.WorldMatrices[0], mesh.Distance.Primary);
            queues.Add(new() { Kind = Wrath335M2ElementKind.ParticleRun, Distance = key,
                ParticleBlendMode = 3, PriorityPlane = -4 },
                Wrath335M2WaterQueues.RouteParticle(3, 1, true, 0));
            queues.Sort(0x80); // One additive particle leaves the base distance/priority order intact.
            CollectionAssert.AreEqual(new[] { 1, 0 }, queues.AboveWater.ToArray());
            CollectionAssert.AreEqual(new[] { 0 }, queues.BelowWater.ToArray());
            Assert.AreEqual(frame == 0 ? 56.25f : 100f, queues.Elements[0].Distance.Secondary);
            Assert.AreEqual(queues.Elements[0].Distance, queues.Elements[1].Distance);
            Assert.AreEqual(2u, queues.Elements[0].Flags & 2);
            Assert.AreEqual(both, route.Queues);
        }
    }

    [TestMethod]
    public void NativeWotlkDecoderOwnsTheFullSkeletonWithoutGpuLimitRejection()
    {
        M2Animation decoded;
        using (var root = new WoWLib.Formats.M2.Root.M2RootWotlk())
        {
            for (var i = 0; i <= 500; i++)
            {
                using var bone = new WoWLib.Formats.M2.Root.Record.M2CompBoneWotlkPlus
                { ParentBone = (short)(i - 1), Flags = (WoWLib.Formats.M2.Root.Record.BoneFlags)(i == 500 ? 4 : 0) };
                using var pivot = new WoWLib.Formats.Common.C3Vector(i, 2, 3);
                bone.Pivot = pivot;
                root.Bones.Add(bone);
            }
            decoded = (M2Animation)typeof(WoWRenderLib.Loaders.M2Loader).GetMethod("ReadAnimation",
                BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [root])!;
        }
        Assert.AreEqual(501, decoded.Bones.Length);
        Assert.AreEqual(499, decoded.Bones[500].Parent);
        Assert.AreEqual(new Vector3(500, 2, 3), decoded.Bones[500].Pivot);
        Assert.IsTrue(decoded.HasAnimatedBones);
        Assert.IsTrue(decoded.HasViewDependentBones);
        Assert.AreEqual(501, Pose(decoded, Matrix4x4.Identity).BoneModelMatrices.Length);
    }

    [TestMethod]
    public void FullCpuPoseUsesHighCenterBoneAndLeavesGpuBytePaletteBounded()
    {
        var animation = Animation(501, 500);
        var model = Model(animation, 500);
        var packet = Packet(model, Placement(7, Vector3.Zero));
        var groups = packet.BuildAnimationGroups(animation, model.submeshes, 250,
            Matrix4x4.Identity, [0]);
        var pose = groups[0].Pose!;
        Assert.AreEqual(501, pose.BoneModelMatrices.Length);
        Assert.AreEqual(256, pose.BonePalette!.Length);
        Assert.AreEqual(Matrix4x4.Identity, pose.BonePalette[255]);
        Assert.AreEqual(2.5f, pose.BoneModelMatrices[500].M43, 0.0001f);
        var element = Build(model, packet, pose);
        Assert.AreEqual(new Wrath335M2DistanceKeys(56.25f, 56.25f), element.Distance);
        var version = pose.Version;
        var bones = pose.BoneModelMatrices;
        groups = packet.BuildAnimationGroups(animation, model.submeshes, 500,
            Matrix4x4.Identity, [0]);
        Assert.AreSame(bones, groups[0].Pose!.BoneModelMatrices);
        Assert.IsTrue(groups[0].Pose!.Version > version);
        Assert.AreEqual(100f, Build(model, packet, groups[0].Pose).Distance.Secondary);
    }

    [TestMethod]
    public void FrozenFullPoseSurvivesLiveRecyclingAndSkeletonReplacement()
    {
        var animation = Animation(501, 500);
        var model = Model(animation, 500);
        var packet = Packet(model, Placement(7, Vector3.Zero), Placement(7, Vector3.Zero));
        packet.BuildAnimationGroups(animation, model.submeshes, 250, Matrix4x4.Identity, [0, 1]);
        var groups = packet.BuildAnimationGroups(animation, model.submeshes, 750,
            Matrix4x4.Identity, [0, 1], [1]);
        var frozen = groups.Single(g => g.Indices.Contains(0)).Pose!;
        var live = groups.Single(g => g.Indices.Contains(1)).Pose!;
        Assert.AreEqual(2.5f, frozen.BoneModelMatrices[500].M43, 0.0001f);
        Assert.AreEqual(7.5f, live.BoneModelMatrices[500].M43, 0.0001f);
        Assert.AreNotSame(frozen.BoneModelMatrices, live.BoneModelMatrices);
        var replacement = Animation(2, 1);
        groups = packet.BuildAnimationGroups(replacement, model.submeshes, 500,
            Matrix4x4.Identity, [0, 1]);
        Assert.AreEqual(2, groups[0].Pose!.BoneModelMatrices.Length);
        Assert.AreEqual(2.5f, frozen.BoneModelMatrices[500].M43, 0.0001f);
    }

    [TestMethod]
    [DataRow(0x08)]
    [DataRow(0x10)]
    [DataRow(0x20)]
    [DataRow(0x40)]
    public void NativeBillboardsKeepViewPivotAxisLengthsAndInheritedChildren(int mask)
    {
        var pivot = new Vector3(2, 3, 4);
        var animation = Billboard((uint)mask, pivot);
        var root = Matrix4x4.CreateScale(2, 3, 4) * Matrix4x4.CreateRotationY(0.7f)
            * Matrix4x4.CreateTranslation(5, -7, 11);
        var pose = Pose(animation, root);
        var viewBone = pose.BoneModelMatrices[0] * root;
        Near(Vector3.Transform(pivot, root), Vector3.Transform(pivot, viewBone));
        for (var axis = 0; axis < 3; axis++)
            Assert.AreEqual(Axis(root, axis).Length(), Axis(viewBone, axis).Length(), 0.0001f);
        if (mask == 8)
        {
            Near(new(0, 0, -2), Axis(viewBone, 0));
            Near(new(3, 0, 0), Axis(viewBone, 1));
            Near(new(0, 4, 0), Axis(viewBone, 2));
        }
        else
        {
            var locked = mask == 0x10 ? 0 : mask == 0x20 ? 1 : 2;
            Near(Axis(root, locked), Axis(viewBone, locked));
            Assert.AreEqual(0f, Axis(viewBone, locked == 1 ? 0 : 1).Z, 0.0001f);
        }
        Near(Axis(viewBone, 0), Axis(pose.BoneModelMatrices[1] * root, 0));
    }

    [TestMethod]
    [DataRow(0.000244140625f, false)]
    [DataRow(0.00048828125f, false)]
    [DataRow(0.0009765625f, true)]
    public void LockedBillboardUsesNativeStrictSquaredNormalizationCutoff(float scale, bool normalized)
    {
        var root = Matrix4x4.CreateScale(scale, 2, 3);
        var pose = Pose(Billboard(0x10, Vector3.Zero), root);
        var viewBone = pose.BoneModelMatrices[0] * root;
        Assert.AreEqual(normalized ? scale : scale * scale, viewBone.M11, 1e-10f);
        Assert.AreEqual(normalized ? -2 : -2 * scale, viewBone.M22, 1e-7f);
    }

    [TestMethod]
    [DataRow(2, 2f, 3f, 4f)]
    [DataRow(4, 10f, 18f, 28f)]
    [DataRow(6, 2f, 3f, 4f)]
    public void ParentInheritanceModesAreExclusiveAndPinTheAuthoredPivot(int mode, float x, float y, float z)
    {
        var empty = Track<Vector3>();
        var pivot = new Vector3(1, 2, 3);
        var animation = new M2Animation
        {
            Bones = [new(-1, 0x200, Vector3.Zero, empty, Track<Quaternion>(),
                        Track(new M2Timeline<Vector3>([0], [new(5, 6, 7)]))),
                     new(0, (uint)mode, pivot, empty, Track<Quaternion>(), empty)],
            Sequences = [], GlobalLoops = [], HasAnimatedBones = true
        };
        Assert.IsTrue(animation.HasViewDependentBones);
        var root = Matrix4x4.CreateScale(2, 3, 4) * Matrix4x4.CreateTranslation(8, 9, 10);
        var pose = Pose(animation, root);
        var parent = pose.BoneModelMatrices[0] * root;
        var child = pose.BoneModelMatrices[1] * root;
        Near(new(x, 0, 0), Axis(child, 0));
        Near(new(0, y, 0), Axis(child, 1));
        Near(new(0, 0, z), Axis(child, 2));
        Near(Vector3.Transform(pivot, parent), Vector3.Transform(pivot, child));
    }

    [TestMethod]
    public void RootRelativeParentModeReadsScaledViewAxesAfterParentRotation()
    {
        var empty = Track<Vector3>();
        var animation = new M2Animation
        {
            Bones = [new(-1, 0x200, Vector3.Zero, empty,
                Track(new M2Timeline<Quaternion>([0], [Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)])), empty),
                new(0, 4, Vector3.Zero, empty, Track<Quaternion>(), empty)],
            Sequences = [], GlobalLoops = [], HasAnimatedBones = true
        };
        var root = Matrix4x4.CreateScale(2, 3, 4);
        var child = Pose(animation, root).BoneModelMatrices[1] * root;
        Near(new(3, 0, 0), Axis(child, 0));
        Near(new(0, 2, 0), Axis(child, 1));
    }

    [TestMethod]
    public void PlacementAndCameraChangesRefreshBillboardKeysAtTheSameAnimationTime()
    {
        var animation = Billboard(8, new(2, 0, 0));
        var model = Model(animation, 0);
        var placement = Placement(7, Vector3.Zero);
        var packet = Packet(model, placement);
        var pose = packet.BuildAnimationGroups(animation, model.submeshes, 0,
            Matrix4x4.Identity, [0])[0].Pose!;
        var old = Build(model, packet, pose).Distance;
        placement.Scale = 3;
        placement.Position = new(0, 0, 10);
        packet.RefreshSpatialData(0, placement, model);
        var view = Matrix4x4.CreateTranslation(0, 0, -4);
        var next = packet.BuildAnimationGroups(animation, model.submeshes, 0, view, [0])[0].Pose!;
        Assert.AreNotSame(pose, next);
        var current = Build(model, packet, next, view: view);
        Assert.AreNotEqual(old, current.Distance);
        Near(Vector3.Transform(new Vector3(2, 0, 0), packet.WorldMatrices[0] * view),
            Vector3.Transform(new Vector3(2, 0, 0), next.BoneModelMatrices[0] * packet.WorldMatrices[0] * view));
    }

    [TestMethod]
    public void RigidEffectChainCanExceedGpuPaletteAndPreservesNoncommutingComposition()
    {
        var animation = Animation(501, 500);
        var full = new Matrix4x4[501];
        animation.Evaluate(0, 500, full);
        var rigid = animation.EvaluateRigidBone(500, 0, 500);
        Assert.AreEqual(full[500], rigid);
        var empty = Track<Vector3>();
        var rotated = new M2Animation
        {
            Bones = [new(-1, 0x200, Vector3.Zero, empty,
                Track(new M2Timeline<Quaternion>([0], [Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.8f)])), empty),
                new(0, 0x200, Vector3.Zero, Track(new M2Timeline<Vector3>([0], [new(2, 0, 3)])), Track<Quaternion>(), empty)],
            Sequences = [], GlobalLoops = [], HasAnimatedBones = true
        };
        rotated.Evaluate(0, 0, full);
        var expected = Vector3.Transform(new(2, 0, 3), Matrix4x4.CreateRotationY(0.8f));
        Near(expected, Vector3.Transform(Vector3.Zero, rotated.EvaluateRigidBone(1, 0, 0)));
    }

    [TestMethod]
    public void RetainedIdentitiesFollowObjectsAndLoadedGenerationsThroughPacketReordering()
    {
        var model = Model(Animation(1, 0), 0);
        var a = Placement(7, Vector3.Zero);
        var b = Placement(7, Vector3.Zero);
        var packet = Packet(model, a, b);
        var first = Build(model, packet, null, translucent: false);
        var second = Build(model, packet, null, index: 1, translucent: false);
        Assert.AreNotEqual(first.ModelIdentity, second.ModelIdentity);
        Assert.AreEqual(first.SharedModelIdentity, second.SharedModelIdentity);
        Assert.AreEqual(first.BatchIdentity, second.BatchIdentity);
        packet.Instances.Reverse(); packet.Invalidate(); packet.EnsureSpatialData(model);
        Assert.AreEqual(first.ModelIdentity, Build(model, packet, null, index: 1, translucent: false).ModelIdentity);
        var reload = model;
        reload.submeshes = (Submesh[])model.submeshes.Clone();
        var reloaded = Build(reload, packet, null, index: 1, translucent: false);
        Assert.AreEqual(first.ModelIdentity, reloaded.ModelIdentity);
        Assert.AreNotEqual(first.SharedModelIdentity, reloaded.SharedModelIdentity);
        Assert.AreNotEqual(first.BatchIdentity, reloaded.BatchIdentity);
        var fresh = Packet(model, Placement(7, Vector3.Zero));
        Assert.AreNotEqual(first.ModelIdentity, Build(model, fresh, null, translucent: false).ModelIdentity);
        Assert.AreEqual(first.SharedModelIdentity, Build(model, fresh, null, translucent: false).SharedModelIdentity);
    }

    [TestMethod]
    public void WmoParentMotionRefreshesPoseAndModelOriginWithoutInheritingACm2ParentKey()
    {
        var parent = (WMOContainer)RuntimeHelpers.GetUninitializedObject(typeof(WMOContainer));
        parent.ActiveDoodads = [];
        parent.Scale = 1;
        var placement = Placement(7, Vector3.Zero);
        placement.ParentWMO = parent; placement.LocalScale = 2;
        placement.LocalRotation = Quaternion.Identity;
        parent.ActiveDoodads.Add(placement);
        var animation = Billboard(8, Vector3.UnitX);
        var model = Model(animation, 0);
        var packet = Packet(model, placement);
        var firstPose = packet.BuildAnimationGroups(animation, model.submeshes, 0,
            Matrix4x4.Identity, [0])[0].Pose!;
        var first = Build(model, packet, firstPose, translucent: false);
        parent.Position = new(3, 4, 0);
        packet.RefreshSpatialData(0, placement, model);
        var nextPose = packet.BuildAnimationGroups(animation, model.submeshes, 0,
            Matrix4x4.Identity, [0])[0].Pose!;
        var next = Build(model, packet, nextPose, translucent: false);
        Assert.AreNotSame(firstPose, nextPose);
        Assert.AreEqual(new Wrath335M2DistanceKeys(25, 25), next.Distance);
        Assert.AreEqual(first.ModelIdentity, next.ModelIdentity);
        Assert.AreEqual(first.SharedModelIdentity, next.SharedModelIdentity);
    }

    [TestMethod]
    public void LiveAdapterRetainsBatchAddressOrderAndResolvedShaderTextureInputs()
    {
        var model = Model(Animation(1, 0), 0);
        model.submeshes = [model.submeshes[0], model.submeshes[0]];
        var packet = Packet(model, Placement(7, Vector3.Zero));
        uint[] textures = [12, 16];
        var shader = new Wrath335M2ShaderSortKey(101, 202);
        Assert.IsTrue(Wrath335M2RetainedMeshSortAdapter.TryBuild(model, packet, 0, 1, null,
            Matrix4x4.Identity, false, false, false, shader, textures, 6, 4, out var second));
        var first = Build(model, packet, null, translucent: false);
        Assert.AreEqual(first.SharedModelIdentity, second.SharedModelIdentity);
        Assert.IsTrue(first.BatchIdentity < second.BatchIdentity);
        Assert.AreEqual(shader, second.Shader);
        CollectionAssert.AreEqual(textures, second.TextureIdentities.ToArray());
        Assert.AreEqual((ushort)6, second.BlendMode);
        Assert.AreEqual(4u, second.Flags);
        Assert.AreEqual(-3, second.PriorityPlane);
        Assert.AreEqual((ushort)2, second.MaterialLayer);
    }

    [TestMethod]
    public void ResolvedTextureHandlesShareWhileRetiredAndReusedAddressesGetNewGenerations()
    {
        var resources = new Wrath335M2RetainedHandleIdentities();
        var first = resources.Resolve(0x1000);
        Assert.AreEqual(first, resources.Resolve(0x1000));
        Assert.AreNotEqual(first, resources.Resolve(0x2000));
        Assert.AreEqual(0u, resources.Resolve(0));
        resources.Retire(0x1000);
        var reloaded = resources.Resolve(0x1000);
        Assert.AreNotEqual(first, reloaded);
        resources.Clear();
        Assert.AreNotEqual(reloaded, resources.Resolve(0x1000));
    }

    [TestMethod]
    public void LiveAdapterKeepsCm2DistanceParentSeparateFromPlacementTransformParent()
    {
        var model = Model(Animation(1, 0), 0);
        var packet = Packet(model, Placement(7, new(0, 0, 10)));
        Assert.IsTrue(Wrath335M2RetainedMeshSortAdapter.TryBuild(model, packet, 0, 0, null,
            Matrix4x4.Identity, false, false, false, null, default, 0, 0, out var element, 49));
        Assert.AreEqual(new Wrath335M2DistanceKeys(49, 49), element.Distance);
        Assert.IsTrue(Wrath335M2RetainedMeshSortAdapter.TryBuild(model, packet, 0, 0, null,
            Matrix4x4.Identity, false, false, false, null, default, 0, 0, out element, 49, true));
        Assert.AreEqual(new Wrath335M2DistanceKeys(100, 100), element.Distance);
        Assert.IsFalse(Wrath335M2RetainedMeshSortAdapter.TryBuild(model, packet, 0, 0, null,
            Matrix4x4.Identity, true, false, false, null, default, 2, 0, out _));
    }

    private static M2AnimationPose Pose(M2Animation animation, Matrix4x4 root)
    {
        var cache = new M2AnimationPoseCache(); cache.BeginFrame(animation, 0, true);
        return cache.GetPose(animation, new(new(0, 0), 0, root), [], true)!;
    }

    private static M2Animation Animation(int count, int movingBone)
    {
        var empty = Track<Vector3>();
        var bones = Enumerable.Range(0, count).Select(i => new M2Bone(i - 1,
            i == movingBone ? 0x200u : 0, Vector3.Zero,
            i == movingBone ? Track(new M2Timeline<Vector3>([0, 1000], [Vector3.Zero, new(0, 0, 10)])) : empty,
            Track<Quaternion>(), empty)).ToArray();
        return new() { Bones = bones, Sequences = [new(1000, 0)], GlobalLoops = [], HasAnimatedBones = true };
    }

    private static M2Animation Billboard(uint mask, Vector3 pivot)
    {
        var empty = Track<Vector3>();
        return new() { Bones = [new(-1, mask, pivot, empty, Track<Quaternion>(), empty),
            new(0, 0, Vector3.Zero, empty, Track<Quaternion>(), empty)],
            Sequences = [], GlobalLoops = [], HasAnimatedBones = true, HasBillboardBones = true };
    }

    private static M2Track<T> Track<T>(params M2Timeline<T>[] timelines) =>
        new() { Interpolation = 1, GlobalSequence = -1, Timelines = timelines };

    private static ParsedDoodadBatch Model(M2Animation animation, ushort center) => new()
    {
        fileDataID = 7, animation = animation, boundingRadius = 1,
        submeshes = [new() { wrath335Sort = new(0, -3, 2, center, 1, new(0, 0, 5), 2, false) }]
    };

    private static M2Container Placement(uint id, Vector3 position)
    {
        var result = (M2Container)RuntimeHelpers.GetUninitializedObject(typeof(M2Container));
        typeof(M2Container).GetField("<AnimationState>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(result, new M2InstanceAnimationState());
        result.FileDataId = id; result.Scale = 1; result.Position = position;
        return result;
    }

    private static M2InstancePacket Packet(ParsedDoodadBatch model, params M2Container[] placements)
    {
        var packet = new M2InstancePacket(placements.ToList());
        Assert.IsTrue(packet.EnsureSpatialData(model)); return packet;
    }

    private static Wrath335M2ElementSortData Build(ParsedDoodadBatch model, M2InstancePacket packet,
        M2AnimationPose? pose, int index = 0, bool translucent = true, Matrix4x4? view = null)
    {
        Assert.IsTrue(Wrath335M2RetainedMeshSortAdapter.TryBuild(model, packet, index, 0, pose,
            view ?? Matrix4x4.Identity, translucent, false, false, null, default, 2, 0, out var element));
        return element;
    }

    private static Vector3 Axis(Matrix4x4 m, int axis) => axis switch
    { 0 => new(m.M11, m.M12, m.M13), 1 => new(m.M21, m.M22, m.M23), _ => new(m.M31, m.M32, m.M33) };
    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.IsTrue(Vector3.Distance(expected, actual) < 0.0001f, $"Expected {expected}, actual {actual}");
}
