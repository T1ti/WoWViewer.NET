using Avalonia;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Avalonia.Views;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class ObjectWindowDockingSmokeTests
{
    [TestMethod]
    public void FloatingObjectWindow_DocksOnlyWhenCursorReachesEdgeFromInsideWorkspace()
    {
        var workspace = new PixelRect(100, 100, 1200, 800);

        Assert.AreEqual(ObjectDockEdge.Left, ObjectWindowDocking.FindTarget(
            new PixelPoint(110, 300), workspace, 48));
        Assert.AreEqual(ObjectDockEdge.Right, ObjectWindowDocking.FindTarget(
            new PixelPoint(1290, 300), workspace, 48));
        Assert.AreEqual(ObjectDockEdge.Bottom, ObjectWindowDocking.FindTarget(
            new PixelPoint(650, 890), workspace, 48));
        Assert.IsNull(ObjectWindowDocking.FindTarget(
            new PixelPoint(250, 300), workspace, 48));
        Assert.IsNull(ObjectWindowDocking.FindTarget(
            new PixelPoint(90, 300), workspace, 48));
        Assert.IsNull(ObjectWindowDocking.FindTarget(
            new PixelPoint(1310, 300), workspace, 48));
        Assert.IsNull(ObjectWindowDocking.FindTarget(
            new PixelPoint(650, 910), workspace, 48));
        Assert.IsNull(ObjectWindowDocking.FindTarget(
            new PixelPoint(1700, 300), workspace, 48));
    }
}
