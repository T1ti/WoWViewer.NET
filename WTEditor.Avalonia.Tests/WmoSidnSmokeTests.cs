using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Structs;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class WmoSidnSmokeTests
{
    [TestMethod]
    public void SidnPulseFollowsDawnDuskAndWrapsTheWorldClock()
    {
        Assert.AreEqual(1f, WmoMaterialPolicy.SidnPulse(0));
        Assert.AreEqual(1f, WmoMaterialPolicy.SidnPulse(720));
        Assert.AreEqual(0.5f, WmoMaterialPolicy.SidnPulse(780));
        Assert.AreEqual(0f, WmoMaterialPolicy.SidnPulse(840));
        Assert.AreEqual(0f, WmoMaterialPolicy.SidnPulse(2460));
        Assert.AreEqual(0.5f, WmoMaterialPolicy.SidnPulse(2520));
        Assert.AreEqual(1f, WmoMaterialPolicy.SidnPulse(2580));
        Assert.AreEqual(1f, WmoMaterialPolicy.SidnPulse(2880));
        Assert.AreEqual(1f, WmoMaterialPolicy.SidnPulse(-1));
    }

    [TestMethod]
    public void SidnColorUsesFlagAndMaterialRgbAtCurrentPulse()
    {
        var sidnMaterial = new PreppedWMOMaterial { Flags = 0x10, Color1 = 0x00804020 };
        var ordinaryMaterial = new PreppedWMOMaterial { Color1 = sidnMaterial.Color1 };

        Assert.AreEqual(Vector3.Zero, WmoMaterialPolicy.SidnColor(sidnMaterial, 0f));
        Assert.AreEqual(Vector3.Zero, WmoMaterialPolicy.SidnColor(ordinaryMaterial, 1f));
        Assert.AreEqual(new Vector3(128f / 510f, 64f / 510f, 32f / 510f),
            WmoMaterialPolicy.SidnColor(sidnMaterial, 0.5f));
    }

    [TestMethod]
    public void SidnColorsRefreshWhenTimeChangesAndInitializeNewWmosAtCurrentTime()
    {
        var cache = new WmoSidnColorCache();
        var firstWmo = new[] { new PreppedWMOMaterial { Flags = 0x10, Color1 = 0x00ff0000 } };
        var secondWmo = new[] { new PreppedWMOMaterial { Flags = 0x10, Color1 = 0x0000ff00 } };

        Assert.AreEqual(Vector3.UnitX, cache.GetColors(firstWmo, 0)[0]);
        firstWmo[0] = new PreppedWMOMaterial { Flags = 0x10, Color1 = 0x000000ff };
        // The existing WMO keeps its frame color until the world time advances.
        Assert.AreEqual(Vector3.UnitX, cache.GetColors(firstWmo, 0)[0]);
        Assert.AreEqual(Vector3.UnitY, cache.GetColors(secondWmo, 0)[0]);
        Assert.AreEqual(Vector3.Zero, cache.GetColors(firstWmo, 840)[0]);
        Assert.AreEqual(Vector3.Zero, cache.GetColors(secondWmo, 840)[0]);
        Assert.AreEqual(Vector3.UnitZ * 0.5f, cache.GetColors(firstWmo, 780)[0]);
        Assert.AreEqual(Vector3.UnitZ, cache.GetColors(firstWmo, 0)[0]);
        Assert.AreEqual(Vector3.Zero, cache.GetColors(firstWmo, 1440)[0]);
        Assert.AreEqual(Vector3.UnitZ, cache.GetColors(firstWmo, 2880)[0]);
    }

    [TestMethod]
    public void WispSidnIsHalvedInByteSpaceAndCachedByWorldTime()
    {
        var material = new PreppedWMOMaterial { Flags = 0x10, Color1 = 0x00ff8040 };
        Assert.AreEqual(new Vector3(127f / 255f, 64f / 255f, 32f / 255f),
            WmoMaterialPolicy.WispSidnColor(material, 1f));

        var cache = new WmoSidnColorCache();
        var materials = new[] { material };
        Assert.AreEqual(Vector3.Zero, cache.GetColors(materials, 840, wispLegacy: true)[0]);
        Assert.AreEqual(new Vector3(64f / 255f, 32f / 255f, 16f / 255f),
            cache.GetColors(materials, 780, wispLegacy: true)[0]);
    }
}
