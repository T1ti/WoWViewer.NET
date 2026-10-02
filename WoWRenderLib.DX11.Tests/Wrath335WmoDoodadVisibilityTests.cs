using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335WmoDoodadVisibilityTests
{
    private static readonly WmoPortalRect Left = new(-0.8f, -0.2f, -0.4f, 0.2f);
    private static readonly WmoPortalRect Right = new(0.4f, -0.2f, 0.8f, 0.2f);

    [TestMethod]
    public void VisibleGroupRejectsOffWindowSphereAndUnreferencedDoodad()
    {
        var groups = new[] { Group(0) };
        var visibility = Begin(groups, 2);
        visibility.RecordCallback(0, Left);
        visibility.Finish(groups, [true], true);
        Assert.IsTrue(visibility.Accept(0, Sphere(-0.6f)));
        Assert.IsFalse(visibility.Accept(0, Sphere(0.6f)));
        Assert.IsFalse(visibility.Accept(1, Sphere(-0.6f)));
        Assert.IsFalse(visibility.TryGetCurrentFog(0, out _));
    }

    [TestMethod]
    public void TwoDisjointPortalWindowsAdmitEitherSphereButNotTheGap()
    {
        var groups = new[] { Group(0) };
        var visibility = Begin(groups);
        visibility.RecordCallback(0, Left);
        visibility.RecordCallback(0, Right);
        visibility.Finish(groups, [true], true);
        Assert.IsTrue(visibility.Accept(0, Sphere(-0.6f)));
        Assert.IsTrue(visibility.Accept(0, Sphere(0.6f)));
        Assert.IsFalse(visibility.Accept(0, Sphere(0)));
    }

    [TestMethod]
    public void TouchingSphereIsAcceptedEvenWhenItsCenterIsOutsideThePortal()
    {
        var frustum = Wrath335DoodadFrustum.Create(Matrix4x4.Identity, new(-0.25f, -0.25f, 0.25f, 0.25f));
        Assert.IsTrue(frustum.Intersects(new(new(0.375f, 0, 0.5f), 0.125f)));
        Assert.IsFalse(frustum.Intersects(new(new(0.37501f, 0, 0.5f), 0.125f)));
    }

    [DataTestMethod]
    [DataRow(-1.125f, 0f, 0.5f, -1.12501f, 0f, 0.5f)]
    [DataRow(1.125f, 0f, 0.5f, 1.12501f, 0f, 0.5f)]
    [DataRow(0f, -1.125f, 0.5f, 0f, -1.12501f, 0.5f)]
    [DataRow(0f, 1.125f, 0.5f, 0f, 1.12501f, 0.5f)]
    [DataRow(0f, 0f, -0.125f, 0f, 0f, -0.12501f)]
    [DataRow(0f, 0f, 1.125f, 0f, 0f, 1.12501f)]
    public void AllSixPlanesKeepInclusiveSphereBoundaries(float x, float y, float z, float outsideX, float outsideY, float outsideZ)
    {
        var frustum = Wrath335DoodadFrustum.Create(Matrix4x4.Identity, WmoPortalRect.Full);
        Assert.IsTrue(frustum.Intersects(new(new(x, y, z), 0.125f)));
        Assert.IsFalse(frustum.Intersects(new(new(outsideX, outsideY, outsideZ), 0.125f)));
    }

    [TestMethod]
    public void PerspectivePlanesUseWorldRadiusAndCameraTranslation()
    {
        var projection = Matrix4x4.CreateTranslation(-10, 0, 0) *
            Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2, 1, 0.1f, 10);
        var frustum = Wrath335DoodadFrustum.Create(projection, new(-0.2f, -0.2f, 0.2f, 0.2f));
        Assert.IsTrue(frustum.Intersects(new(new(8.9f, 0, -5), 0.11f)));
        Assert.IsFalse(frustum.Intersects(new(new(8.9f, 0, -5), 0.09f)));
        Assert.IsFalse(frustum.Intersects(new(new(10, 0, 1), 0.1f)));
    }

    [TestMethod]
    public void FirstCallbackArrivalWinsSharedReferenceFogAndRepeatedCallsStayStable()
    {
        var groups = new[] { Group(0), Group(0) };
        var visibility = Begin(groups);
        visibility.RecordCallback(1, WmoPortalRect.Full);
        visibility.RecordCallback(0, WmoPortalRect.Full);
        visibility.Finish(groups, [false, true], true);
        Assert.IsTrue(visibility.Accept(0, Sphere(0)));
        Assert.IsTrue(visibility.TryGetCurrentFog(0, out var current));
        Assert.IsTrue(current);
        Assert.IsTrue(visibility.Accept(0, Sphere(0)));
        Assert.IsTrue(visibility.TryGetCurrentFog(0, out current));
        Assert.IsTrue(current);
    }

    [TestMethod]
    public void RejectedFirstOwnerLeavesSharedDefinitionPendingForTheNextOwner()
    {
        var groups = new[] { Group(0), Group(0) };
        var visibility = Begin(groups);
        visibility.RecordCallback(0, Left);
        visibility.RecordCallback(1, Right);
        visibility.Finish(groups, [false, true], true);
        Assert.IsTrue(visibility.Accept(0, Sphere(0.6f)));
        Assert.IsTrue(visibility.TryGetCurrentFog(0, out var current));
        Assert.IsTrue(current);
    }

    [TestMethod]
    public void UnbucketedConsumerCannotUseAFrustumAddedAfterItsFogSnapshot()
    {
        var groups = new[] { Group(0), Group(0) };
        var visibility = Begin(groups);
        visibility.RecordCallback(0, Left);
        visibility.ConsumeGroup(0, false);
        visibility.RecordCallback(0, Right);
        visibility.RecordCallback(1, Right);
        visibility.ConsumeGroup(1, true);
        visibility.Finish(groups, [false, true], true);
        Assert.IsTrue(visibility.Accept(0, Sphere(0.6f)));
        Assert.IsTrue(visibility.TryGetCurrentFog(0, out var current));
        Assert.IsTrue(current);
        Assert.IsTrue(visibility.Accept(0, Sphere(-0.6f)));
        Assert.IsTrue(visibility.TryGetCurrentFog(0, out current));
        Assert.IsFalse(current);
    }

    [TestMethod]
    public void PrimaryPlacementIncludesExteriorAndAlwaysDrawCallbacksInFinalConsumer()
    {
        var groups = new[] { Group(0) with { mogiFlags = 8 }, Group(1) with { mogiFlags = 0x10000 } };
        var visibility = Begin(groups, 2);
        visibility.RecordCallback(0, WmoPortalRect.Full);
        visibility.RecordCallback(1, WmoPortalRect.Full);
        visibility.Finish(groups, [false, true], false);
        Assert.IsFalse(visibility.Accept(0, Sphere(0)));
        Assert.IsFalse(visibility.Accept(1, Sphere(0)));
        visibility.Begin(groups, 2, Matrix4x4.Identity);
        visibility.RecordCallback(0, WmoPortalRect.Full);
        visibility.RecordCallback(1, WmoPortalRect.Full);
        visibility.Finish(groups, [false, true], true);
        Assert.IsTrue(visibility.Accept(0, Sphere(0)));
        Assert.IsTrue(visibility.Accept(1, Sphere(0)));
    }

    [TestMethod]
    public void FirstExteriorBucketAdmissionUsesTheFreshDefinitionsStagedFog()
    {
        var groups = new[] { Group(0) with { mogiFlags = 8 }, Group(0) };
        var visibility = Begin(groups);
        visibility.SetExteriorSphere(0, Sphere(0));
        var queue = new Wrath335ExteriorDoodads();
        queue.Begin(Vector3.Zero, Vector3.UnitX, Matrix4x4.Identity, WmoPortalRect.Full);
        queue.Enlist(visibility, [0], 0);
        queue.Consume(0, new(), new());
        visibility.RecordCallback(1, WmoPortalRect.Full);
        visibility.Finish(groups, [false, true], false);
        Assert.IsTrue(visibility.Accept(0, Sphere(0)));
        Assert.IsTrue(visibility.TryGetCurrentFog(0, out var current));
        Assert.IsFalse(current);
    }

    [TestMethod]
    public void NewFrameDropsFogAndFrustaAndChangedResourceRebuildsOwnership()
    {
        var groups = new[] { Group(0) };
        var visibility = Begin(groups, 2);
        visibility.RecordCallback(0, WmoPortalRect.Full);
        visibility.Finish(groups, [true], true);
        Assert.IsTrue(visibility.Accept(0, Sphere(0)));
        visibility.Begin(groups, 2, Matrix4x4.Identity);
        Assert.IsFalse(visibility.TryGetCurrentFog(0, out _));
        visibility.Finish(groups, [false], true);
        Assert.IsFalse(visibility.Accept(0, Sphere(0)));
        var replaced = new[] { Group(1) };
        visibility.Begin(replaced, 2, Matrix4x4.Identity);
        visibility.RecordCallback(0, WmoPortalRect.Full);
        visibility.Finish(replaced, [false], true);
        Assert.IsFalse(visibility.Accept(0, Sphere(0)));
        Assert.IsTrue(visibility.Accept(1, Sphere(0)));
        Assert.IsTrue(visibility.TryGetCurrentFog(1, out var current));
        Assert.IsFalse(current);
    }

    [TestMethod]
    public void EmptyFrustumListAndInvalidModrNeverAdmitADefinition()
    {
        var groups = new[] { Group(0, ushort.MaxValue) };
        var visibility = Begin(groups);
        visibility.ConsumeGroup(0, true);
        visibility.Finish(groups, [true], true);
        Assert.IsFalse(visibility.Accept(0, Sphere(0)));
    }

    private static Wrath335WmoDoodadVisibility Begin(WorldModelGroupBatches[] groups, int count = 1)
    {
        var result = new Wrath335WmoDoodadVisibility();
        result.Begin(groups, count, Matrix4x4.Identity);
        return result;
    }
    private static WorldModelGroupBatches Group(params ushort[] references) => new() { doodadReferences = references };
    private static BoundingSphere Sphere(float x) => new(new(x, 0, 0.5f), 0.01f);
}
