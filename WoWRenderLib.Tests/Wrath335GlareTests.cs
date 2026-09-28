using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Tests;

[TestClass]
public sealed class Wrath335GlareTests
{
    [TestMethod]
    public void SunGlareUsesClientDayEnvelopeCloudDensityAndVisibilitySmoothing()
    {
        var glare = new Wrath335GlareEvaluator(moon: false);
        Assert.AreEqual(0f, glare.Evaluate(0, 1f, 1f,
            0f, 0f, 1f, 1f).Opacity);

        var midday = glare.Evaluate(1440, 1f, 1f,
            0f, 0f, 1f, 1f);
        Assert.AreEqual(20f, midday.Size);
        Assert.AreEqual(1f, midday.Opacity);

        var occluded = glare.Evaluate(1440, 0.1f, 0f,
            0f, 0f, 1f, 1f);
        Assert.IsTrue(occluded.Opacity < midday.Opacity);
        Assert.IsTrue(occluded.Opacity > 0f);

        var cloudCovered = new Wrath335GlareEvaluator(moon: false)
            .Evaluate(1440, 1f, 1f, 1f, 0f, 1f, 1f);
        Assert.AreEqual(0f, cloudCovered.Opacity);
    }

    [TestMethod]
    public void MoonGlareHasNightEnvelopeAndMidDensityResponse()
    {
        var glare = new Wrath335GlareEvaluator(moon: true);
        Assert.AreEqual(0f, glare.Evaluate(1440, 1f, 1f,
            0.5f, 0f, 1f, 1f).Opacity);
        var night = glare.Evaluate(240, 1f, 1f,
            0.5f, 0f, 1f, 1f);
        Assert.AreEqual(2f, night.Size);
        Assert.AreEqual(1f, night.Opacity);
        var opaqueSkybox = glare.Evaluate(240, 1f, 1f,
            0.5f, 1f, 1f, 1f);
        Assert.AreEqual(0f, opaqueSkybox.Opacity);
    }
}
