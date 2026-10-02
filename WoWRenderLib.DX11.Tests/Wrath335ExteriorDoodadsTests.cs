using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Raycasting;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335ExteriorDoodadsTests
{
    private static readonly WorldModelGroupBatches[] Groups = [new() { doodadReferences = [0] }];

    [DataTestMethod]
    [DataRow(-10f, 1f, 7, 7)]
    [DataRow(101f, 1f, 0, 2)] // depth=100: 3-.5 rounds to even 2.
    [DataRow(101f, 1f, 5, 5)]
    [DataRow(102f, 1f, 0, 3)]
    [DataRow(201f, 1f, 0, 6)] // depth=200: 6-.5 rounds to even 6.
    public void SphereNearDepthUsesRadiusAndOriginatingBucketFloor(float center, float radius, int origin, int expected)
    {
        Assert.IsTrue(Wrath335ExteriorDoodads.TryGetBucket(new(new(center, 0, 0), radius),
            new(1, 0, 0, 0), origin, out var bucket));
        Assert.AreEqual(expected, bucket);
    }

    [DataTestMethod]
    [DataRow(2133f, true, 63)]
    [DataRow(2134f, false, 0)]
    public void UnsignedSixtyFourCutoffPrecedesOriginatingBucketFloor(float depth, bool accepted, int expected)
    {
        Assert.AreEqual(accepted, Wrath335ExteriorDoodads.TryGetBucket(new(new(depth, 0, 0), 0),
            new(1, 0, 0, 0), 0, out var bucket));
        if (accepted) Assert.AreEqual(expected, bucket);
    }

    [TestMethod]
    public void BucketPlaneUsesHorizontalCameraTranslationAndIgnoresHeight()
    {
        var plane = Wrath335ExteriorGroupOrder.HorizontalDepthPlane(new(10, 20, 30), new(0, 4, 3));
        Assert.IsTrue(Wrath335ExteriorDoodads.TryGetBucket(new(new(500, 121, 9000), 1), plane, 0, out var bucket));
        Assert.AreEqual(2, bucket);
        Assert.IsFalse(Wrath335ExteriorDoodads.TryGetBucket(new(new(float.NaN, 0, 0), 1), plane, 0, out _));
    }

    [TestMethod]
    public void DelayedBucketDoesNotConsumeAtGroupBandAndSharedOwnersCannotMoveALinkedDefinitionEarlier()
    {
        var visibility = Begin();
        var sphere = new BoundingSphere(new(101, 0, 0.5f), 1);
        visibility.SetExteriorSphere(0, sphere);
        var queue = Queue(Matrix4x4.CreateScale(0.001f, 1, 1));
        queue.Enlist(visibility, [0], 5);
        queue.Enlist(visibility, [0, ushort.MaxValue], 0);
        queue.Consume(2, new(), new());
        visibility.Finish(Groups, [false], false);
        Assert.IsFalse(visibility.Accept(0, sphere));
        queue.Consume(5, new(), new());
        Assert.IsTrue(visibility.Accept(0, sphere));
    }

    [TestMethod]
    public void MissingLoadedSphereAndDepthCutoffLeavePortalOwnersEligible()
    {
        var visibility = Begin();
        var queue = Queue(Matrix4x4.Identity);
        queue.Enlist(visibility, [0], 0); // no loaded-model sphere
        queue.Consume(0, new(), new());
        visibility.SetExteriorSphere(0, new(new(3000, 0, 0.5f), 0));
        queue.Enlist(visibility, [0], 0); // cutoff does not stamp pending
        queue.Consume(0, new(), new());
        visibility.RecordCallback(0, WmoPortalRect.Full);
        visibility.Finish(Groups, [true], true);
        Assert.IsTrue(visibility.Accept(0, new(new(0, 0, 0.5f), 0.01f)));
        Assert.IsTrue(visibility.TryGetCurrentFog(0, out var current));
        Assert.IsTrue(current);
    }

    [TestMethod]
    public void FrustumRejectionLeavesDefinitionPendingForAWiderPortalOwner()
    {
        var sphere = new BoundingSphere(new(0.5f, 0, 0.5f), 0.01f);
        var visibility = Begin();
        visibility.SetExteriorSphere(0, sphere);
        var queue = new Wrath335ExteriorDoodads();
        queue.Begin(Vector3.Zero, Vector3.UnitX, Matrix4x4.Identity, new(-0.1f, -0.1f, 0.1f, 0.1f));
        queue.Enlist(visibility, [0], 0);
        queue.Consume(0, new(), new());
        visibility.RecordCallback(0, WmoPortalRect.Full);
        visibility.Finish(Groups, [true], true);
        Assert.IsTrue(visibility.Accept(0, sphere));
        Assert.IsTrue(visibility.TryGetCurrentFog(0, out var current));
        Assert.IsTrue(current);
    }

    [TestMethod]
    public void StaticVolumeFailureLeavesPortalEligibilityWhileTerrainRejectionConsumesIt()
    {
        var sphere = new BoundingSphere(new(0, 0, 100), 0.1f);
        var camera = Matrix4x4.CreateScale(1, 1, 0.005f);
        var volumes = new Wrath335ClipVolumes();
        volumes.AddPolygon(Vector3.Zero, [new(-10, -10, 50), new(10, -10, 50),
            new(10, 10, 50), new(-10, 10, 50)], Vector3.UnitZ, 0, false);
        Assert.IsTrue(volumes.ContainsSphere(new(sphere.Center, sphere.Radius)));
        var visibility = Begin(camera);
        visibility.SetExteriorSphere(0, sphere);
        var queue = Queue(camera);
        queue.Enlist(visibility, [0], 0);
        queue.Consume(0, volumes, new());
        visibility.RecordCallback(0, WmoPortalRect.Full);
        visibility.Finish(Groups, [true], true);
        Assert.IsTrue(visibility.Accept(0, sphere));

        visibility.Begin(Groups, 1, camera);
        visibility.SetExteriorSphere(0, sphere);
        var terrain = Horizon();
        Assert.IsTrue(terrain.ContainsSphere(sphere));
        queue.Begin(Vector3.Zero, Vector3.UnitX, camera, WmoPortalRect.Full);
        queue.Enlist(visibility, [0], 0);
        queue.Consume(0, new(), terrain);
        visibility.RecordCallback(0, WmoPortalRect.Full);
        visibility.Finish(Groups, [true], true);
        Assert.IsFalse(visibility.Accept(0, sphere));
        Assert.IsFalse(visibility.TryGetCurrentFog(0, out _));
    }

    [TestMethod]
    public void CurrentBandHorizonUpdateCannotRetroactivelyRejectAnAcceptedDoodad()
    {
        var sphere = new BoundingSphere(new(0, 0, 100), 0.1f);
        var camera = Matrix4x4.CreateScale(1, 1, 0.005f);
        var visibility = Begin(camera);
        visibility.SetExteriorSphere(0, sphere);
        var queue = Queue(camera);
        var terrain = new Wrath335TerrainClipBuffer();
        terrain.BeginProjected(Matrix4x4.Identity, 0);
        queue.Enlist(visibility, [0], 0);
        queue.Consume(0, new(), terrain);
        terrain.UpdateProtectedLine(new(-100, 20, 100), new(100, 20, 100));
        Assert.IsTrue(terrain.ContainsSphere(sphere));
        // Later owners may re-enlist an unlinked definition, but cannot undo submission.
        queue.Enlist(visibility, [0], 1);
        queue.Consume(1, new(), terrain);
        visibility.Finish(Groups, [false], false);
        Assert.IsTrue(visibility.Accept(0, sphere));
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ExteriorAdmissionRetainsLastPortalFogBankAcrossFramesAndClear(bool propagated)
    {
        var sphere = new BoundingSphere(new(0, 0, 0.5f), 0.01f);
        var visibility = Begin();
        visibility.SetExteriorSphere(0, sphere);
        visibility.RecordCallback(0, WmoPortalRect.Full);
        visibility.Finish(Groups, [propagated], true); // Prepares fog even before an M2 draw query.
        Assert.IsTrue(visibility.TryGetCurrentFog(0, out var current));
        Assert.AreEqual(propagated, current);
        visibility.Clear();
        visibility.Begin(Groups, 1, Matrix4x4.Identity);
        visibility.SetExteriorSphere(0, sphere);
        Assert.IsFalse(visibility.TryGetCurrentFog(0, out _)); // frame result remains guarded
        var queue = Queue(Matrix4x4.Identity);
        queue.Enlist(visibility, [0], 0);
        queue.Consume(0, new(), new());
        visibility.RecordCallback(0, WmoPortalRect.Full);
        visibility.Finish(Groups, [!propagated], true); // Bucket consumes before final portal pass.
        Assert.IsTrue(visibility.Accept(0, sphere));
        Assert.IsTrue(visibility.TryGetCurrentFog(0, out current));
        Assert.AreEqual(propagated, current);
    }

    [TestMethod]
    public void ResourceReplacementDropsHistoryAndNewFrameDropsOldBucketAdmission()
    {
        var sphere = new BoundingSphere(new(0, 0, 0.5f), 0.01f);
        var visibility = Begin();
        visibility.RecordCallback(0, WmoPortalRect.Full);
        visibility.Finish(Groups, [true], true);
        Assert.IsTrue(visibility.Accept(0, sphere));
        var replaced = new[] { new WorldModelGroupBatches { doodadReferences = [0] } };
        visibility.Begin(replaced, 1, Matrix4x4.Identity);
        visibility.SetExteriorSphere(0, sphere);
        var queue = Queue(Matrix4x4.Identity);
        queue.Enlist(visibility, [0], 0);
        queue.Consume(0, new(), new());
        visibility.Finish(replaced, [true], false);
        Assert.IsTrue(visibility.Accept(0, sphere));
        Assert.IsTrue(visibility.TryGetCurrentFog(0, out var current));
        Assert.IsFalse(current);
        visibility.Begin(replaced, 1, Matrix4x4.Identity);
        visibility.Finish(replaced, [true], false);
        Assert.IsFalse(visibility.Accept(0, sphere));
    }

    private static Wrath335WmoDoodadVisibility Begin(Matrix4x4? camera = null)
    {
        var result = new Wrath335WmoDoodadVisibility();
        result.Begin(Groups, 1, camera ?? Matrix4x4.Identity);
        return result;
    }
    private static Wrath335ExteriorDoodads Queue(Matrix4x4 camera)
    {
        var result = new Wrath335ExteriorDoodads();
        result.Begin(Vector3.Zero, Vector3.UnitX, camera, WmoPortalRect.Full);
        return result;
    }
    private static Wrath335TerrainClipBuffer Horizon()
    {
        var result = new Wrath335TerrainClipBuffer();
        result.BeginProjected(Matrix4x4.Identity, 0);
        result.UpdateProtectedLine(new(-100, 20, 100), new(100, 20, 100));
        return result;
    }
}
