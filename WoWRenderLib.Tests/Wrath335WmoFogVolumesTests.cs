using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Tests;

[TestClass]
public sealed class Wrath335WmoFogVolumesTests
{
    [TestMethod]
    public void SingleBaseRecordDoesNotActivateInteriorFog()
    {
        var fogs = new[] { Volume(400f, Vector3.Zero, 0f, 50f, 0u) };
        Assert.IsFalse(Wrath335WmoFogVolumes.TryEvaluate(
            fogs, new byte[] { 0, 0, 0, 0 }, Vector3.Zero, out _));
    }

    [TestMethod]
    public void NearestEligibleVolumeBlendsLastAndSuppliesFlags()
    {
        var fogs = new[]
        {
            Volume(400f, Vector3.Zero, 0f, 20f, 0u),
            Volume(200f, new Vector3(15f, 0f, 0f), 0f, 20f, 0x10u),
            Volume(100f, new Vector3(5f, 0f, 0f), 0f, 20f, 0x20u),
            Volume(1f, Vector3.Zero, 0f, 20f, 1u)
        };

        Assert.IsTrue(Wrath335WmoFogVolumes.TryEvaluate(
            fogs, new byte[] { 1, 2, 3, 0 }, Vector3.Zero, out var result));
        Assert.AreEqual(162.5f, result.Fog.End, 0.0001f);
        Assert.AreEqual(0x20u, result.Flags);
        Assert.AreEqual(162.5f, result.UnderwaterFog.End, 0.0001f);
    }

    private static WmoFogVolume Volume(float end, Vector3 position,
        float smallerRadius, float largerRadius, uint flags) => new(
        flags, position, smallerRadius, largerRadius,
        new WmoFogBand(end, 0.5f, 0xFF000000u),
        new WmoFogBand(end, 0.25f, 0xFF000000u));
}
