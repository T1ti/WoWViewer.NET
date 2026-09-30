using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335PortalSceneViewsTests
{
    [TestMethod]
    public void ExteriorViewerGetsFullViewsAndResetDiscardsPreviousFrame()
    {
        var views = new Wrath335PortalSceneViews();
        views.Reset(false);
        Assert.AreEqual(WmoPortalRect.Full, views.SkyRect);
        Assert.AreEqual(WmoPortalRect.Full, views.ExteriorRect);
        views.Reset(true);
        Assert.IsFalse(views.HasSkyView);
        Assert.IsFalse(views.HasExteriorView);
        Assert.AreEqual(-1f, views.SkyDistance);
    }

    [TestMethod]
    public void ViewerRootSkyFlagsSeedSkyOnlyAndSecondaryDiscardsThatSeed()
    {
        var views = new Wrath335PortalSceneViews();
        foreach (var flag in new uint[] { 0x40, 0x100, 0x40000 })
        {
            views.Reset(true, flag);
            Assert.AreEqual(WmoPortalRect.Full, views.SkyRect);
            Assert.IsTrue(views.HasSkyView);
            Assert.IsFalse(views.HasExteriorView);
            views.Reset(true, flag, secondaryPlacement: true);
            Assert.IsFalse(views.HasSkyView);
        }
        views.Reset(true, 8);
        Assert.IsFalse(views.HasSkyView);
    }

    [TestMethod]
    public void DestinationFlagsKeepSkyAndExteriorUnionsSeparate()
    {
        var views = new Wrath335PortalSceneViews();
        views.Reset(true);
        var sky = new WmoPortalRect(-0.7f, -0.2f, -0.4f, 0.2f);
        var exterior = new WmoPortalRect(0.3f, -0.6f, 0.8f, 0.9f);
        views.AddPortal(sky, 17f, 0x40000);
        Assert.IsFalse(views.HasExteriorView);
        views.AddPortal(exterior, 12f, 8);
        Assert.AreEqual(new WmoPortalRect(-0.7f, -0.6f, 0.8f, 0.9f), views.SkyRect);
        Assert.AreEqual(exterior, views.ExteriorRect);
        Assert.AreEqual(17f, views.SkyDistance);
        Assert.AreEqual(12f, views.ExteriorDistance);
        views.AddPortal(WmoPortalRect.Full, 30f, 0);
        Assert.AreEqual(17f, views.SkyDistance);
    }

    [TestMethod]
    public void AlwaysDrawPortalEmitsExteriorButExteriorLightingEmitsSkyOnly()
    {
        var views = new Wrath335PortalSceneViews();
        views.Reset(true);
        views.AddPortal(WmoPortalRect.Full, 0f, 0x40);
        Assert.IsTrue(views.HasSkyView);
        Assert.IsFalse(views.HasExteriorView);
        views.AddPortal(WmoPortalRect.Full, 0f, 0x10000);
        Assert.IsTrue(views.HasExteriorView);
    }

    [TestMethod]
    public void DistanceUsesLocalAxisAllOriginalVerticesAndZeroFloor()
    {
        Assert.AreEqual(7f, Wrath335PortalSceneViews.MaximumDistance(
            [new(100f, 0f, 2f), new(0f, 0f, 10f)], new(0f, 0f, 3f), new(0f, 0f, 4f)));
        Assert.AreEqual(0f, Wrath335PortalSceneViews.MaximumDistance(
            [new(0f, 0f, -2f)], Vector3.Zero, Vector3.UnitZ));
        Assert.AreEqual(0.005f, Wrath335PortalSceneViews.MaximumDistance(
            [Vector3.UnitZ], Vector3.Zero, new(0f, 0f, 0.005f)), 0.000001f);
    }

    [TestMethod]
    public void InvalidEmissionDoesNotOpenClosedViews()
    {
        var views = new Wrath335PortalSceneViews();
        views.Reset(true);
        views.AddPortal(default, 1f, 8);
        views.AddPortal(WmoPortalRect.Full, float.NaN, 8);
        views.AddPortal(WmoPortalRect.Full, -1f, 8);
        Assert.IsFalse(views.HasSkyView);
    }

    [TestMethod]
    public void ScissorFlipsYAndUsesClientAsymmetricRounding()
    {
        Assert.IsTrue(Wrath335SkyScissor.TryCreate(new(-0.5f, 0f, 0.5f, 0.5f),
            100, 80, out var rect));
        Assert.AreEqual(new SkyScissor(25, 20, 76, 41), rect);
        Assert.IsTrue(Wrath335SkyScissor.TryCreate(new(-2f, -2f, 2f, 2f),
            100, 80, out rect));
        Assert.AreEqual(new SkyScissor(0, 0, 100, 80), rect);
    }

    [TestMethod]
    public void ScissorRejectsEmptyOffscreenNonfiniteAndZeroViewport()
    {
        Assert.IsFalse(Wrath335SkyScissor.TryCreate(default, 100, 80, out _));
        Assert.IsFalse(Wrath335SkyScissor.TryCreate(new(2f, 2f, 3f, 3f), 100, 80, out _));
        Assert.IsFalse(Wrath335SkyScissor.TryCreate(new(float.NaN, 0f, 1f, 1f), 100, 80, out _));
        Assert.IsFalse(Wrath335SkyScissor.TryCreate(WmoPortalRect.Full, 0, 80, out _));
    }
}
