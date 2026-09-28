using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Tests;

[TestClass]
public sealed class Wrath335CelestialTests
{
    [TestMethod]
    public void NoonSunUsesTheClientPolarCurveAndTexturePath()
    {
        var noon = Wrath335CelestialEvaluator.Evaluate(1440);
        Assert.AreEqual(@"Textures\sunCenter.blp", noon.Sun.ClientTexturePath);
        Assert.AreEqual(0.68766f, noon.Sun.CameraRelativeCenter.X, 0.002f);
        Assert.AreEqual(0.68766f, noon.Sun.CameraRelativeCenter.Y, 0.002f);
        Assert.AreEqual(11.96053f, noon.Sun.CameraRelativeCenter.Z, 0.002f);
        Assert.AreEqual(12f, noon.Sun.CameraRelativeCenter.Length(), 0.0001f);
        Assert.AreEqual(1f, noon.Sun.Size, 0.0001f);
        Assert.AreEqual(1f, noon.DayNightProminence, 0.0001f);
    }

    [TestMethod]
    public void MoonCurvesUseTheirOwnPathsAzimuthsAndOrbitalClock()
    {
        var midnight = Wrath335CelestialEvaluator.Evaluate(0);
        Assert.AreEqual(@"Textures\moon.blp", midnight.Moon1.ClientTexturePath);
        Assert.AreEqual(4.72822f, midnight.Moon1.CameraRelativeCenter.X, 0.002f);
        Assert.AreEqual(9.96433f, midnight.Moon1.CameraRelativeCenter.Z, 0.002f);
        Assert.AreEqual(1.75f, midnight.Moon1.Size, 0.0001f);

        var noon = Wrath335CelestialEvaluator.Evaluate(1440);
        Assert.AreEqual(@"Textures\moon02.blp", noon.Moon02.ClientTexturePath);
        Assert.IsTrue(noon.Moon02.CameraRelativeCenter.X < 0f);
        Assert.IsTrue(noon.Moon02.CameraRelativeCenter.Y > 0f);
        Assert.IsTrue(noon.Moon02.CameraRelativeCenter.Z < 0f);
        Assert.AreEqual(1.5f, noon.Moon02.Size, 0.0001f);
        Assert.AreEqual(noon.Moon02,
            Wrath335CelestialEvaluator.Evaluate(1440 + DayNight.GameDayLength).Moon02);
    }
}
