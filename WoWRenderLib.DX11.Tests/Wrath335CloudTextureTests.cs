using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335CloudTextureTests
{
    [TestMethod]
    public void ClientCloudTextureInitializesBothBuffersAndUpdatesEightRows()
    {
        var cloud = CreateFlatCloud();
        var initial = cloud.Update(0f, 0.6f, Vector3.UnitX,
            Vector3.Zero, Vector3.Zero, Vector3.UnitZ, 0f);
        Assert.AreEqual(new Wrath335CloudUpdate(0, 0, 128, true), initial);
        Assert.AreEqual(0, cloud.ActiveTextureIndex);
        CollectionAssert.AreEqual(
            cloud.GetPixels(0).ToArray(), cloud.GetPixels(1).ToArray());
        Assert.IsTrue(cloud.SampleDensity(Vector3.UnitZ) > 0);
        Assert.IsTrue(cloud.GetPixels(0)[2] > 0);
        Assert.AreEqual((byte)0, cloud.GetPixels(0)[0]);
        Assert.AreEqual((byte)0, cloud.GetPixels(0)[1]);
        var firstMip = cloud.CreateFirstMip(0);
        Assert.AreEqual(64 * 64 * 4, firstMip.Length);
        Assert.AreEqual(cloud.GetPixels(0)[2], firstMip[2]);

        for (var updateIndex = 0; updateIndex < 16; updateIndex++)
        {
            var update = cloud.Update(0.05f, 0.6f, Vector3.UnitX,
                Vector3.Zero, Vector3.Zero, Vector3.UnitZ, 0f);
            Assert.AreEqual(1, update.TextureIndex);
            Assert.AreEqual(updateIndex * 8, update.FirstRow);
            Assert.AreEqual(8, update.RowCount);
            Assert.IsFalse(update.InitializeBoth);
            if (updateIndex < 15)
                Assert.AreEqual(0, cloud.ActiveTextureIndex);
        }
        Assert.AreEqual(1, cloud.ActiveTextureIndex);
    }

    [TestMethod]
    public void CloudLightProjectionAndMoonHandoffUseClientGeometry()
    {
        var center = Wrath335CloudTextureGenerator.ProjectLightToTexture(
            Vector3.UnitZ, 128, 0f);
        var edge = Wrath335CloudTextureGenerator.ProjectLightToTexture(
            Vector3.UnitX, 128, 0f);
        Assert.AreEqual(new Vector3(64f, 64f, 64f), center);
        Assert.AreEqual(128f, edge.X, 0.0001f);
        Assert.AreEqual(64f, edge.Y, 0.0001f);

        Assert.IsTrue(Wrath335CloudTextureGenerator.UseMoon(0));
        Assert.IsTrue(Wrath335CloudTextureGenerator.UseMoon(570));
        Assert.IsFalse(Wrath335CloudTextureGenerator.UseMoon(600));
        Assert.IsTrue(Wrath335CloudTextureGenerator.UseMoon(2700));
    }

    [TestMethod]
    public void ClientCloudLodSelectsPowerOfTwoTextureSizes()
    {
        var noise = new Wrath335CloudNoise(
            new float[Wrath335CloudNoise.GradientCount]);
        for (var lod = 0; lod <= Wrath335CloudNoise.MaximumCloudLod; lod++)
        {
            var cloud = new Wrath335CloudTextureGenerator(lod, noise);
            Assert.AreEqual(128 << lod, cloud.Size);
        }
    }

    [TestMethod]
    public void ZeroAlphaCopiesLeftRgbButPreservesClientFirstColumn()
    {
        var gradients = Enumerable.Repeat(-1f, Wrath335CloudNoise.GradientCount)
            .ToArray();
        var cloud = new Wrath335CloudTextureGenerator(0,
            new Wrath335CloudNoise(gradients));
        cloud.Update(0f, 0.6f, Vector3.One, Vector3.One,
            Vector3.One, Vector3.UnitZ, 0f);
        var pixels = cloud.GetPixels(0);

        Assert.AreEqual((byte)255, pixels[3]);
        Assert.AreEqual((byte)0, pixels[7]);
        Assert.AreEqual((byte)255, pixels[4]);
        Assert.AreEqual((byte)255, pixels[5]);
        Assert.AreEqual((byte)255, pixels[6]);
    }

    private static Wrath335CloudTextureGenerator CreateFlatCloud() =>
        new(0, new Wrath335CloudNoise(
            new float[Wrath335CloudNoise.GradientCount]));
}
