using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Tests;

[TestClass]
public sealed class Wrath335OutdoorFogTests
{
    [TestMethod]
    public void OutdoorFogAppliesTheClientBandFloorAndFarClip()
    {
        var floored = Wrath335OutdoorFogEvaluator.Evaluate(
            5f, 0.25f, 350f, expansionMode: false);
        Assert.AreEqual(2.5f, floored.StartDistance, 0.0001f);
        Assert.AreEqual(10f, floored.EndDistance, 0.0001f);
        Assert.AreEqual(1f, floored.Rate, 0.0001f);

        var clipped = Wrath335OutdoorFogEvaluator.Evaluate(
            400f, 0.5f, 350f, expansionMode: false);
        Assert.AreEqual(175f, clipped.StartDistance, 0.0001f);
        Assert.AreEqual(350f, clipped.EndDistance, 0.0001f);
    }

    [TestMethod]
    public void ExpansionFogUsesBandWidthForRateButFarClipForDistances()
    {
        var fog = Wrath335OutdoorFogEvaluator.Evaluate(
            100f, 0.5f, 350f, expansionMode: true);
        Assert.AreEqual(175f, fog.StartDistance, 0.0001f);
        Assert.AreEqual(350f, fog.EndDistance, 0.0001f);
        Assert.AreEqual(5.166667f, fog.Rate, 0.0001f);

        // CalcFogRate caps its far-clip contribution at 700, even if the
        // validated camera far clip is higher.
        Assert.AreEqual(6.45f,
            Wrath335OutdoorFogEvaluator.CalculateExpansionRate(50f, 100f, 791.66669f),
            0.0001f);
    }

    [TestMethod]
    public void ExpansionFogClampsNegativeStartAfterComputingTheRate()
    {
        var fog = Wrath335OutdoorFogEvaluator.Evaluate(
            100f, -2f, 350f, expansionMode: true);
        Assert.AreEqual(0f, fog.StartDistance, 0.0001f);
        Assert.AreEqual(350f, fog.EndDistance, 0.0001f);
        Assert.AreEqual(1.5f, fog.Rate, 0.0001f);

        var shortRange = Wrath335OutdoorFogEvaluator.Evaluate(
            20f, 0.5f, 350f, expansionMode: true);
        Assert.AreEqual(10f, shortRange.StartDistance, 0.0001f);
        Assert.AreEqual(20f, shortRange.EndDistance, 0.0001f);
        Assert.AreEqual(1f, shortRange.Rate, 0.0001f);
    }

    [TestMethod]
    public void CompiledShaderVisibilityUsesViewDepthAndClientRate()
    {
        var fog = new Wrath335OutdoorFog(100f, 300f, 2f);
        Assert.AreEqual(1f, Wrath335OutdoorFogEvaluator.Visibility(50f, fog), 0.0001f);
        Assert.AreEqual(1f, Wrath335OutdoorFogEvaluator.Visibility(100f, fog), 0.0001f);
        Assert.AreEqual(0.25f, Wrath335OutdoorFogEvaluator.Visibility(200f, fog), 0.0001f);
        Assert.AreEqual(0f, Wrath335OutdoorFogEvaluator.Visibility(300f, fog), 0.0001f);
    }
}
