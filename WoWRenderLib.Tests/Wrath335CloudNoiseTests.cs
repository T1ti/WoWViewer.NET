using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Tests;

[TestClass]
public sealed class Wrath335CloudNoiseTests
{
    [TestMethod]
    public void ConstantClientGradientHasFourOctaveAmplitudeAndWraps()
    {
        var noise = new Wrath335CloudNoise(
            Enumerable.Repeat(1f, Wrath335CloudNoise.GradientCount).ToArray());

        var center = noise.Sample(0, 0, 0, 0);
        var translated = noise.Sample(4096, 0, 0, 0);
        var otherLod = noise.Sample(37, 42, 239, 3);

        Assert.AreEqual(1.875f, center.Height, 0.00001f);
        Assert.AreEqual(1.75f, center.NormalHeight, 0.00001f);
        Assert.AreEqual(center, translated);
        Assert.AreEqual(center, otherLod);
        Assert.AreEqual((byte)248,
            Wrath335CloudNoise.ToHeightByte(center.Height));
    }

    [TestMethod]
    public void FixedClientPermutationSelectsExpectedOriginGradient()
    {
        var gradients = Enumerable.Range(0, Wrath335CloudNoise.GradientCount)
            .Select(value => value / 255f)
            .ToArray();
        var noise = new Wrath335CloudNoise(gradients);

        // P[P[P[0]]] == 218 in the build 12340 permutation.
        Assert.AreEqual(218f / 255f * 1.875f,
            noise.Sample(0, 0, 0, 0).Height, 0.00001f);
    }

    [TestMethod]
    public void CoverageUsesClientByteAndStartupLookup()
    {
        var lookup = Wrath335CloudDensityLut.Create();
        Assert.AreEqual((byte)101,
            Wrath335CloudNoise.ToCoverageByte(0.6f));
        Assert.AreEqual((byte)0,
            Wrath335CloudNoise.ToAlpha(100, 101, lookup));
        Assert.AreEqual((byte)0,
            Wrath335CloudNoise.ToAlpha(101, 101, lookup));
        Assert.IsTrue(Wrath335CloudNoise.ToAlpha(248, 101, lookup) > 0);
    }
}
