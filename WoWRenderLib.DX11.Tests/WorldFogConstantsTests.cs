using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class WorldFogConstantsTests
{
    [TestMethod]
    public void ViewportFogOverrideDisablesOutdoorAndInteriorBanksWithoutChangingClientValues()
    {
        Wrath335FogState[] fogBanks =
        [
            new(100f, 1000f, 1.5f, 0xFF203040u),
            new(10f, 100f, 1f, 0xFF304050u)
        ];
        foreach (var fog in fogBanks)
        {
            var enabled = WorldFogConstants.Create(fog, true);
            var disabled = WorldFogConstants.Create(fog, false);
            Assert.AreEqual(1f, enabled.Parameters.W);
            Assert.AreEqual(0f, disabled.Parameters.W);
            Assert.AreEqual(enabled.Parameters.X, disabled.Parameters.X);
            Assert.AreEqual(enabled.Parameters.Y, disabled.Parameters.Y);
            Assert.AreEqual(enabled.Parameters.Z, disabled.Parameters.Z);
            Assert.AreEqual(enabled.Color, disabled.Color);
            Assert.AreEqual(new Vector4(Wrath335InteriorFog.UnpackColor(fog.Color), 1f), enabled.Color);
        }
        Assert.AreEqual(32, Marshal.SizeOf<WrathFogCB>());
    }

    [TestMethod]
    public void FogConstantsRetainMinimumShaderWidthForDegenerateBands()
    {
        var fog = WorldFogConstants.Create(new(100f, 100f, 1f, 0u), true);
        Assert.AreEqual(-1f / Wrath335OutdoorFogEvaluator.MinimumShaderFogWidth, fog.Parameters.X);
        Assert.AreEqual(100f / Wrath335OutdoorFogEvaluator.MinimumShaderFogWidth, fog.Parameters.Y);
        Assert.AreEqual(1f, fog.Parameters.W);
    }
}
