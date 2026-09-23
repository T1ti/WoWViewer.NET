using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.Loaders;
using WoWRenderLib.Structs;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class WmoLiquidSmokeTests
{
    [TestMethod]
    public void MliqGridKeepsSharedTileAndRejectsEmptyNibble()
    {
        var parsed = WmoLiquidMeshBuilder.Build(new WmoLiquidInput
        {
            XVertices = 3, YVertices = 2, XTiles = 2, YTiles = 1,
            Origin = new Vector3(10, 20, 0),
            Heights = [1, 2, 3, 4, 5, 6],
            Depths = [0, 21, 42, 0, 21, 42],
            Tiles = [0x80, 0x0f],
            GroupLiquid = 0,
            GroupFlags = 0x8,
            MogiFlags = 0x8
        }, WorldLiquidMaterialCatalog.Shared);

        Assert.AreEqual(6, parsed.Indices.Length);
        CollectionAssert.AreEqual(new uint[] { 0, 3, 4, 0, 4, 1 }, parsed.Indices);
        Assert.AreEqual((ushort)13, parsed.Batches[0].LiquidTypeId);
        Assert.IsTrue(parsed.Batches[0].IsWmo);
        Assert.IsFalse(parsed.Batches[0].IsWmoInterior);
        Assert.AreEqual(10f, parsed.Bounds.Min.X);
        Assert.AreEqual(20f, parsed.Bounds.Min.Y);
        Assert.AreEqual(5f, parsed.Bounds.Max.Z);
    }

    [TestMethod]
    public void ModernTypePassesThroughAndInteriorTintIsApplied()
    {
        var parsed = WmoLiquidMeshBuilder.Build(new WmoLiquidInput
        {
            XVertices = 2, YVertices = 2, XTiles = 1, YTiles = 1,
            Heights = [0, 0, 0, 0],
            Tiles = [0],
            RootFlags = 0x4,
            GroupLiquid = 350,
            InteriorColor = new Vector4(0.5f, 0.25f, 1f, 0.75f)
        }, WorldLiquidMaterialCatalog.Shared);

        Assert.AreEqual((ushort)350, parsed.Batches[0].LiquidTypeId);
        Assert.IsTrue(parsed.Batches[0].IsWmoInterior);
        Assert.AreEqual(0.5f * WorldLiquidMaterialCatalog.Shared.Resolve(350, 0).ShallowColor.X,
            parsed.Materials[0].ShallowColor.X, 0.0001f);
    }

    [TestMethod]
    public void SharedTileClipsAgainstOverlappingNeighborPortal()
    {
        var step = WmoLiquidMeshBuilder.GridStep;
        var parsed = WmoLiquidMeshBuilder.Build(new WmoLiquidInput
        {
            XVertices = 2, YVertices = 2, XTiles = 1, YTiles = 1,
            Heights = [0, 0, 0, 0],
            Tiles = [0x80],
            SharedClips = [new WmoLiquidClip(
                Vector2.Zero, new Vector2(step),
                Vector3.UnitX, -2f, 1)]
        }, WorldLiquidMaterialCatalog.Shared);

        Assert.AreEqual(6, parsed.Indices.Length);
        Assert.AreEqual(2f, parsed.Bounds.Min.X, 0.0001f);
        Assert.AreEqual(step, parsed.Bounds.Max.X, 0.0001f);
    }

    [TestMethod]
    public void MalformedAndFullyEmptyGridsHaveNoGeometry()
    {
        var malformed = WmoLiquidMeshBuilder.Build(new WmoLiquidInput
        {
            XVertices = 2, YVertices = 2, XTiles = 2, YTiles = 1,
            Heights = [0, 0, 0, 0], Tiles = [0, 0]
        }, WorldLiquidMaterialCatalog.Shared);
        var empty = WmoLiquidMeshBuilder.Build(new WmoLiquidInput
        {
            XVertices = 2, YVertices = 2, XTiles = 1, YTiles = 1,
            Heights = [0, 0, 0, 0], Tiles = [0x8f]
        }, WorldLiquidMaterialCatalog.Shared);

        Assert.IsTrue(malformed.IsEmpty);
        Assert.IsTrue(empty.IsEmpty);
    }

    [TestMethod]
    public void LegacyLiquidTypeMappingFollowsWispGroupRules()
    {
        var input = new WmoLiquidInput { GroupLiquid = 0, Tiles = [0] };
        Assert.AreEqual((ushort)13, WmoLiquidMeshBuilder.ResolveLiquidType(input));
        Assert.AreEqual((ushort)14, WmoLiquidMeshBuilder.ResolveLiquidType(
            input with { GroupFlags = 0x80000 }));
        Assert.AreEqual((ushort)19, WmoLiquidMeshBuilder.ResolveLiquidType(
            input with { GroupLiquid = 2 }));
        Assert.AreEqual((ushort)20, WmoLiquidMeshBuilder.ResolveLiquidType(
            input with { GroupLiquid = 3 }));
        Assert.AreEqual((ushort)13, WmoLiquidMeshBuilder.ResolveLiquidType(
            input with { GroupLiquid = 15 }));
    }
}
