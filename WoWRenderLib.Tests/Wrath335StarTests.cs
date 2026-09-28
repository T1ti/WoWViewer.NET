using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Tests;

[TestClass]
public sealed class Wrath335StarTests
{
    [TestMethod]
    public void StarsUseClientNightFadeAndRenderingThreshold()
    {
        Assert.AreEqual(@"Environments\Stars\Stars.m2", Wrath335StarEvaluator.ModelPath);
        Assert.AreEqual(byte.MaxValue, Wrath335StarEvaluator.EvaluateAlphaByte(0));
        Assert.AreEqual(byte.MaxValue, Wrath335StarEvaluator.EvaluateAlphaByte(360));
        Assert.IsTrue(Wrath335StarEvaluator.EvaluateOpacity(450) > 0f);
        Assert.AreEqual((byte)1, Wrath335StarEvaluator.EvaluateAlphaByte(540));
        Assert.AreEqual(0f, Wrath335StarEvaluator.EvaluateOpacity(1440));
        Assert.AreEqual(0f, Wrath335StarEvaluator.EvaluateOpacity(2700));
        Assert.IsTrue(Wrath335StarEvaluator.EvaluateOpacity(2790) > 0f);
        Assert.AreEqual(1f, Wrath335StarEvaluator.EvaluateOpacity(2880));
    }

    [TestMethod]
    public void StarsRepeatAcrossDaysIncludingNegativeTime()
    {
        Assert.AreEqual(Wrath335StarEvaluator.EvaluateAlphaByte(90),
            Wrath335StarEvaluator.EvaluateAlphaByte(90 + DayNight.GameDayLength));
        Assert.AreEqual(Wrath335StarEvaluator.EvaluateAlphaByte(-90),
            Wrath335StarEvaluator.EvaluateAlphaByte(DayNight.GameDayLength - 90));
    }
}
