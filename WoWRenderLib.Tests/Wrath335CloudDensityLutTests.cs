using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Tests;

[TestClass]
public sealed class Wrath335CloudDensityLutTests
{
    [TestMethod]
    public void ClientStartupCoverageProducesTheFixedDensityCurve()
    {
        var values = Wrath335CloudDensityLut.Create();

        Assert.AreEqual((byte)101, Wrath335CloudDensityLut.DefaultCoverageByte);
        Assert.AreEqual(256, values.Length);
        Assert.AreEqual((byte)0, values[0]);
        Assert.AreEqual((byte)202, values[64]);
        Assert.AreEqual((byte)243, values[128]);
        Assert.AreEqual((byte)254, values[255]);
        for (var index = 1; index < values.Length; index++)
            Assert.IsTrue(values[index] >= values[index - 1]);
    }
}
