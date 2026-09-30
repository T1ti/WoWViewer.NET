using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335ViewerPlacementSelectionTests
{
    [TestMethod]
    public void EqualHitsRetainTheLastPlacementAndBothPortalGroups()
    {
        var selection = new Wrath335ViewerPlacementSelection(1760f);
        selection.AcceptQueryHit(0, 3, 10f, 2, -1);
        selection.AcceptQueryHit(0, 9, 10f, 4, 5);
        selection.GetResults(out var primary, out var secondary);
        Assert.AreEqual(9, primary.PlacementIndex);
        Assert.AreEqual(4, primary.PrimaryGroupIndex);
        Assert.AreEqual(5, primary.SecondaryGroupIndex);
        Assert.IsFalse(secondary.HasPlacement);
    }

    [TestMethod]
    public void ExteriorHitClearsTheWinnerButKeepsItsNarrowedPoolCap()
    {
        var selection = new Wrath335ViewerPlacementSelection(1760f);
        selection.AcceptQueryHit(0, 0, 10f, 2, -1);
        selection.AcceptQueryHit(0, 1, 5f, -1, -1);
        selection.GetResults(out var primary, out _);
        Assert.IsFalse(primary.HasPlacement);
        Assert.AreEqual(5f, selection.GetMaximumDistance(0));
        selection.AcceptQueryHit(0, 2, 5f, 3, -1);
        selection.GetResults(out primary, out _);
        Assert.AreEqual(2, primary.PlacementIndex);
    }

    [TestMethod]
    public void NormalPoolHasPriorityOverCloserUpdatedPlacement()
    {
        var selection = new Wrath335ViewerPlacementSelection(40f);
        selection.AcceptQueryHit(0x400, 8, 2f, 1, 3);
        Assert.AreEqual(40f, selection.GetMaximumDistance(0));
        selection.AcceptQueryHit(0, 4, 20f, 0, -1);
        selection.GetResults(out var primary, out var secondary);
        Assert.AreEqual(4, primary.PlacementIndex);
        Assert.AreEqual(8, secondary.PlacementIndex);
        Assert.AreEqual(3, secondary.SecondaryGroupIndex);
        Assert.AreEqual(2f, selection.GetMaximumDistance(0x400));
    }

    [TestMethod]
    public void UpdatedPoolIsPromotedOnlyWhenNormalPoolIsEmpty()
    {
        var selection = new Wrath335ViewerPlacementSelection(40f);
        selection.AcceptQueryHit(0, 1, 5f, 0, -1);
        selection.AcceptQueryHit(0x400, 2, 10f, 2, 3);
        selection.AcceptQueryHit(0, 3, 4f, -1, -1);
        selection.GetResults(out var primary, out var secondary);
        Assert.AreEqual(2, primary.PlacementIndex);
        Assert.AreEqual(2, primary.PrimaryGroupIndex);
        Assert.AreEqual(3, primary.SecondaryGroupIndex);
        Assert.IsFalse(secondary.HasPlacement);
    }

    [TestMethod]
    public void AcceptedPortalCanRaiseTheRunningCap()
    {
        var selection = new Wrath335ViewerPlacementSelection(1760f);
        selection.AcceptQueryHit(0, 1, 1760.1f, 0, 1);
        Assert.AreEqual(1760.1f, selection.GetMaximumDistance(0));
        selection.AcceptQueryHit(0, 2, 1760.05f, 2, -1);
        selection.GetResults(out var primary, out _);
        Assert.AreEqual(2, primary.PlacementIndex);
    }

    [TestMethod]
    public void SkipMaskAndInvalidHitsLeavePreviousSelectionUntouched()
    {
        var selection = new Wrath335ViewerPlacementSelection(40f);
        selection.AcceptQueryHit(0, 1, 10f, 0, -1);
        Assert.IsFalse(Wrath335ViewerPlacementSelection.CanQuery(0x420));
        Assert.IsTrue(Wrath335ViewerPlacementSelection.CanQuery(0x400));
        selection.AcceptQueryHit(0x20, 2, 1f, 1, -1);
        selection.AcceptQueryHit(0, 3, float.NaN, 1, -1);
        selection.AcceptQueryHit(0, 3, float.PositiveInfinity, 1, -1);
        selection.AcceptQueryHit(0, 3, -1f, 1, -1);
        selection.GetResults(out var primary, out _);
        Assert.AreEqual(1, primary.PlacementIndex);
        Assert.AreEqual(10f, selection.GetMaximumDistance(0));
    }

    [TestMethod]
    public void FileInitializationAndLaterTransformsHaveDistinctRuntimeState()
    {
        var state = new WmoViewerPlacementState();
        state.TransformChanged(); // Object initializer writes are not live moves.
        var fileBounds = new BoundingBox(new(1, 2, 3), new(4, 5, 6));
        state.InitializeFilePlacement(fileBounds);
        Assert.AreEqual(0u, state.RuntimeFlags);
        Assert.AreEqual(fileBounds.Min, state.FileBounds!.Value.Min);
        state.TransformChanged();
        Assert.AreEqual(0x400u, state.RuntimeFlags);
        Assert.IsNull(state.FileBounds);
        state.TransformChanged();
        Assert.AreEqual(0x400u, state.RuntimeFlags);
    }
}
