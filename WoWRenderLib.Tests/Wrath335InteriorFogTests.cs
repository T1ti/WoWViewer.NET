using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Tests;

[TestClass]
public sealed class Wrath335InteriorFogTests
{
    [TestMethod]
    public void InteriorFogUsesPortalDistanceForTheCurrentFogSet()
    {
        var outdoor = Wrath335InteriorFog.FromOutdoor(
            new Wrath335OutdoorFog(100f, 350f, 1f), Vector3.Zero);
        var target = Wrath335InteriorFog.EvaluateTarget(
            new WmoFogBand(100f, 0.5f, 0xFFFFFFFFu), 350f, false)
            with { Rate = 2f };

        Assert.AreEqual(outdoor,
            Wrath335InteriorFog.BlendPortal(outdoor, target, 0f));
        var halfway = Wrath335InteriorFog.BlendPortal(outdoor, target, 12.5f);
        Assert.AreEqual(75f, halfway.StartDistance, 0.0001f);
        Assert.AreEqual(225f, halfway.EndDistance, 0.0001f);
        Assert.AreEqual(1.5f, halfway.Rate, 0.0001f);
        var staged = Wrath335InteriorFog.StageOutdoorColor(outdoor, halfway);
        Assert.AreEqual(halfway.StartDistance, staged.StartDistance, 0.0001f);
        Assert.AreEqual(halfway.EndDistance, staged.EndDistance, 0.0001f);
        Assert.AreEqual(halfway.Rate, staged.Rate, 0.0001f);
        Assert.AreEqual(outdoor.Color, staged.Color);
        Assert.AreEqual(target,
            Wrath335InteriorFog.BlendPortal(outdoor, target, 25f));
    }

    [TestMethod]
    public void MfogEndFloorDoesNotRecalculateStartBeforeExpansion()
    {
        var band = new WmoFogBand(10f, 0.5f, 0xFF204080u);
        var ordinary = Wrath335InteriorFog.EvaluateTarget(band, 350f, false);
        Assert.AreEqual(5f, ordinary.StartDistance, 0.0001f);
        Assert.AreEqual(30f, ordinary.EndDistance, 0.0001f);

        var expansion = Wrath335InteriorFog.EvaluateTarget(band, 350f, true);
        Assert.AreEqual(5f, expansion.StartDistance, 0.0001f);
        Assert.AreEqual(350f, expansion.EndDistance, 0.0001f);
        Assert.AreEqual(6.0833335f, expansion.Rate, 0.0001f);
    }
}
