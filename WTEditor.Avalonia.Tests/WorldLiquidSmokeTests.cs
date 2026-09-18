using System.Numerics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWLib;
using WoWLib.Database;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Loaders;
using WoWRenderLib.Structs;
using WoWRenderLib.Database;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class WorldLiquidSmokeTests
{
    [TestMethod]
    public void EmptyExistsBitmapEmitsEveryQuadAndUsesMhxAxes()
    {
        var result = Build(new WorldLiquidLayerInput
        {
            ChunkIndex = 0,
            LayerIndex = 2,
            ChunkPosition = new Vector3(100f, 200f, 0f),
            LiquidTypeId = 1,
            VertexFormat = WorldLiquidVertexFormat.HeightDepth,
            MinHeight = 10f,
            Width = 2,
            Height = 1,
            Heightmap = [10f, 11f, 12f, 13f, 14f, 15f],
            Depthmap = [0, 64, 128, 192, 224, 255]
        });

        Assert.AreEqual(6, result.Vertices.Length);
        Assert.AreEqual(12, result.Indices.Length);
        Assert.AreEqual(100f, result.Vertices[0].Position.X, 0.0001f);
        Assert.AreEqual(200f, result.Vertices[0].Position.Y, 0.0001f);
        Assert.AreEqual(10f, result.Vertices[0].Position.Z, 0.0001f);
        Assert.AreEqual(1f, result.Vertices[^1].Depth, 0.0001f);
        Assert.AreEqual(2, result.Batches[0].LayerIndex);
    }

    [TestMethod]
    public void SparseExistsBitmapUsesLsbFirstQuadSelection()
    {
        var result = Build(new WorldLiquidLayerInput
        {
            ChunkIndex = 0,
            ChunkPosition = Vector3.Zero,
            LiquidTypeId = 1,
            VertexFormat = WorldLiquidVertexFormat.DepthOnly,
            MinHeight = 4f,
            Width = 3,
            Height = 2,
            ExistsBitmap = [0b0000_0101]
        });

        Assert.AreEqual(12, result.Indices.Length);
        Assert.AreEqual(1, result.Batches.Length);
        Assert.AreEqual(12u, result.Batches[0].IndexCount);
    }

    [TestMethod]
    public void HeightUvDepthReadsAllAttributesAndBoundsEmittedVertices()
    {
        var layer = new WorldLiquidLayerInput
        {
            ChunkIndex = 3,
            ChunkPosition = new Vector3(1_000f, 2_000f, 0f),
            LiquidTypeId = 5,
            LiquidObjectOrLvf = 42,
            VertexFormat = WorldLiquidVertexFormat.HeightUvDepth,
            XOffset = 1,
            YOffset = 2,
            Width = 1,
            Height = 1,
            Heightmap = [20f, 21f, 22f, 23f],
            Depthmap = [0, 64, 128, 255],
            Uvmap = [new(0, 8), new(8, 16), new(16, 24), new(24, 32)]
        };

        var result = Build(layer);
        Assert.AreEqual(new Vector2(0f, 8f * 3f / 256f), result.Vertices[0].TexCoord);
        Assert.AreEqual(new Vector2(24f * 3f / 256f, 32f * 3f / 256f), result.Vertices[^1].TexCoord);
        Assert.AreEqual(0f, result.Vertices[0].Depth, 0.0001f);
        Assert.AreEqual(1f, result.Vertices[^1].Depth, 0.0001f);
        Assert.AreEqual(20f, result.Bounds.Min.Z, 0.0001f);
        Assert.AreEqual(23f, result.Bounds.Max.Z, 0.0001f);
        Assert.AreEqual((ushort)42, result.Batches[0].LiquidObjectOrLvf);
    }

    [TestMethod]
    public void MissingDepthDefaultsToShallowForHeightUvLayers()
    {
        var result = Build(new WorldLiquidLayerInput
        {
            ChunkIndex = 0,
            LiquidTypeId = 1,
            VertexFormat = WorldLiquidVertexFormat.HeightUv,
            MinHeight = 12f,
            Width = 1,
            Height = 1,
            Heightmap = [12f, 12f, 12f, 12f],
            Uvmap = [new(0, 0), new(0, 0), new(0, 0), new(0, 0)]
        });

        Assert.AreEqual(4, result.Vertices.Length);
        Assert.IsTrue(result.Vertices.All(static vertex => Math.Abs(vertex.Depth) < 0.0001f));
    }

    [TestMethod]
    public void FlatHeightLayoutUsesMinHeightWhenHeightmapIsAbsent()
    {
        var result = Build(new WorldLiquidLayerInput
        {
            ChunkIndex = 0,
            LiquidTypeId = 1,
            VertexFormat = WorldLiquidVertexFormat.HeightDepth,
            MinHeight = 17f,
            Width = 1,
            Height = 1
        });

        Assert.AreEqual(4, result.Vertices.Length);
        Assert.IsTrue(result.Vertices.All(vertex => Math.Abs(vertex.Position.Z - 17f) < 0.0001f));
        Assert.AreEqual(17f, result.Bounds.Min.Z, 0.0001f);
        Assert.AreEqual(17f, result.Bounds.Max.Z, 0.0001f);
    }

    [TestMethod]
    public void AllFormatsHaveStableFallbacksAndMultipleLayersRemainBatches()
    {
        var layers = new[]
        {
            new WorldLiquidLayerInput
            {
                ChunkIndex = 0, LayerIndex = 0, LiquidTypeId = 1,
                VertexFormat = WorldLiquidVertexFormat.HeightDepth,
                Width = 1, Height = 1, MinHeight = 6f,
                Heightmap = [6f, 6f, 6f, 6f]
            },
            new WorldLiquidLayerInput
            {
                ChunkIndex = 0, LayerIndex = 1, LiquidTypeId = 3,
                VertexFormat = WorldLiquidVertexFormat.HeightUv,
                Width = 1, Height = 1, MinHeight = 7f,
                Heightmap = [7f, 7f, 7f, 7f],
                Uvmap = [new(8, 8), new(8, 8), new(8, 8), new(8, 8)]
            },
            new WorldLiquidLayerInput
            {
                ChunkIndex = 0, LayerIndex = 2, LiquidTypeId = 2,
                VertexFormat = WorldLiquidVertexFormat.DepthOnly,
                Width = 1, Height = 1, MinHeight = 8f
            },
            new WorldLiquidLayerInput
            {
                ChunkIndex = 0, LayerIndex = 3, LiquidTypeId = 5,
                VertexFormat = WorldLiquidVertexFormat.HeightUvDepth,
                Width = 1, Height = 1, MinHeight = 9f,
                Heightmap = [9f, 9f, 9f, 9f],
                Uvmap = [new(0, 0), new(0, 0), new(0, 0), new(0, 0)],
                Depthmap = [255, 255, 255, 255]
            }
        };

        var result = Build(layers);
        Assert.AreEqual(4, result.Batches.Length);
        Assert.AreEqual(WorldLiquidMaterialFamily.Water, result.Materials[0].Family);
        Assert.AreEqual(WorldLiquidMaterialFamily.Swamp, result.Materials[1].Family);
        Assert.AreEqual(WorldLiquidMaterialFamily.Water, result.Materials[2].Family);
        Assert.AreEqual(WorldLiquidMaterialFamily.Magma, result.Materials[3].Family);
        Assert.AreEqual(WorldLiquidWaterType.River, result.Materials[0].WaterType);
        Assert.AreEqual(WorldLiquidWaterType.Ocean, result.Materials[2].WaterType);
        Assert.AreEqual(16, result.Vertices.Length);
    }

    [TestMethod]
    public void MalformedRectangleIsSkippedWithoutPoisoningBounds()
    {
        var result = Build(
            new WorldLiquidLayerInput
            {
                ChunkIndex = 0,
                Width = 8,
                Height = 8,
                XOffset = 4,
                VertexFormat = WorldLiquidVertexFormat.DepthOnly,
                MinHeight = 1f
            },
            new WorldLiquidLayerInput
            {
                ChunkIndex = 1,
                Width = 1,
                Height = 1,
                VertexFormat = WorldLiquidVertexFormat.DepthOnly,
                MinHeight = 3f
            });

        Assert.AreEqual(1, result.Batches.Length);
        Assert.AreEqual(3f, result.Bounds.Min.Z, 0.0001f);
        Assert.IsTrue(float.IsFinite(result.Bounds.Max.Z));
    }

    [TestMethod]
    public void AggregateMeshRetains32BitIndicesWhenLayersExceed16BitVertices()
    {
        var layers = Enumerable.Range(0, 820)
            .Select(index => new WorldLiquidLayerInput
            {
                ChunkIndex = index % 256,
                LayerIndex = index,
                ChunkPosition = new Vector3(index % 16, index / 16, 0f),
                LiquidTypeId = 1,
                VertexFormat = WorldLiquidVertexFormat.DepthOnly,
                MinHeight = 4f,
                Width = 8,
                Height = 8
            })
            .ToArray();

        var result = Build(layers);

        Assert.IsTrue(result.Vertices.Length > ushort.MaxValue);
        Assert.IsTrue(result.Indices.Max() > ushort.MaxValue);
        Assert.AreEqual(layers.Length, result.Batches.Length);
    }

    [TestMethod]
    public void TileAggregateIncludesLiquidHeightAboveTerrain()
    {
        var tileBounds = new TileSceneBounds(default);
        tileBounds.SetTerrain(
            42,
            new BoundingBox(new Vector3(-1f, -1f, 0f), new Vector3(1f, 1f, 1f)),
            new BoundingBox(new Vector3(-0.5f, -0.5f, 25f), new Vector3(0.5f, 0.5f, 30f)));

        Assert.IsTrue(tileBounds.TryGetCombinedBounds(out var combined));
        Assert.AreEqual(30f, combined.Max.Z, 0.0001f);
        Assert.AreEqual(0f, combined.Min.Z, 0.0001f);
    }

    [TestMethod]
    public void WowlibLiquidDb2ColumnsUseExactSchemaNames()
    {
        using var version = new ClientVersion(11, 0, 0, 0, ClientFlavor.Retail);
        using var table = Table.Open("LiquidTypeXTexture", version);

        var liquidTypeColumn = Db2Schema.RequireColumn(
            table,
            "LiquidTypeXTexture",
            "liquid_type_id");
        var fileDataColumn = Db2Schema.RequireColumn(
            table,
            "LiquidTypeXTexture",
            "file_data_id");
        var orderColumn = Db2Schema.RequireColumn(
            table,
            "LiquidTypeXTexture",
            "order_index");

        Assert.AreEqual("liquid_type_id", table.ColumnInfo(liquidTypeColumn).Name);
        Assert.AreEqual("file_data_id", table.ColumnInfo(fileDataColumn).Name);
        Assert.AreEqual("order_index", table.ColumnInfo(orderColumn).Name);
        var exception = Assert.ThrowsException<InvalidDataException>(() =>
            Db2Schema.RequireColumn(table, "LiquidTypeXTexture", "LiquidTypeID"));
        StringAssert.Contains(exception.Message, "liquid_type_id");
    }

    [TestMethod]
    public void WaterUsesLightParamsAlphaWithoutWaveTextureMaskingCoverage()
    {
        var shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "liquid.hlsl");
        var shaderSource = File.ReadAllText(shaderPath);

        Assert.IsFalse(
            shaderSource.Contains("surface.a * sampled.a", StringComparison.Ordinal),
            "The water wave texture alpha must not make the MH2O surface transparent.");
        Assert.IsFalse(
            shaderSource.Contains("sampled.a *", StringComparison.Ordinal),
            "The animated liquid texture alpha must not mask the MH2O surface.");
        StringAssert.Contains(
            shaderSource,
            "surface.a = lerp(closeAlpha, farAlpha, depthMix);");
        StringAssert.Contains(
            shaderSource,
            "float finalCoverage = saturate(surface.a);");
        StringAssert.Contains(
            shaderSource,
            "? surface.rgb * waveDetail");
        StringAssert.Contains(
            shaderSource,
            "float3 lighting = lightingAmbient.rgb + lightingDiffuse.rgb * directional;");
        Assert.IsFalse(
            shaderSource.Contains("0.35f + 0.65f", StringComparison.Ordinal),
            "Direct lighting must use the reference Lambert term without a fabricated minimum.");
        Assert.IsFalse(
            WorldLiquidRenderer.UsesOpaqueComposition(WorldLiquidMaterialFamily.Water));
        Assert.IsFalse(
            WorldLiquidRenderer.UsesOpaqueComposition(WorldLiquidMaterialFamily.Swamp));
    }

    [TestMethod]
    public void LiquidLightingPaletteMatchesReferenceWaterTypeSelection()
    {
        Assert.IsFalse(WorldLiquidRenderer.UsesRiverLightingPalette(
            WorldLiquidWaterType.Ocean));
        Assert.IsTrue(WorldLiquidRenderer.UsesRiverLightingPalette(
            WorldLiquidWaterType.River));
        Assert.IsTrue(WorldLiquidRenderer.UsesRiverLightingPalette(
            WorldLiquidWaterType.Wmo));
        Assert.IsTrue(WorldLiquidRenderer.UsesRiverLightingPalette(
            WorldLiquidWaterType.Unknown));
    }

    [TestMethod]
    public void UnknownClientMaterialIdsFollowReferenceWaterFallback()
    {
        Assert.AreEqual(
            WorldLiquidMaterialFamily.Water,
            WorldLiquidMaterialCatalog.ClassifyMaterialId(130));
        Assert.AreEqual(
            WorldLiquidWaterType.Ocean,
            WorldLiquidMaterialCatalog.DefaultWaterType(
                1250,
                WorldLiquidMaterialFamily.Water,
                hasModernTextureData: true));
        Assert.AreEqual(
            WorldLiquidWaterType.Unknown,
            WorldLiquidMaterialCatalog.DefaultWaterType(
                3,
                WorldLiquidMaterialFamily.Magma,
                hasModernTextureData: false));
    }

    [TestMethod]
    public void SimpleWaterFallbackDoesNotBindIncompleteHighDetailInputs()
    {
        var material = new WorldLiquidMaterialDescriptor(
            new WorldLiquidMaterialKey(2, 2),
            WorldLiquidMaterialFamily.Water,
            Vector4.One,
            Vector4.One,
            1f,
            0f,
            0f,
            [10, 20, 30, 40])
        {
            WaterType = WorldLiquidWaterType.Ocean,
            TextureSlots =
            [
                new WorldLiquidTextureSlot([10]),
                new WorldLiquidTextureSlot([]),
                new WorldLiquidTextureSlot([30, 31]),
                new WorldLiquidTextureSlot([40])
            ]
        };

        Assert.IsTrue(WorldLiquidRenderer.IsWaterMaterial(material));

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "liquid.hlsl");
        var shaderSource = File.ReadAllText(shaderPath);
        StringAssert.Contains(shaderSource, "Texture2D liquidTexture : register(t0);");
        Assert.IsFalse(shaderSource.Contains("register(t1)", StringComparison.Ordinal));
    }

    private static ParsedWorldLiquid Build(params WorldLiquidLayerInput[] layers) =>
        WorldLiquidMeshBuilder.Build(layers, WorldLiquidMaterialCatalog.Shared);
}
