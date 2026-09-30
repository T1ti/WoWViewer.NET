using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335PortalComplementTests
{
    [TestMethod]
    public void EmptyInputKeepsViewportAndFullWindowRemovesIt()
    {
        var complement = new Wrath335PortalComplement();
        complement.Build([]);
        AssertViews(complement, new WmoPortalRect(0f, 0f, 1f, 1f));
        complement.Build([Window(0f, 0f, 1f, 1f)]);
        AssertViews(complement);
    }

    [TestMethod]
    public void CenteredWindowEmitsYStripsBeforeTrimmedXStripsAndZerosDistance()
    {
        var complement = new Wrath335PortalComplement();
        complement.Build([new(new(0.25f, 0.25f, 0.75f, 0.75f), 345f)]);
        AssertViews(complement, new(0f, 0f, 1f, 0.25f), new(0f, 0.75f, 1f, 1f),
            new(0f, 0.25f, 0.25f, 0.75f), new(0.75f, 0.25f, 1f, 0.75f));
    }

    [TestMethod]
    public void OverlappingWindowOrderChangesPartitionWithoutChangingVisibleArea()
    {
        var complement = new Wrath335PortalComplement();
        var center = Window(0.25f, 0.25f, 0.75f, 0.75f);
        var corner = Window(0.5f, 0.5f, 1f, 1f);
        complement.Build([center, corner]);
        AssertViews(complement, new(0f, 0f, 1f, 0.25f), new(0f, 0.75f, 0.5f, 1f),
            new(0f, 0.25f, 0.25f, 0.75f), new(0.75f, 0.25f, 1f, 0.5f));
        Assert.AreEqual(0.5625f, Area(complement));
        complement.Build([corner, center]);
        AssertViews(complement, new(0f, 0f, 1f, 0.25f), new(0f, 0.25f, 0.25f, 0.5f),
            new(0.75f, 0.25f, 1f, 0.5f), new(0f, 0.75f, 0.5f, 1f),
            new(0f, 0.5f, 0.25f, 0.75f));
        Assert.AreEqual(0.5625f, Area(complement));
    }

    [TestMethod]
    public void DisjointWindowsRemainSeparateInsteadOfSubtractingTheirUnionBounds()
    {
        var complement = new Wrath335PortalComplement();
        complement.Build([Window(0.25f, 0f, 0.5f, 1f), Window(0.75f, 0f, 1f, 1f)]);
        AssertViews(complement, new(0f, 0f, 0.25f, 1f), new(0.5f, 0f, 0.75f, 1f));
    }

    [DataTestMethod]
    [DataRow(1f, 0f, 2f, 1f)]
    [DataRow(-1f, 0f, 0f, 1f)]
    [DataRow(0f, 1f, 1f, 2f)]
    [DataRow(0f, -1f, 1f, 0f)]
    [DataRow(2f, 0f, 3f, 1f)]
    [DataRow(0f, -3f, 1f, -2f)]
    public void TouchingAndOffscreenWindowsDoNotSubtract(float minX, float minY, float maxX, float maxY)
    {
        var complement = new Wrath335PortalComplement();
        complement.Build([Window(minX, minY, maxX, maxY)]);
        AssertViews(complement, new WmoPortalRect(0f, 0f, 1f, 1f));
    }

    [TestMethod]
    public void WindowCrossingViewportBoundariesKeepsOnlyTheTwoOutsideBands()
    {
        var complement = new Wrath335PortalComplement();
        complement.Build([Window(-0.25f, 0.25f, 1.25f, 0.75f)]);
        AssertViews(complement, new(0f, 0f, 1f, 0.25f), new(0f, 0.75f, 1f, 1f));
    }

    [TestMethod]
    public void RepeatedWindowPreservesTheOrderedComplement()
    {
        var complement = new Wrath335PortalComplement();
        var window = Window(0.25f, 0.25f, 0.75f, 0.75f);
        complement.Build([window, window]);
        AssertViews(complement, new(0f, 0f, 1f, 0.25f), new(0f, 0.75f, 1f, 1f),
            new(0f, 0.25f, 0.25f, 0.75f), new(0.75f, 0.25f, 1f, 0.75f));
    }

    [TestMethod]
    public void SubEpsilonBandsAndZeroAreaWindowsUseExactComparisons()
    {
        var complement = new Wrath335PortalComplement();
        complement.Build([Window(1f / 8192f, 0f, 0.75f, 1f)]);
        AssertViews(complement, new(0f, 0f, 1f / 8192f, 1f), new(0.75f, 0f, 1f, 1f));
        complement.Build([Window(0.5f, 0.25f, 0.5f, 0.75f)]);
        AssertViews(complement, new(0f, 0f, 1f, 0.25f), new(0f, 0.75f, 1f, 1f),
            new(0f, 0.25f, 0.5f, 0.75f), new(0.5f, 0.25f, 1f, 0.75f));
        Assert.AreEqual(1f, Area(complement));
    }

    [TestMethod]
    public void SixtyFragmentsCanProcessAnotherWindowButSixtyOneStopBeforeIt()
    {
        var complement = new Wrath335PortalComplement();
        complement.Build([.. VerticalCuts(59), Window(0f, 0f, 1f, 1f)]);
        AssertViews(complement);
        Assert.IsFalse(complement.ReachedFragmentLimit);
        complement.Build([.. VerticalCuts(60), Window(0f, 0f, 1f, 1f)]);
        Assert.AreEqual(61, complement.Views.Length);
        Assert.IsTrue(complement.ReachedFragmentLimit);
        Assert.AreEqual(new WmoPortalRect(0f, 0f, 1f / 128f, 1f), complement.Views[0].Rect);
        Assert.AreEqual(new WmoPortalRect(120f / 128f, 0f, 1f, 1f), complement.Views[60].Rect);
    }

    [TestMethod]
    public void FragmentLimitFinishesTheCurrentRectangleAndDropsUnprocessedPieces()
    {
        var complement = new Wrath335PortalComplement();
        complement.Build([.. VerticalCuts(59), Window(1f / 512f, 0.25f, 1f, 0.75f)]);
        Assert.AreEqual(61, complement.Views.Length);
        Assert.IsTrue(complement.ReachedFragmentLimit);
        Assert.AreEqual(new WmoPortalRect(0f, 0f, 1f / 128f, 0.25f), complement.Views[0].Rect);
        Assert.AreEqual(new WmoPortalRect(0f, 0.75f, 1f / 128f, 1f), complement.Views[1].Rect);
        Assert.AreEqual(new WmoPortalRect(0f, 0.25f, 1f / 512f, 0.75f), complement.Views[2].Rect);
        Assert.AreEqual(new WmoPortalRect(58f / 128f, 0.75f, 59f / 128f, 1f), complement.Views[60].Rect);
        // The native limit discards the remaining right-hand source pieces;
        // it does not return a conservative complete viewport complement.
        Assert.IsFalse(Contains(complement, new(0.95f, 0.1f)));
    }

    [TestMethod]
    public void ReusedStorageClearsOldLimitAndOldOutputBetweenFrames()
    {
        var complement = new Wrath335PortalComplement();
        complement.Build(VerticalCuts(60));
        Assert.IsTrue(complement.ReachedFragmentLimit);
        complement.Build([Window(0f, 0f, 1f, 1f)]);
        AssertViews(complement);
        Assert.IsFalse(complement.ReachedFragmentLimit);
        complement.Build([]);
        AssertViews(complement, new WmoPortalRect(0f, 0f, 1f, 1f));
        complement.Clear();
        AssertViews(complement);
    }

    [TestMethod]
    public void SceneComplementUsesInteriorWindowsInsteadOfExteriorPolygonsOrSkyUnion()
    {
        var scene = new Wrath335PortalSceneViews();
        scene.Reset(true, 0x40); // full sky seed must not become a full window
        scene.AddProjectedPortal(new(-0.5f, -0.5f, 0.5f, 0.5f), 17f, 0x40000);
        scene.RenderViews.Add(new(0f, 0f, 1f, 1f), [Vector3.Zero, Vector3.UnitX, Vector3.UnitY]);
        scene.BuildComplement();
        Assert.AreEqual(new Wrath335PortalWindow(new(0.25f, 0.25f, 0.75f, 0.75f), 17f), scene.Windows[0]);
        Assert.AreEqual(WmoPortalRect.Full, scene.SkyRect);
        AssertViews(scene.Complement, new(0f, 0f, 1f, 0.25f), new(0f, 0.75f, 1f, 1f),
            new(0f, 0.25f, 0.25f, 0.75f), new(0.75f, 0.25f, 1f, 0.75f));
        Assert.AreEqual(1, scene.RenderViews.Views.Length);
        Assert.IsFalse(scene.HasExteriorView);
    }

    [TestMethod]
    public void SceneSkipsComplementWhenSkyOrWindowListIsAbsentAndResetDropsBothLists()
    {
        var scene = new Wrath335PortalSceneViews();
        scene.Reset(true, 0x40);
        scene.BuildComplement();
        AssertViews(scene.Complement); // native caller does not build for zero windows
        scene.AddProjectedPortal(new(-0.5f, -0.5f, 0.5f, 0.5f), 10f, 8);
        scene.RenderViews.Add(new(0f, 0f, 1f, 1f), [Vector3.Zero, Vector3.UnitX, Vector3.UnitY]);
        scene.BuildComplement();
        Assert.AreEqual(4, scene.Complement.Views.Length);
        scene.Reset(true, secondaryPlacement: true);
        scene.BuildComplement();
        AssertViews(scene.Complement);
        Assert.AreEqual(0, scene.Windows.Length);
        Assert.AreEqual(0, scene.RenderViews.Views.Length);
        Assert.IsFalse(scene.HasSkyView);
        scene.Reset(false);
        scene.BuildComplement();
        AssertViews(scene.Complement);
    }

    private static Wrath335PortalWindow Window(float minX, float minY, float maxX, float maxY) =>
        new(new(minX, minY, maxX, maxY), 0f);

    private static Wrath335PortalWindow[] VerticalCuts(int count) => Enumerable.Range(0, count)
        .Select(i => Window((2 * i + 1) / 128f, 0f, (2 * i + 2) / 128f, 1f)).ToArray();

    private static void AssertViews(Wrath335PortalComplement complement, params WmoPortalRect[] expected) =>
        CollectionAssert.AreEqual(expected.Select(rect => new Wrath335PortalWindow(rect, 0f)).ToArray(),
            complement.Views.ToArray());

    private static float Area(Wrath335PortalComplement complement)
    {
        var area = 0f;
        foreach (var view in complement.Views)
            area += (view.Rect.MaxX - view.Rect.MinX) * (view.Rect.MaxY - view.Rect.MinY);
        return area;
    }

    private static bool Contains(Wrath335PortalComplement complement, Vector2 point)
    {
        foreach (var view in complement.Views)
            if (point.X > view.Rect.MinX && point.X < view.Rect.MaxX &&
                point.Y > view.Rect.MinY && point.Y < view.Rect.MaxY)
                return true;
        return false;
    }
}
