using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Tests;

[TestClass]
public sealed class Wrath335WmoPlacementBoundsTests
{
    [TestMethod]
    public void AxisReflectionPreservesOrderedExtentsAndVerticalHeight()
    {
        var bounds = Wrath335WmoPlacementBounds.FromClientExtents(
            new(10f, -30f, 50f), new(20f, 40f, 60f), Vector3.Zero);
        Assert.AreEqual(new Vector3(-60f, -20f, -30f), bounds.Min);
        Assert.AreEqual(new Vector3(-50f, -10f, 40f), bounds.Max);
        Assert.AreEqual(new Vector3(10f, 10f, 70f), bounds.Size);
    }

    [TestMethod]
    public void PlacementOffsetIsAppliedAfterReflection()
    {
        var bounds = Wrath335WmoPlacementBounds.FromClientExtents(
            new(10f, -30f, 50f), new(20f, 40f, 60f), new(100f, 200f, 300f));
        Assert.AreEqual(new Vector3(40f, 180f, 270f), bounds.Min);
        Assert.AreEqual(new Vector3(50f, 190f, 340f), bounds.Max);
    }
}
