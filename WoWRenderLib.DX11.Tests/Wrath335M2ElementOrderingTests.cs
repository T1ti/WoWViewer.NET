using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class Wrath335M2ElementOrderingTests
{
    [TestMethod]
    public void ModelKeyUsesViewOriginSquaredLengthOrEligibleParentKey()
    {
        Assert.AreEqual(169f, Wrath335M2ElementOrdering.ModelDistance(new(3, 4, -12)));
        Assert.AreEqual(7f, Wrath335M2ElementOrdering.ModelDistance(new(3, 4, -12), 7));
        Assert.AreEqual(169f, Wrath335M2ElementOrdering.ModelDistance(new(3, 4, -12), 7, true));
    }

    [TestMethod]
    [DataRow(0, 25f)]
    [DataRow(1, 9f)]
    [DataRow(2, 49f)]
    [DataRow(3, 9f)]
    public void MeshFlagsSelectCenterNearOrFarSignedSquaredDistance(int flags, float expected)
    {
        Assert.AreEqual(new Wrath335M2DistanceKeys(expected, expected),
            Mesh(new(3, 0, 4), 2, (byte)flags));
        Assert.AreEqual(new Wrath335M2DistanceKeys(-expected, -expected),
            Mesh(new(3, 0, -4), 2, (byte)flags));
    }

    [TestMethod]
    public void NearRadiusCanCrossTheEyeAndReverseTheDistanceSign()
    {
        Assert.AreEqual(new Wrath335M2DistanceKeys(-1, -1), Mesh(new(0, 0, 1), 2, 1));
        Assert.AreEqual(new Wrath335M2DistanceKeys(1, 1), Mesh(new(0, 0, -1), 2, 1));
        Assert.AreEqual(new Wrath335M2DistanceKeys(0, 0), Mesh(Vector3.Zero, 2, 1));
    }

    [TestMethod]
    public void TinyCenterDirectionIsNotNormalizedAtTheNativeThreshold()
    {
        var threshold = BitConverter.Int32BitsToSingle(0x34800000);
        // Choose an exactly representable center below the threshold.
        var z = 1f / 4096;
        Assert.IsTrue(z * z < threshold);
        Assert.AreEqual((z * 3) * (z * 3), Mesh(new(0, 0, z), 2, 2).Secondary);
        Assert.AreEqual((3f / 4096) * (3f / 4096), Mesh(new(0, 0, 1f / 4096), -2, 1).Secondary);
        z = 1f / 2048;
        Assert.AreEqual(threshold, z * z);
        Assert.AreEqual(0f, Mesh(new(0, 0, z), 1, 1).Secondary); // Equality retains the raw direction.
        Assert.IsTrue(Mesh(new(0, 0, 1f / 1024), 1, 1).Secondary < -0.99f);
    }

    [TestMethod]
    public void OpaqueAndRawDistancePathsBypassRadiusAndEyeSideRules()
    {
        Assert.AreEqual(new Wrath335M2DistanceKeys(81, 81),
            Wrath335M2ElementOrdering.MeshDistance(new(0, 0, -5), 2, 3, 81, false, false, true, false, false));
        Assert.AreEqual(new Wrath335M2DistanceKeys(81, 25),
            Wrath335M2ElementOrdering.MeshDistance(new(0, 0, -5), 2, 3, 81, true, true, false, false, false));
    }

    [TestMethod]
    [DataRow(false, false, 81f)]
    [DataRow(true, false, 25f)]
    [DataRow(false, true, 25f)]
    public void ZFillEligibilitySelectsModelPrimaryWithoutLosingSectionSecondary(bool projected, bool noWrite, float primary)
    {
        Assert.AreEqual(new Wrath335M2DistanceKeys(primary, 25),
            Wrath335M2ElementOrdering.MeshDistance(new(3, 0, 4), 2, 0, 81, true, false, true, projected, noWrite));
    }

    [TestMethod]
    public void TransparentPrefixOrdersDistanceCloneSignedPriorityThenSecondary()
    {
        // The contradictory later fields prove each earlier field wins.
        var a = MeshElement() with { Distance = new(10, -100), PriorityPlane = 127, ModelIdentity = 900 };
        var b = MeshElement() with { Distance = new(9, 100), Flags = 1, PriorityPlane = -128, ModelIdentity = 1 };
        Before(a, b);
        a = a with { Distance = b.Distance, Flags = 1 };
        b = b with { Flags = 0 };
        Before(a, b);
        a = a with { Flags = 0, PriorityPlane = -128 };
        b = b with { PriorityPlane = 127 };
        Before(a, b);
        a = a with { PriorityPlane = 127, Distance = new(9, 101) };
        Before(a, b);
    }

    [TestMethod]
    public void ShaderGroupingCanPrecedeModelAndKindButRequiresBothEffects()
    {
        var a = MeshElement() with { ModelIdentity = 200, Shader = new(1, 2) };
        var b = MeshElement() with { ModelIdentity = 100, Shader = new(2, 1) };
        Before(a, b, true);
        Before(b, a, false);
        Before(b with { Shader = null }, a, true);
        a = a with { ModelIdentity = 100, Kind = Wrath335M2ElementKind.ProjectedMesh };
        Before(a, b, true);
        Before(b, a, false);
    }

    [TestMethod]
    public void SameModelKindOrdersLayersBeforeShadersAndGeometryFallbackStillGroupsShaders()
    {
        var a = MeshElement() with { MaterialLayer = 0, Shader = new(900, 900) };
        var b = MeshElement() with { MaterialLayer = 1, Shader = new(1, 1) };
        Before(a, b, true);
        Before(a, b, false);
        b = b with { MaterialLayer = 0 };
        Before(b, a, true);
        Before(b, a, false); // Opaque mesh fallback compares shader keys independently.
        a = a with { Shader = new(1, 2) };
        Before(b, a, true);
    }

    [TestMethod]
    public void UnsignedIdentitiesAndOpaqueGeometryFieldsKeepTheirNativeOrder()
    {
        var a = MeshElement() with { ModelIdentity = 0x7FFFFFFC };
        var b = MeshElement() with { ModelIdentity = 0x80000000 };
        Before(a, b);
        a = MeshElement() with { SharedModelIdentity = 1, Flags = 4, SectionBoneComboIndex = 10 };
        b = MeshElement() with { SharedModelIdentity = 2, Flags = 0, SectionBoneComboIndex = 1 };
        OpaqueBefore(a, b);
        b = b with { SharedModelIdentity = 1, Flags = 4, ModelIdentity = 200 };
        OpaqueBefore(a, b);
        a = a with { ModelIdentity = 200, Flags = 0 };
        OpaqueBefore(a, b);
        a = a with { Flags = 4, SectionBoneComboIndex = 0 };
        OpaqueBefore(a, b);
    }

    [TestMethod]
    public void ProjectedOpaqueFallbackSkipsNormalGeometryGrouping()
    {
        var a = MeshElement() with
        {
            Kind = Wrath335M2ElementKind.ProjectedMesh, ModelIdentity = 900,
            SharedModelIdentity = 900, MaterialLayer = 100, Shader = new(900, 900), BlendMode = 1
        };
        var b = MeshElement() with { Kind = Wrath335M2ElementKind.ProjectedMesh, BlendMode = 2 };
        OpaqueBefore(a, b);
        b = b with { BlendMode = 1, MaterialFlags = 0x10 };
        OpaqueBefore(a with { MaterialFlags = 0x20 }, b); // Upper flag bits do not participate.
    }

    [TestMethod]
    public void GeometryTexturePrefixesCountsAndBatchAddressBreakRemainingTies()
    {
        var a = MeshElement() with { TextureIdentities = new uint[] { 0x1000, 0x2000 }, BatchIdentity = 900 };
        var b = MeshElement() with { TextureIdentities = new uint[] { 0x1000, 0x3000 }, BatchIdentity = 1 };
        Before(a, b);
        OpaqueBefore(a, b);
        a = a with { TextureIdentities = new uint[] { 0x1000 } };
        OpaqueBefore(a, b);
        b = b with { TextureIdentities = a.TextureIdentities, BatchIdentity = 901 };
        OpaqueBefore(a, b);
        OpaqueBefore(a with { BlendMode = 0, MaterialFlags = 0x1F }, b with { BlendMode = 1, MaterialFlags = 0 });
    }

    [TestMethod]
    public void PointerTextureComparisonUsesWrappedSignedDwordDifference()
    {
        var a = MeshElement() with { TextureIdentities = new uint[] { 0xFFFFFFFC } };
        var b = MeshElement() with { TextureIdentities = new uint[] { 0 } };
        OpaqueBefore(a, b); // SUB wraps to -4, SAR 2 becomes -1.
        a = a with { TextureIdentities = new uint[] { 1 } };
        Assert.AreEqual(0, Wrath335M2ElementOrdering.CompareOpaque(a, b)); // +1 truncates to zero.
    }

    [TestMethod]
    public void RibbonTiesUseTexturePrefixThenCountThenEmitterIndex()
    {
        var a = MeshElement() with { Kind = Wrath335M2ElementKind.Ribbon, TextureIdentities = new uint[] { 100 }, RibbonIndex = 9 };
        var b = a with { TextureIdentities = new uint[] { 104 }, RibbonIndex = 0 };
        OpaqueBefore(a, b);
        b = b with { TextureIdentities = new uint[] { 100, 104 } };
        OpaqueBefore(a, b);
        b = b with { TextureIdentities = a.TextureIdentities, RibbonIndex = 10 };
        OpaqueBefore(a, b);
    }

    [TestMethod]
    [DataRow(7, 6)]
    [DataRow(6, 5)]
    [DataRow(5, 4)]
    [DataRow(4, 3)]
    [DataRow(3, 2)]
    [DataRow(2, 1)]
    [DataRow(1, 0)]
    public void ParticleFlagsReconstructOpaqueMaterialOrder(int firstFlags, int secondFlags)
    {
        var a = MeshElement() with { Kind = Wrath335M2ElementKind.ParticleRun, ParticleFlags = (uint)firstFlags };
        var b = a with { ParticleFlags = (uint)secondFlags };
        OpaqueBefore(a, b);
    }

    [TestMethod]
    public void ParticleFallbackUsesSignedBlendThenMaterialFlagsThenWrappedTextureIdentity()
    {
        var a = MeshElement() with { Kind = Wrath335M2ElementKind.ParticleRun, ParticleBlendMode = -1, ParticleFlags = 0 };
        var b = a with { ParticleBlendMode = 0, ParticleFlags = 7 };
        OpaqueBefore(a, b);
        a = b with { ParticleTextureIdentity = 0xFFFFFFFC };
        OpaqueBefore(a, b);
    }

    [TestMethod]
    public void UnorderedFloatKeysFallThroughToLaterNativeFields()
    {
        var a = MeshElement() with { Distance = new(float.NaN, float.NaN), PriorityPlane = -1 };
        var b = MeshElement() with { Distance = new(100, 100), PriorityPlane = 0 };
        Before(a, b);
        a = a with { PriorityPlane = 0, MaterialLayer = 1 };
        Before(b, a);
        Assert.AreEqual(0, Wrath335M2ElementOrdering.CompareTransparent(
            MeshElement() with { Distance = new(-0f, -0f) }, MeshElement(), false));
    }

    [TestMethod]
    public void NativeHeapPermutesCompleteTiesWithoutAddingInsertionOrder()
    {
        Wrath335M2ElementSortData[] elements = Enumerable.Repeat(
            new Wrath335M2ElementSortData { Kind = Wrath335M2ElementKind.Callback }, 5).ToArray();
        int[] indices = [0, 1, 2, 3, 4];
        Wrath335M2ElementOrdering.Sort(indices, elements, true);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 0 }, indices);
        int[] empty = [];
        Wrath335M2ElementOrdering.Sort(empty, elements, true);
        int[] singleton = [3];
        Wrath335M2ElementOrdering.Sort(singleton, elements, false);
        CollectionAssert.AreEqual(new[] { 3 }, singleton);
    }

    [TestMethod]
    public void CommonElementHeapInterleavesKindsAndKeepsDepthCloneBeforeColor()
    {
        Wrath335M2ElementSortData[] elements = [
            MeshElement() with { Distance = new(100, float.MaxValue) }, // Original ZFill color.
            MeshElement() with { Distance = new(100, 40), Flags = 1 }, // Depth clone precedes it.
            MeshElement() with { Kind = Wrath335M2ElementKind.Ribbon, Distance = new(101, 0) },
            MeshElement() with { Kind = Wrath335M2ElementKind.ParticleRun, Distance = new(99, 0) },
            MeshElement() with { Kind = Wrath335M2ElementKind.Callback, Distance = new(100, 80), PriorityPlane = -1 }
        ];
        int[] indices = [0, 1, 2, 3, 4];
        Wrath335M2ElementOrdering.Sort(indices, elements, true);
        CollectionAssert.AreEqual(new[] { 2, 1, 4, 0, 3 }, indices);
        indices = [4, 3, 2, 1, 0];
        Wrath335M2ElementOrdering.Sort(indices, elements, true);
        CollectionAssert.AreEqual(new[] { 2, 1, 4, 0, 3 }, indices);
    }

    [TestMethod]
    public void OpaqueHeapUsesMaterialStateInsteadOfTransparentDistance()
    {
        Wrath335M2ElementSortData[] elements = [
            MeshElement() with { MaterialLayer = 2, Distance = new(100, 100) },
            MeshElement() with { MaterialLayer = 0, BlendMode = 2 },
            MeshElement() with { MaterialLayer = 0, BlendMode = 1 },
            MeshElement() with { MaterialLayer = 1 }
        ];
        int[] indices = [0, 1, 2, 3];
        Wrath335M2ElementOrdering.Sort(indices, elements, false);
        CollectionAssert.AreEqual(new[] { 2, 1, 3, 0 }, indices);
    }

    private static Wrath335M2ElementSortData MeshElement() => new() { ModelIdentity = 100 };

    private static Wrath335M2DistanceKeys Mesh(Vector3 center, float radius, byte flags) =>
        Wrath335M2ElementOrdering.MeshDistance(center, radius, flags, 81, true, false, false, false, false);

    private static void Before(Wrath335M2ElementSortData a, Wrath335M2ElementSortData b, bool groupShaders = false)
    {
        Assert.IsTrue(Wrath335M2ElementOrdering.CompareTransparent(a, b, groupShaders) < 0);
        Assert.IsTrue(Wrath335M2ElementOrdering.CompareTransparent(b, a, groupShaders) > 0);
    }

    private static void OpaqueBefore(Wrath335M2ElementSortData a, Wrath335M2ElementSortData b)
    {
        Assert.IsTrue(Wrath335M2ElementOrdering.CompareOpaque(a, b) < 0);
        Assert.IsTrue(Wrath335M2ElementOrdering.CompareOpaque(b, a) > 0);
    }
}
