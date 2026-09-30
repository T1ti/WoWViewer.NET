using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Application;
using WTEditor.Application.Commands;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.Presentation;
using WTEditor.Avalonia.Controls;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Views;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Raycasting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Loaders;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;
using WoWRenderLib.Services;

namespace WTEditor.Avalonia.Tests;

public sealed partial class EditorSettingsSmokeTests
{
    [TestMethod]
    public void ScreenSpaceCulling_AccountsForSphereRadiusAtRenderDistanceBoundary()
    {
        Assert.IsTrue(ScreenSpaceCulling.IntersectsRenderDistance(
            Vector3.Zero,
            new Vector3(110f, 0f, 0f),
            10f,
            100f));
        Assert.IsFalse(ScreenSpaceCulling.IntersectsRenderDistance(
            Vector3.Zero,
            new Vector3(110.01f, 0f, 0f),
            10f,
            100f));
        Assert.IsTrue(ScreenSpaceCulling.IntersectsRenderDistanceSquared(
            110f * 110f, 10f, 100f));
        Assert.IsFalse(ScreenSpaceCulling.IntersectsRenderDistanceSquared(
            110.01f * 110.01f, 10f, 100f));
        Assert.IsFalse(ScreenSpaceCulling.IntersectsRenderDistanceSquared(
            float.NaN, 10f, 100f));

        Assert.IsTrue(ScreenSpaceCulling.IsFullyWithinRenderDistance(
            Vector3.Zero,
            new Vector3(90f, 0f, 0f),
            10f,
            100f));
        Assert.IsFalse(ScreenSpaceCulling.IsFullyWithinRenderDistance(
            Vector3.Zero,
            new Vector3(90.01f, 0f, 0f),
            10f,
            100f));
        Assert.IsFalse(ScreenSpaceCulling.IsFullyWithinRenderDistance(
            Vector3.Zero,
            Vector3.Zero,
            101f,
            100f));
    }

    [TestMethod]
    public void TriangleMeshRaycaster_RejectsBoundsOnlyHitsAndReturnsWorldDistance()
    {
        Vector3[] vertices =
        [
            new(0f, 0f, 0f),
            new(1f, 0f, 0f),
            new(0f, 1f, 0f)
        ];
        ushort[] indices = [0, 1, 2];
        var modelMatrix = Matrix4x4.CreateScale(2f) * Matrix4x4.CreateTranslation(0f, 0f, 5f);
        var hitRay = new Ray(new Vector3(0.5f, 0.5f, 10f), -Vector3.UnitZ);

        Assert.IsTrue(TriangleMeshRaycaster.TryCreateContext(hitRay, modelMatrix, out var hitContext));
        Assert.IsTrue(TriangleMeshRaycaster.TryIntersectTriangles(
            hitContext,
            vertices,
            indices,
            float.MaxValue,
            out var distance));
        Assert.AreEqual(5f, distance, 1e-4f);
        Assert.IsFalse(TriangleMeshRaycaster.TryIntersectTriangles(
            hitContext,
            vertices,
            indices,
            4.99f,
            out _));

        // This ray crosses the transformed mesh bounds, but not the triangle itself.
        var boundsOnlyRay = new Ray(new Vector3(1.5f, 1.5f, 10f), -Vector3.UnitZ);
        Assert.IsTrue(TriangleMeshRaycaster.TryCreateContext(boundsOnlyRay, modelMatrix, out var missContext));
        Assert.IsFalse(TriangleMeshRaycaster.TryIntersectTriangles(
            missContext,
            vertices,
            indices,
            float.MaxValue,
            out _));
    }

    [TestMethod]
    public void M2SelectionRaycaster_UsesOrientedBoxForParticleModels()
    {
        var localBox = new BoundingBox(
            new Vector3(-1f, -0.1f, -0.1f),
            new Vector3(1f, 0.1f, 0.1f));
        var model = new ParsedDoodadBatch
        {
            boundingBox = localBox,
            particleEmitterCount = 1,
            raycastVertices = [],
            raycastIndices = []
        };
        var transform = Matrix4x4.CreateRotationZ(MathF.PI / 4f);
        var hitRay = new Ray(new Vector3(0f, 0f, 5f), -Vector3.UnitZ);
        Assert.IsTrue(M2SelectionRaycaster.TryIntersect(
            hitRay, model, transform, float.MaxValue, out var hitDistance));
        Assert.AreEqual(4.9f, hitDistance, 1e-4f);

        var aabbOnlyRay = new Ray(new Vector3(0.7f, -0.7f, 5f), -Vector3.UnitZ);
        Assert.IsTrue(IntersectionTests.RayIntersectsBox(
            aabbOnlyRay, BoundingBox.Transform(localBox, transform), out _));
        Assert.IsFalse(M2SelectionRaycaster.TryIntersect(
            aabbOnlyRay, model, transform, float.MaxValue, out _));
        Assert.IsFalse(M2SelectionRaycaster.TryIntersect(
            hitRay, model, transform, 4.89f, out _));
    }

    [TestMethod]
    public void M2SelectionRaycaster_UsesClickTimeParticleQuadBounds()
    {
        var model = new ParsedDoodadBatch
        {
            boundingBox = new BoundingBox(
                new Vector3(-3f, -1f, -0.5f),
                new Vector3(3f, 1f, 0.5f)),
            particleEmitterCount = 1,
            raycastVertices = [],
            raycastIndices = []
        };
        var vertices = new M2RibbonVertex[8];
        for (var quad = 0; quad < 2; quad++)
        {
            var x = quad == 0 ? -2f : 2f;
            var start = quad * 4;
            vertices[start] = new(new Vector3(x - 0.5f, -0.5f, 0f), default, default);
            vertices[start + 1] = new(new Vector3(x + 0.5f, -0.5f, 0f), default, default);
            vertices[start + 2] = new(new Vector3(x + 0.5f, 0.5f, 0f), default, default);
            vertices[start + 3] = new(new Vector3(x - 0.5f, 0.5f, 0f), default, default);
        }
        M2RibbonMesh[] meshes =
        [
            new(vertices, [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7]),
            // A stale cache entry at an index absent from the renderable set.
            new(
            [
                new(new Vector3(-0.25f, -0.25f, 0f), default, default),
                new(new Vector3(0.25f, -0.25f, 0f), default, default),
                new(new Vector3(0.25f, 0.25f, 0f), default, default),
                new(new Vector3(-0.25f, 0.25f, 0f), default, default)
            ],
            [0, 1, 2, 0, 2, 3])
        ];
        var transform = Matrix4x4.CreateRotationZ(MathF.PI / 4f);
        var particleRay = new Ray(
            Vector3.Transform(new Vector3(2f, 0f, 5f), transform), -Vector3.UnitZ);
        Assert.IsTrue(M2SelectionRaycaster.TryIntersect(
            particleRay, model, transform, float.MaxValue, meshes, [0], out var distance));
        Assert.AreEqual(4.95f, distance, 1e-4f);
        Assert.IsFalse(M2SelectionRaycaster.TryIntersect(
            particleRay, model, transform, 4.94f, meshes, [0], out _));

        // The ray crosses the authored model box, but the space between the
        // two particle quads is empty.
        var gapRay = new Ray(new Vector3(0f, 0f, 5f), -Vector3.UnitZ);
        Assert.IsFalse(M2SelectionRaycaster.TryIntersect(
            gapRay, model, transform, float.MaxValue, meshes, [0], out _));
    }

    [TestMethod]
    public void M2SelectionRaycaster_StillRequiresTrianglesWithoutParticles()
    {
        var model = new ParsedDoodadBatch
        {
            boundingBox = new BoundingBox(Vector3.Zero, Vector3.One),
            particleEmitterCount = 0,
            raycastVertices =
            [
                Vector3.Zero,
                Vector3.UnitX,
                Vector3.UnitY
            ],
            raycastIndices = [0, 1, 2]
        };
        var triangleRay = new Ray(new Vector3(0.25f, 0.25f, 5f), -Vector3.UnitZ);
        Assert.IsTrue(M2SelectionRaycaster.TryIntersect(
            triangleRay, model, Matrix4x4.Identity, float.MaxValue, out var distance));
        Assert.AreEqual(5f, distance, 1e-4f);

        var boundsOnlyRay = new Ray(new Vector3(0.9f, 0.9f, 5f), -Vector3.UnitZ);
        Assert.IsFalse(M2SelectionRaycaster.TryIntersect(
            boundsOnlyRay, model, Matrix4x4.Identity, float.MaxValue, out _));
    }

    [TestMethod]
    public void Wowlib_UsesModernFormatLineageForClassicEra115()
    {
        var version = new WoWLib.ClientVersion(1, 15, 9, 69109, WoWLib.ClientFlavor.ClassicEra);
        var wmo = WoWLib.Formats.WMO.WMO.ForVersion(version);

        Assert.IsTrue(version.IsClassic);
        Assert.AreNotEqual("WMO", wmo.GetType().Name);
        Assert.IsFalse(wmo.GetType().Name.Contains("Vanilla", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WmoMotvChunksAreMappedToVertexTextureCoordinateSets()
    {
        var bytes = new byte[2 * (8 + 16)];
        WriteMotvChunk(bytes, 0, (0.1f, 0.2f), (0.3f, 0.4f));
        WriteMotvChunk(bytes, 24, (0.5f, 0.6f), (0.7f, 0.8f));

        using var body = WoWLib.Formats.WMO.Group.WMOGroupBody.ForVersion(WoWLib.Expansion.Wotlk);
        body.Read(new byte[68].Concat(bytes).ToArray());
        var sets = WMOLoader.ReadTextureCoordinateSets(body, 2);

        Assert.AreEqual(new Vector2(0.1f, 0.2f), sets[0][0]);
        Assert.AreEqual(new Vector2(0.3f, 0.4f), sets[0][1]);
        Assert.AreEqual(new Vector2(0.5f, 0.6f), sets[1][0]);
        Assert.AreEqual(new Vector2(0.7f, 0.8f), sets[1][1]);
    }

    private static void WriteMotvChunk(
        byte[] bytes,
        int offset,
        (float X, float Y) first,
        (float X, float Y) second)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), 0x4D4F5456);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 4, 4), 16);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 8, 4), BitConverter.SingleToInt32Bits(first.X));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 12, 4), BitConverter.SingleToInt32Bits(first.Y));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 16, 4), BitConverter.SingleToInt32Bits(second.X));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 20, 4), BitConverter.SingleToInt32Bits(second.Y));
    }

    [TestMethod]
    public void Wowlib_ClassicEraDirectPlacementIdsAreNotTreatedAsNameIndexes()
    {
        const uint modelFileDataId = 204129;
        const uint doodadEntryIsFileDataId = 0x40;
        const uint wmoEntryIsFileDataId = 0x8;

        Assert.AreEqual(
            modelFileDataId,
            ADTLoader.ResolvePlacementFileDataId(
                null!, null, null, modelFileDataId, doodadEntryIsFileDataId, doodadEntryIsFileDataId));
        Assert.AreEqual(
            modelFileDataId,
            ADTLoader.ResolvePlacementFileDataId(
                null!, null, null, modelFileDataId, wmoEntryIsFileDataId, wmoEntryIsFileDataId));
    }

    [TestMethod]
    public void AdtVertexRowsUseTheNonOverlappingDiamondLayout()
    {
        Assert.AreEqual(0, ADTLoader.GetVertexIndex(0, 0));
        Assert.AreEqual(9, ADTLoader.GetVertexIndex(1, 0));
        Assert.AreEqual(17, ADTLoader.GetVertexIndex(2, 0));
        Assert.AreEqual(26, ADTLoader.GetVertexIndex(3, 0));
        Assert.AreEqual(144, ADTLoader.GetVertexIndex(16, 8));
    }

    [TestMethod]
    public void AdtGpuVertexFormat_RetainsDynamicAttributesAndOmitsStaticLayout()
    {
        var cpuVertex = new ADTVertex
        {
            Position = new Vector3(11f, -7f, 23f),
            Normal = new Vector3(0.25f, 0.5f, 0.75f),
            TexCoord = new Vector2(0.25f, 0.75f),
            Color = new Vector4(0.5f, 0.6f, 0.7f, 1f)
        };

        var gpuVertex = ADTGpuVertex.FromCpu(cpuVertex);

        Assert.AreEqual(48, Marshal.SizeOf<ADTVertex>());
        Assert.AreEqual(32, Marshal.SizeOf<ADTGpuVertex>());
        Assert.AreEqual(cpuVertex.Position.Z, gpuVertex.Height);
        Assert.AreEqual(cpuVertex.Normal, gpuVertex.Normal);
        Assert.AreEqual(cpuVertex.Color, gpuVertex.Color);
    }

    [TestMethod]
    public void Wowlib_FileSystemPreparationProvidesNonEmptyOptionalPaths()
    {
        var projectDirectory = WowlibFileSystem.GetProjectDirectory("wow_classic_era");
        var listfilePath = WowlibFileSystem.GetListfilePath();

        Assert.IsTrue(Directory.Exists(projectDirectory));
        Assert.IsTrue(File.Exists(listfilePath));

        using var settings = new WoWLib.Filesystem.FileSystemSettings(
            "unused-client-path",
            new WoWLib.ClientVersion(1, 15, 9, 69109, WoWLib.ClientFlavor.ClassicEra),
            WoWLib.Locale.enUS,
            projectDirectory,
            listfilePath,
            new WoWLib.FileDataId(),
            "wow_classic_era");

        Assert.AreEqual(projectDirectory, settings.ProjectDirectory);
        Assert.AreEqual(listfilePath, settings.ListfileCsv);
    }

    [TestMethod]
    public void FlagsField_UsesEnumNamesAndPreservesUnknownBits()
    {
        var field = FlagsFieldViewModel.FromEnum<WoWLib.Formats.Common.DoodadDefFlags>("Flags", 0x421);

        CollectionAssert.AreEqual(
            new[] { "Biodome", "Liquid Known", "Unknown (0x400)" },
            field.ActiveFlags.ToArray());
        Assert.AreEqual("Raw value: 0x421", field.ToolTip);
    }

    [TestMethod]
    public void FlagsField_UsesWowlibNamesWithoutDroppingMeaningfulPrefixes()
    {
        var field = FlagsFieldViewModel.FromEnum<WoWLib.Formats.WMO.Group.Chunks.GroupFlags>("Flags", 0x4 | 0x80000000);

        CollectionAssert.AreEqual(
            new[] { "Has Vertex Colors", "Unknown (0x80000000)" },
            field.ActiveFlags.ToArray());
    }

    [TestMethod]
    public void SelectionInspector_TransformSpinValuesPublishAndClassicWmoScaleIsLocked()
    {
        var inspector = new SelectionInspectorViewModel([]);
        ObjectTransform? published = null;
        inspector.TransformChanged += (_, transform) => published = transform;
        inspector.SetBuildProfile(ClientBuildProfile.From(new ClientConfiguration
        {
            WowProduct = "wow_classic_era"
        }));
        inspector.Inspect(new EditorObjectSnapshot(
            EditorObjectId.New(),
            "Keep",
            "World model",
            ObjectTransform.Identity,
            new WorldModelObjectData(1, 2, 3, 4, 5, true)));

        Assert.IsFalse(inspector.IsScaleEditable);
        Assert.AreEqual(1d, inspector.UniformScale);

        inspector.PositionX = 42d;
        inspector.UniformScale = 3d;

        Assert.IsNotNull(published);
        Assert.AreEqual(42f, published.Position.X);
        Assert.AreEqual(1f, published.Scale.X);

        inspector.SetBuildProfile(ClientBuildProfile.From(new ClientConfiguration
        {
            WowProduct = "wow"
        }));
        Assert.IsTrue(inspector.IsScaleEditable);
    }

    [TestMethod]
    public void CommunityListfile_LoadsNamesAndReverseLookupFromConfiguredPath()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "WTEditor.Tests", Guid.NewGuid().ToString("N"));
        var listfilePath = Path.Combine(tempDirectory, "community-listfile.csv");
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllLines(listfilePath,
        [
            "123;World\\Model\\Keep.wmo",
            "456;Textures/Stone.blp",
            "invalid line"
        ]);

        try
        {
            WoWRenderLib.Listfile.Load(listfilePath);

            Assert.IsTrue(WoWRenderLib.Listfile.TryGetFilename(123, out var modelName));
            Assert.AreEqual("World\\Model\\Keep.wmo", modelName);
            Assert.IsTrue(WoWRenderLib.Listfile.TryGetFileDataID("world/model/keep.wmo", out var fileDataId));
            Assert.AreEqual(123u, fileDataId);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

}
