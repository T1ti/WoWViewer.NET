using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Loaders;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335M2FadeMaterialTests
{
    [DataTestMethod]
    [DataRow(0, 0)]
    [DataRow(1, 1)]
    [DataRow(2, 2)]
    [DataRow(3, 10)]
    [DataRow(4, 3)]
    [DataRow(5, 4)]
    [DataRow(6, 5)]
    public void FullAlphaLayersUseOpaqueBasePassAndTheirOwnBlendFamily(int layerBlend, int blendState)
    {
        var state = Wrath335M2FadeMaterial.Resolve(layerBlend, true, 0, 1, true, 1, 0);
        Assert.IsTrue(state.Draw);
        Assert.IsFalse(state.Translucent);
        Assert.AreEqual(blendState, state.BlendState);
        Assert.AreEqual(layerBlend == 1 ? 224f / 255f : layerBlend > 1 ? 1f / 255f : -1f,
            state.AlphaReference);
    }

    [DataTestMethod]
    [DataRow(0, 2)]
    [DataRow(1, 2)]
    [DataRow(3, 10)]
    [DataRow(6, 5)]
    public void TranslucentBaseDemotesLayersWithoutReplacingTheirCutoutOrBlendFamily(int layerBlend, int blendState)
    {
        var state = Wrath335M2FadeMaterial.Resolve(layerBlend, true, 0, 1, true, 1, 2);
        Assert.IsTrue(state.Translucent);
        Assert.AreEqual(blendState, state.BlendState);
        Assert.AreEqual(layerBlend == 1 ? 224f / 255f : layerBlend > 1 ? 1f / 255f : -1f,
            state.AlphaReference);
    }

    [TestMethod]
    public void LayerAlphaStillDemotesOpaqueBaseAndSuppressesInvisibleBatches()
    {
        var state = Wrath335M2FadeMaterial.Resolve(3, true, 0, 0.5f, true, 0.5f, 0);
        Assert.IsTrue(state.Translucent);
        Assert.AreEqual(10, state.BlendState);
        Assert.IsFalse(Wrath335M2FadeMaterial.Resolve(3, true, 0, 0.0001f, true, 0.5f, 0).Draw);
    }

    [TestMethod]
    public void DecodedMaterialLayerIsAnOffsetInTheMaterialTableRatherThanTheBatchList()
    {
        M2Loader.M2RenderMaterial[] materials = [new(0, 2), new(4, 0), new(0x10, 3), new(0, 6)];
        var own = M2Loader.ResolveRenderMaterial(3, materials);
        var baseMaterial = M2Loader.ResolveBaseRenderMaterial(3, 2, materials);
        Assert.AreEqual((ushort)0, baseMaterial.BlendMode);
        Assert.AreEqual((ushort)4, baseMaterial.Flags);
        Assert.IsFalse(Wrath335M2FadeMaterial.Resolve(own.BlendMode, true, own.Flags, 1, true, 1,
            baseMaterial.BlendMode).Translucent);
        Assert.IsTrue(Wrath335M2FadeMaterial.Resolve(own.BlendMode, true, own.Flags, 1, true, 1,
            M2Loader.ResolveBaseRenderMaterial(3, 3, materials).BlendMode).Translucent);
        Assert.AreEqual(own, M2Loader.ResolveBaseRenderMaterial(3, 0, materials));
        Assert.AreEqual(own, M2Loader.ResolveBaseRenderMaterial(3, 4, materials));
        Assert.AreEqual(default(M2Loader.M2RenderMaterial), M2Loader.ResolveBaseRenderMaterial(9, 0, materials));
    }

    [TestMethod]
    public void BaselineModelsIgnoreNativeLayerPartitionMetadata()
    {
        var opaque = Wrath335M2FadeMaterial.Resolve(1, false, 0, 1, false, 1, 2);
        Assert.IsFalse(opaque.Translucent);
        Assert.AreEqual(128f / 255f, opaque.AlphaReference);
        Assert.IsTrue(Wrath335M2FadeMaterial.Resolve(3, false, 0, 1, false, 1, 0).Translucent);
    }

    [DataTestMethod]
    [DataRow(0, 0, 2)]
    [DataRow(1, 1, 2)]
    [DataRow(2, 2, 2)]
    [DataRow(3, 10, 10)]
    [DataRow(4, 3, 3)]
    [DataRow(5, 4, 4)]
    [DataRow(6, 5, 5)]
    public void FadeChangesOpaqueKeyBlendAndRetainsTheOtherFamilies(int mode, int fullBlend, int fadedBlend)
    {
        var full = Resolve(mode, 1, 1);
        var faded = Resolve(mode, 1, 0.5f);
        Assert.IsTrue(full.Draw);
        Assert.AreEqual(fullBlend, full.BlendState);
        Assert.AreEqual(mode > 1, full.Translucent);
        Assert.AreEqual(fadedBlend, faded.BlendState);
        Assert.IsTrue(faded.Translucent);
    }

    [TestMethod]
    public void ComposedAnimationAndFadeAlphaControlsPassAndInvisibleBoundaries()
    {
        Assert.IsFalse(Resolve(0, Wrath335M2FadeMaterial.OpaqueThreshold, 1).Translucent);
        Assert.IsTrue(Resolve(0, MathF.BitDecrement(Wrath335M2FadeMaterial.OpaqueThreshold), 1).Translucent);
        Assert.IsTrue(Resolve(1, 0.5f, 1).Translucent);
        Assert.IsTrue(Resolve(0, 0.0001f, 1).Draw);
        Assert.IsFalse(Resolve(0, MathF.BitDecrement(0.0001f), 1).Draw);
        Assert.IsFalse(Resolve(2, 0.0001f, 0.5f).Draw);
    }

    [TestMethod]
    public void AlphaReferencesUseNativeComposedAlphaAndByteStores()
    {
        Assert.AreEqual(-1f, Resolve(0, 1, 0.5f).AlphaReference);
        Assert.AreEqual(224f / 255f, Resolve(1, 1, 1).AlphaReference);
        Assert.AreEqual(112f / 255f, Resolve(1, 1, 0.5f).AlphaReference);
        Assert.AreEqual(56f / 255f, Resolve(1, 0.5f, 0.5f).AlphaReference);
        Assert.AreEqual(111f / 255f, Resolve(1, 1, 0.499f).AlphaReference);
        Assert.AreEqual(-1f, Resolve(1, 1, 0.001f).AlphaReference);
        Assert.AreEqual(1f / 255f, Resolve(2, 1, 0.5f).AlphaReference);
    }

    [DataTestMethod]
    [DataRow((ushort)0, 0)]
    [DataRow((ushort)0x10, 1)]
    [DataRow((ushort)0x8, 2)]
    [DataRow((ushort)0x18, 2)]
    public void FadeKeepsTheAuthoredDepthFlags(ushort flags, int depth)
    {
        Assert.AreEqual((M2DepthMode)depth, M2DepthPolicy.ForMaterial(true, flags));
        Assert.AreEqual(2, Resolve(0, 1, 0.5f, flags).BlendState);
        Assert.IsTrue(Resolve(0, 1, 0.5f, flags).Translucent);
        Assert.IsFalse(Resolve(0, 1, 1, flags).Translucent); // Depth flags do not select the native queue.
        Assert.AreEqual(flags != 0, Wrath335M2FadeMaterial.Resolve(0, true, flags, 1, false, 1).Translucent);
    }

    [TestMethod]
    public void BaselineModelsKeepTheirExistingBlendCutoutAndPassBehavior()
    {
        var baseline = Wrath335M2FadeMaterial.Resolve(1, false, 0, 0.2f, false, 0.5f);
        Assert.IsTrue(baseline.Draw);
        Assert.IsFalse(baseline.Translucent);
        Assert.AreEqual(1, baseline.BlendState);
        Assert.AreEqual(128f / 255f, baseline.AlphaReference);
    }

    private static Wrath335M2FadeMaterial Resolve(int mode, float materialAlpha, float opacity, ushort flags = 0) =>
        Wrath335M2FadeMaterial.Resolve(mode, true, flags, materialAlpha, true, opacity);
}
