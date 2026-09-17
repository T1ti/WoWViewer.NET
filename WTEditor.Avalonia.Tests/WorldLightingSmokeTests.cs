using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Structs;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class WorldLightingSmokeTests
{
    [TestMethod]
    public void LightDataSnapshotPreservesValuesAndUnpacksWowColors()
    {
        var snapshot = new WorldLightingData(
            rowIndex: 7,
            id: 123,
            lightParamId: 12,
            time: 1440,
            numericValues: new Dictionary<string, double>
            {
                ["direct_color"] = unchecked((int)0xff336699),
                ["ambient_color"] = unchecked((int)0xff112233),
                ["ocean_close_color"] = unchecked((int)0xff102030),
                ["ocean_far_color"] = unchecked((int)0xff405060),
                ["river_close_color"] = unchecked((int)0xff708090),
                ["river_far_color"] = unchecked((int)0xffa0b0c0),
                ["water_shallow_alpha"] = 0.25,
                ["water_deep_alpha"] = 0.75,
                ["ocean_shallow_alpha"] = 0.4,
                ["ocean_deep_alpha"] = 0.9,
                ["fog_end"] = 512f
            },
            stringValues: new Dictionary<string, string>
            {
                ["debug_name"] = "temporary-profile"
            });

        Assert.AreEqual(12, snapshot.LightParamId);
        Assert.AreEqual(1440, snapshot.Time);
        Assert.AreEqual(0xff336699u, snapshot.DirectColorPacked);
        Assert.AreEqual(0xff112233u, snapshot.AmbientColorPacked);
        Assert.AreEqual(0x33 / 255f, snapshot.DirectColor.X, 0.0001f);
        Assert.AreEqual(0x66 / 255f, snapshot.DirectColor.Y, 0.0001f);
        Assert.AreEqual(0x99 / 255f, snapshot.DirectColor.Z, 0.0001f);
        Assert.AreEqual(0x10 / 255f, snapshot.OceanCloseColor.X, 0.0001f);
        Assert.AreEqual(0x50 / 255f, snapshot.OceanFarColor.Y, 0.0001f);
        Assert.AreEqual(0x90 / 255f, snapshot.RiverCloseColor.Z, 0.0001f);
        Assert.AreEqual(0xa0 / 255f, snapshot.RiverFarColor.X, 0.0001f);
        Assert.IsTrue(snapshot.HasLiquidColorData);
        Assert.IsTrue(snapshot.HasLiquidAlphaData);
        Assert.AreEqual(0.25f, snapshot.WaterShallowAlpha, 0.0001f);
        Assert.AreEqual(0.75f, snapshot.WaterDeepAlpha, 0.0001f);
        Assert.AreEqual(0.4f, snapshot.OceanShallowAlpha, 0.0001f);
        Assert.AreEqual(0.9f, snapshot.OceanDeepAlpha, 0.0001f);
        Assert.AreEqual(512d, snapshot.NumericValues["fog_end"], 0.0001d);
        Assert.AreEqual("temporary-profile", snapshot.StringValues["debug_name"]);
    }

    [TestMethod]
    public void LightDataSnapshotUsesExactWowlibColumnNames()
    {
        var snapshot = new WorldLightingData(
            rowIndex: 0,
            id: 1,
            lightParamId: 12,
            time: 1440,
            numericValues: new Dictionary<string, double>
            {
                ["direct_color"] = 0x00112233,
                ["ambient_color"] = 0x00445566
            },
            stringValues: new Dictionary<string, string>());

        Assert.IsTrue(snapshot.TryGetNumeric("direct_color", out _));
        Assert.IsFalse(snapshot.TryGetNumeric("DirectColor", out _));
        Assert.IsFalse(snapshot.HasLiquidColorData);
        Assert.AreEqual(0x11 / 255f, snapshot.DirectColor.X, 0.0001f);
        Assert.AreEqual(0x22 / 255f, snapshot.DirectColor.Y, 0.0001f);
        Assert.AreEqual(0x33 / 255f, snapshot.DirectColor.Z, 0.0001f);
    }
}
