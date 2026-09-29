using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335M2FogModeTests
{
    [TestMethod]
    public void LegacyBlendFamiliesChooseTheClientFogColor()
    {
        var expected = new[]
        {
            Wrath335M2FogMode.SceneColor, Wrath335M2FogMode.SceneColor,
            Wrath335M2FogMode.SceneColor, Wrath335M2FogMode.Black,
            Wrath335M2FogMode.Black, Wrath335M2FogMode.White,
            Wrath335M2FogMode.Gray
        };
        for (var blendMode = 0; blendMode < expected.Length; blendMode++)
            Assert.AreEqual(expected[blendMode],
                Wrath335M2FogPolicy.ForMaterial(true, blendMode, false));
    }

    [TestMethod]
    public void MaterialFlagAndModernClientKeepTheirOwnFogRules()
    {
        Assert.AreEqual(Wrath335M2FogMode.Disabled,
            Wrath335M2FogPolicy.ForMaterial(true, 4, true));
        Assert.AreEqual(Wrath335M2FogMode.SceneColor,
            Wrath335M2FogPolicy.ForMaterial(false, 4, false));
    }
}
