using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335DoodadFadeTests
{
    [DataTestMethod]
    [DataRow(1f, 0)]
    [DataRow(1.00001f, 1)]
    [DataRow(4f, 1)]
    [DataRow(4.00001f, 2)]
    [DataRow(15f, 2)]
    [DataRow(15.00001f, 3)]
    [DataRow(100f, 3)]
    [DataRow(100.00001f, 4)]
    public void LargestWorldBoxExtentUsesInclusiveCategoryBoundaries(float extent, int category)
    {
        Assert.AreEqual((byte)category, Wrath335DoodadFade.SizeCategory(new(Vector3.Zero, new(extent, 0, 0))));
        Assert.AreEqual((byte)category, Wrath335DoodadFade.SizeCategory(new(Vector3.Zero, new(0, extent, 0))));
        Assert.AreEqual((byte)category, Wrath335DoodadFade.SizeCategory(new(Vector3.Zero, new(0, 0, extent))));
    }

    [TestMethod]
    public void TransformedBoxCanChangeCategoryWithoutChangingItsSphereRadius()
    {
        var local = new BoundingBox(new(-0.4f, -0.4f, 0), new(0.4f, 0.4f, 0));
        Assert.AreEqual((byte)0, Wrath335DoodadFade.SizeCategory(local));
        var rotated = BoundingBox.Transform(local, Matrix4x4.CreateRotationZ(MathF.PI / 4));
        Assert.AreEqual((byte)1, Wrath335DoodadFade.SizeCategory(rotated));
        var scaled = BoundingBox.Transform(local, Matrix4x4.CreateScale(6));
        Assert.AreEqual((byte)2, Wrath335DoodadFade.SizeCategory(scaled));
    }

    [DataTestMethod]
    [DataRow(0f, 0.5f)]
    [DataRow(0.5f, 0.5f)]
    [DataRow(1f, 1f)]
    [DataRow(1.5f, 1.5f)]
    [DataRow(2f, 1.5f)]
    public void DetailScalesOnlyMiddleCategoriesAndKeepsFadeRangesFixed(float requested, float detail)
    {
        var profile = new Wrath335DoodadFade(requested);
        Assert.AreEqual(detail, profile.EnvironmentDetail);
        var maximum = new[] { 30f, 100f * detail, 200f * detail, 750f * detail, 1250f };
        var ranges = new[] { 5f, 10f, 15f, 20f, 50f };
        for (byte i = 0; i < 5; i++)
        {
            Assert.IsTrue(profile.TryGetOpacity(i, Math.Pow(maximum[i] - ranges[i], 2), out var opacity));
            Assert.AreEqual(1f, opacity);
            Assert.IsTrue(profile.TryGetOpacity(i, Math.Pow(maximum[i] - ranges[i] / 2, 2), out opacity));
            Assert.AreEqual(0.5f, opacity, 0.00001f);
            Assert.IsFalse(profile.TryGetOpacity(i, Math.Pow(maximum[i], 2), out _));
        }
    }

    [DataTestMethod]
    [DataRow(-1f, 0)]
    [DataRow(29.999f, 0)]
    [DataRow(30f, 1)]
    [DataRow(99.999f, 1)]
    [DataRow(100f, 2)]
    [DataRow(200f, 3)]
    [DataRow(750f, 4)]
    public void DistanceClassificationAdvancesAtEquality(float distance, int minimum)
    {
        Assert.AreEqual((byte)minimum, new Wrath335DoodadFade(1).MinimumCategory(distance));
    }

    [TestMethod]
    public void FadeSnapAndDropThresholdsUseStoredFloatConstants()
    {
        var profile = new Wrath335DoodadFade(1);
        Assert.IsTrue(profile.TryGetOpacity(0, 25.04 * 25.04, out var opacity));
        Assert.AreEqual(1f, opacity);
        Assert.IsTrue(profile.TryGetOpacity(0, 25.06 * 25.06, out opacity));
        Assert.AreEqual(0.988f, opacity, 0.000001f);
        Assert.IsTrue(profile.TryGetOpacity(0, 29.94 * 29.94, out opacity));
        Assert.AreEqual(0.012f, opacity, 0.000001f);
        Assert.IsFalse(profile.TryGetOpacity(0, 29.96 * 29.96, out _));
        Assert.IsFalse(profile.TryGetOpacity(0, 901, out _));
        Assert.IsTrue(profile.TryGetOpacity(0, 901, out opacity, bypassDistance: true));
        Assert.AreEqual(1f, opacity);
        Assert.IsTrue(new Wrath335DoodadFade(1, false).TryGetOpacity(0, 901, out opacity));
        Assert.AreEqual(1f, opacity);
    }

    [TestMethod]
    public void ThresholdSquaresRetainTheProductBeforeItsFloatStore()
    {
        var profile = new Wrath335DoodadFade(1.0000001f);
        Assert.AreEqual((byte)3, profile.MinimumCategory(750.00006103515625f));
        Assert.IsTrue(profile.TryGetOpacity(3, 730.00006103515625d * 730.00006103515625d, out var opacity));
        Assert.AreEqual(1f, opacity);
    }

    [TestMethod]
    public void SseSquareRootRoundsTheSquaredDistanceBeforeTakingTheRoot()
    {
        const double distance = 27.5000002;
        var profile = new Wrath335DoodadFade(1);
        Assert.IsTrue(profile.TryGetOpacity(0, distance * distance, out var sse));
        Assert.IsTrue(profile.TryGetOpacity(0, distance * distance, out var x87, useSseSquareRoot: false));
        Assert.AreEqual(0.5f, sse);
        Assert.IsTrue(x87 < sse);
    }

    [TestMethod]
    public void CenterDistanceAndGroupDepthKeepDifferentCameraContracts()
    {
        Assert.AreEqual(169d, Wrath335DoodadFade.DistanceSquared(new(13, 24, 42), new(10, 20, 30)));
        var box = new BoundingBox(new(-5, -5, 80), new(5, 5, 90));
        Assert.AreEqual(70f, Wrath335DoodadFade.GroupDistance(box, new(0, 0, 10), new(0, 0, 2)));
        Assert.AreEqual(20f, Wrath335DoodadFade.GroupDistance(box, new(0, 0, 110), -Vector3.UnitZ));
    }

    [TestMethod]
    public void ProfileAndSettingsCloneAreIsolatedToReferenceClientRules()
    {
        var settings = new RendererSettings { EnvironmentDetail = 0.75f, ObjectFade = false };
        Assert.IsNull(Wrath335DoodadFade.Resolve(settings, false));
        Assert.IsFalse(Wrath335DoodadFade.Resolve(settings, true)!.Value.ObjectFade);
        Assert.AreEqual(0.75f, settings.Clone().EnvironmentDetail);
        Assert.IsFalse(settings.Clone().ObjectFade);
        settings.UseClientRenderingRules = false;
        Assert.IsNull(Wrath335DoodadFade.Resolve(settings, true));
    }

    [TestMethod]
    public void FirstGroupCallbackKeepsItsDepthAndUndersizedOwnerCanRetryAnotherOwner()
    {
        var groups = new[] { Group(), Group() };
        var visibility = Begin(groups, new(1, false));
        var sphere = new BoundingSphere(new(0, 0, 0.5f), 0.01f);
        visibility.SetExteriorSphere(0, sphere, Small);
        visibility.RecordCallback(0, WmoPortalRect.Full, new(new(0, 0, 100), new(1, 1, 101)));
        visibility.RecordCallback(0, WmoPortalRect.Full, Small); // Does not overwrite first depth.
        visibility.RecordCallback(1, WmoPortalRect.Full, Small);
        visibility.Finish(groups, [true, false], true);
        Assert.IsTrue(visibility.Accept(0, sphere));
        Assert.IsTrue(visibility.TryGetCurrentFog(0, out var current));
        Assert.IsFalse(current); // The close second owner writes staged fog.
    }

    [TestMethod]
    public void UndersizedBucketLeavesDefinitionPendingEvenWithObjectFadeDisabled()
    {
        var groups = new[] { Group() };
        var visibility = Begin(groups, new(1, false));
        var sphere = new BoundingSphere(new(0, 0, 0.5f), 0.01f);
        visibility.SetExteriorSphere(0, sphere, Small);
        visibility.AdmitExterior(0, sphere, Wrath335DoodadFrustum.Create(Matrix4x4.Identity, WmoPortalRect.Full),
            new(), new(), 30);
        visibility.RecordCallback(0, WmoPortalRect.Full, Small);
        visibility.Finish(groups, [true], true);
        Assert.IsTrue(visibility.Accept(0, sphere));
        Assert.IsTrue(visibility.TryGetCurrentFog(0, out var current));
        Assert.IsTrue(current); // Bucket must not consume the definition before this write.
    }

    [TestMethod]
    public void SubmissionRejectionPreservesPortalFogForTheNextExteriorFrame()
    {
        var groups = new[] { Group() };
        var visibility = Begin(groups, new(1), new(0, 0, -40));
        var sphere = new BoundingSphere(new(0, 0, 0.5f), 0.01f);
        visibility.SetExteriorSphere(0, sphere, Small);
        visibility.RecordCallback(0, WmoPortalRect.Full, new(new(0, 0, -40), new(1, 1, -39)));
        visibility.Finish(groups, [true], true);
        Assert.IsFalse(visibility.Accept(0, sphere));
        Assert.IsTrue(visibility.TryGetCurrentFog(0, out var current));
        Assert.IsTrue(current);
        visibility.Begin(groups, 1, Matrix4x4.Identity);
        visibility.ConfigureFade(new(1), Vector3.Zero, Vector3.UnitZ);
        visibility.SetExteriorSphere(0, sphere, Small);
        visibility.AdmitExterior(0, sphere, Wrath335DoodadFrustum.Create(Matrix4x4.Identity, WmoPortalRect.Full), new(), new());
        visibility.Finish(groups, [false], false);
        Assert.IsTrue(visibility.Accept(0, sphere));
        Assert.IsTrue(visibility.TryGetCurrentFog(0, out current));
        Assert.IsTrue(current);
    }

    [TestMethod]
    public void AcceptedSubmissionRetainsCalculatedOpacityAndUsesSphereCenter()
    {
        var groups = new[] { Group() };
        var visibility = Begin(groups, new(1), new(0, 0, -27));
        var sphere = new BoundingSphere(new(0, 0, 0.5f), 10f);
        visibility.SetExteriorSphere(0, sphere, Small);
        visibility.RecordCallback(0, WmoPortalRect.Full, new(new(0, 0, -27), new(1, 1, -26)));
        visibility.Finish(groups, [false], true);
        Assert.IsTrue(visibility.Accept(0, sphere));
        Assert.AreEqual(0.5f, visibility.GetSubmissionOpacity(0)); // Radius is not subtracted.
        visibility.Clear();
        Assert.AreEqual(1f, visibility.GetSubmissionOpacity(0));
    }

    private static readonly BoundingBox Small = new(Vector3.Zero, Vector3.One);
    private static WorldModelGroupBatches Group() => new() { doodadReferences = [0] };
    private static Wrath335WmoDoodadVisibility Begin(WorldModelGroupBatches[] groups, Wrath335DoodadFade profile,
        Vector3 eye = default)
    {
        var visibility = new Wrath335WmoDoodadVisibility();
        visibility.Begin(groups, 1, Matrix4x4.Identity);
        visibility.ConfigureFade(profile, eye, Vector3.UnitZ);
        return visibility;
    }
}
