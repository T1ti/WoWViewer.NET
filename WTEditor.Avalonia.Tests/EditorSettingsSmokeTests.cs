using System.Numerics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Application;
using WTEditor.Application.Commands;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.ViewModels;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWFormatLib.Structs.M2;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WTEditor.Avalonia.Tests;

[TestClass]
[DoNotParallelize]
public sealed class EditorSettingsSmokeTests
{
    [TestMethod]
    public void PersistedSettings_RoundTripPreservesStartupConfiguration()
    {
        var expected = new EditorSettingsSnapshot
        {
            Client = new ClientConfiguration
            {
                WowDirectory = @"D:\Games\World of Warcraft",
                WowProduct = "wow",
                BuildConfig = "build-config-key",
                CdnConfig = "cdn-config-key"
            },
            Rendering = new RenderingConfiguration
            {
                AmbientColor = new Vector3(0.1f, 0.2f, 0.3f),
                DiffuseColor = new Vector3(0.9f, 0.8f, 0.7f),
                TerrainRenderDistance = 12_500f,
                ModelRenderDistance = 8_000f,
                TileLoadingDistance = 6,
                MovementSpeed = 225f,
                MouseSensitivity = 0.25f,
                RenderADT = false,
                RenderWMO = true,
                RenderM2 = false,
                ShowBoundingBoxes = true,
                ShowBoundingSpheres = true
            },
            KeyboardLayout = KeyboardLayoutMode.Azerty,
            Camera = new CameraState(
                new Vector3(10.5f, -20.25f, 30.75f),
                Vector3.Normalize(new Vector3(-0.25f, 0.5f, 0.75f))),
            Window = new WindowPlacement
            {
                HasBounds = true,
                State = "FullScreen",
                X = 42,
                Y = 84,
                Width = 1_920,
                Height = 1_080
            }
        };

        var json = JsonSerializer.Serialize(PersistedEditorSettings.From(expected));
        var restored = JsonSerializer.Deserialize<PersistedEditorSettings>(json)?.ToModel();

        Assert.IsNotNull(restored);
        Assert.AreEqual(expected.Client, restored.Client);
        Assert.AreEqual(expected.Rendering, restored.Rendering);
        Assert.AreEqual(expected.KeyboardLayout, restored.KeyboardLayout);
        Assert.AreEqual(expected.Camera, restored.Camera);
        Assert.AreEqual(expected.Window, restored.Window);
    }

    [TestMethod]
    public void EditorSession_RenderingUpdateDoesNotRaiseClientRestartEvent()
    {
        var store = new MemorySettingsStore(new EditorSettingsSnapshot());
        var session = new EditorSession(store);
        var clientChanges = 0;
        var renderingChanges = 0;
        session.ClientConfigurationChanged += (_, _) => clientChanges++;
        session.RenderingConfigurationChanged += (_, _) => renderingChanges++;

        session.Apply(
            session.Current with
            {
                Rendering = session.Current.Rendering with { MovementSpeed = 300f }
            },
            save: true);

        Assert.AreEqual(0, clientChanges);
        Assert.AreEqual(1, renderingChanges);
        Assert.AreEqual(1, store.SaveCount);
    }

    [TestMethod]
    public void ViewportSpeedUpdatesAuthoritativeEditorSession()
    {
        var session = new EditorSession(new MemorySettingsStore(new EditorSettingsSnapshot()));
        using var viewModel = new Editor3DViewModel(session);

        viewModel.MoveSpeed = 275f;
        viewModel.MouseSensitivity = 0.2f;

        Assert.AreEqual(275f, session.Current.Rendering.MovementSpeed);
        Assert.AreEqual(0.2f, session.Current.Rendering.MouseSensitivity);
    }

    [TestMethod]
    public void WorldViewportVisibilityIsLocalAndDoesNotModifyEditorSettings()
    {
        var initial = new EditorSettingsSnapshot
        {
            Rendering = new RenderingConfiguration
            {
                RenderADT = true,
                RenderWMO = true,
                RenderM2 = true
            }
        };
        var store = new MemorySettingsStore(initial);
        var session = new EditorSession(store);
        using var firstViewport = new Editor3DViewModel(session);
        using var secondViewport = new Editor3DViewModel(session);
        RenderingConfiguration? published = null;
        firstViewport.RenderingConfigurationChanged += (_, configuration) => published = configuration;

        firstViewport.RenderTerrain = false;
        firstViewport.RenderDoodads = false;

        Assert.IsNotNull(published);
        Assert.IsFalse(published.RenderADT);
        Assert.IsFalse(published.RenderM2);
        Assert.IsTrue(published.RenderWMO);
        Assert.IsTrue(secondViewport.RenderTerrain);
        Assert.IsTrue(secondViewport.RenderDoodads);
        Assert.IsTrue(session.Current.Rendering.RenderADT);
        Assert.IsTrue(session.Current.Rendering.RenderM2);
        Assert.AreEqual(0, store.SaveCount);
    }

    [TestMethod]
    public void JsonStore_PreservesNormalBoundsWhenSavingFullscreenState()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "WTEditor.Tests", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(tempDirectory, "settings.json");

        try
        {
            var store = new JsonEditorSettingsStore(settingsPath);
            var session = new EditorSession(store);
            session.UpdateWindow(new WindowPlacement
            {
                HasBounds = true,
                State = "Normal",
                X = 10,
                Y = 20,
                Width = 1_280,
                Height = 720
            }, save: true);

            session.UpdateWindow(session.Current.Window with { State = "FullScreen" }, save: true);

            var restored = store.Load();
            Assert.AreEqual(new WindowPlacement
            {
                HasBounds = true,
                State = "FullScreen",
                X = 10,
                Y = 20,
                Width = 1_280,
                Height = 720
            }, restored.Window);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Camera_SetDirectionRestoresFrontVector()
    {
        var expectedDirection = Vector3.Normalize(new Vector3(-0.25f, 0.5f, 0.75f));
        var camera = new Camera(Vector3.Zero, 0f, 0f, 1f);

        camera.SetDirection(expectedDirection);

        Assert.AreEqual(expectedDirection.X, camera.Front.X, 1e-5f);
        Assert.AreEqual(expectedDirection.Y, camera.Front.Y, 1e-5f);
        Assert.AreEqual(expectedDirection.Z, camera.Front.Z, 1e-5f);
    }

    [TestMethod]
    public void ScreenSpaceCulling_UsesProjectedSphereDiameter()
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(
            MathF.PI / 4f,
            16f / 9f,
            1f,
            100_000f);
        var diameter = ScreenSpaceCulling.EstimateProjectedDiameterPixels(
            Vector3.Zero,
            Vector3.UnitX,
            new Vector3(1_000f, 0f, 0f),
            1f,
            projection.M22,
            1_080);

        Assert.AreEqual(2.61f, diameter, 0.02f);
        Assert.IsFalse(ScreenSpaceCulling.IsBelowPixelThreshold(
            Vector3.Zero, Vector3.UnitX, new Vector3(1_000f, 0f, 0f), 1f,
            projection.M22, 1_080, 1f));
        Assert.IsTrue(ScreenSpaceCulling.IsBelowPixelThreshold(
            Vector3.Zero, Vector3.UnitX, new Vector3(10_000f, 0f, 0f), 1f,
            projection.M22, 1_080, 1f));
        Assert.IsFalse(ScreenSpaceCulling.IsBelowPixelThreshold(
            Vector3.Zero, Vector3.UnitX, new Vector3(10_000f, 0f, 0f), 1f,
            projection.M22, 1_080, 0f));

        Assert.AreEqual(
            diameter,
            ScreenSpaceCulling.EstimateProjectedDiameterPixelsNormalized(
                Vector3.Zero,
                Vector3.UnitX,
                new Vector3(1_000f, 0f, 0f),
                1f,
                projection.M22,
                1_080),
            0.0001f);
    }

    [TestMethod]
    public void Frustum_ClassifiesBoxesForHierarchicalTerrainCulling()
    {
        var frustum = new Frustum();
        frustum.ExtractFromMatrix(Matrix4x4.Identity);

        Assert.AreEqual(
            Frustum.BoxIntersection.Inside,
            frustum.ClassifyBox(new Vector3(-0.5f), new Vector3(0.5f)));
        Assert.AreEqual(
            Frustum.BoxIntersection.Intersecting,
            frustum.ClassifyBox(
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(1.5f, 0.5f, 0.5f)));
        Assert.AreEqual(
            Frustum.BoxIntersection.Outside,
            frustum.ClassifyBox(
                new Vector3(2f, -0.5f, -0.5f),
                new Vector3(3f, 0.5f, 0.5f)));
    }

    [TestMethod]
    public void TileSceneBounds_AggregatesChildrenAndRebuildsAfterExplicitInvalidation()
    {
        var tileBounds = new TileSceneBounds(new MapTile
        {
            wdtFileDataID = 1,
            tileX = 2,
            tileY = 3
        });
        var child = new TestBoundsContainer(new BoundingBox(
            new Vector3(2f, -2f, 0.5f),
            new Vector3(4f, 0.5f, 3f)));

        tileBounds.SetTerrain(42, new BoundingBox(Vector3.Zero, Vector3.One));
        tileBounds.AddObject(child);

        Assert.IsTrue(tileBounds.TryGetCombinedBounds(out var combined));
        Assert.AreEqual(new Vector3(0f, -2f, 0f), combined.Min);
        Assert.AreEqual(new Vector3(4f, 1f, 3f), combined.Max);
        Assert.IsFalse(tileBounds.IsDirty);

        child.SetBounds(new BoundingBox(
            new Vector3(-5f, -4f, -3f),
            new Vector3(-2f, -1f, -0.5f)));
        tileBounds.MarkDirty();

        Assert.IsTrue(tileBounds.IsDirty);
        Assert.IsTrue(tileBounds.TryGetCombinedBounds(out combined));
        Assert.AreEqual(new Vector3(-5f, -4f, -3f), combined.Min);
        Assert.AreEqual(Vector3.One, combined.Max);
    }

    [TestMethod]
    public void TileSceneBounds_DoesNotCullWithIncompleteChildBounds()
    {
        var tileBounds = new TileSceneBounds(default);
        tileBounds.SetTerrain(7, new BoundingBox(Vector3.Zero, Vector3.One));
        tileBounds.AddObject(new TestBoundsContainer(null));

        Assert.IsFalse(tileBounds.TryGetCombinedBounds(out _));
        Assert.IsTrue(tileBounds.IsDirty);
    }

    [TestMethod]
    public void M2RenderBounds_ContainEveryUploadedVertex()
    {
        var vertices = new[]
        {
            new Vertice { position = new Vector3(-2, -3, -4) },
            new Vertice { position = new Vector3(10, 5, 6) },
            new Vertice { position = new Vector3(1, 20, 2) }
        };

        var (box, radius) = WoWRenderLib.Loaders.M2Loader.CalculateRenderBounds(vertices);

        Assert.AreEqual(new Vector3(-2, -3, -4), box.Min);
        Assert.AreEqual(new Vector3(10, 20, 6), box.Max);
        foreach (var vertex in vertices)
        {
            Assert.IsTrue(
                Vector3.Distance(box.Center, vertex.position) <= radius + 0.0001f,
                $"Render vertex {vertex.position} escaped the calculated sphere.");
        }
    }

    [TestMethod]
    public void M2WorldSphere_AppliesAdtPlacementScaleRotationAndTranslation()
    {
        var local = new BoundingSphere(new Vector3(1, 2, 3), 5f);
        var transform = Matrix4x4.CreateScale(3f) *
                        Matrix4x4.CreateRotationZ(MathF.PI / 4f) *
                        Matrix4x4.CreateTranslation(100, -50, 20);

        var world = BoundingSphere.Transform(local, transform);

        Assert.AreEqual(15f, world.Radius, 0.0001f);
        Assert.IsTrue(world.Center.X > 90f);
        Assert.IsTrue(world.Center.Y < -35f);
    }

    [TestMethod]
    public void WmoEnabledGroupSignature_UsesMaskContentsRatherThanArrayIdentity()
    {
        var first = new[] { true, false, true, true, false, false, false, false, true };
        var sameContents = first.ToArray();
        var different = first.ToArray();
        different[1] = true;

        Assert.AreEqual(
            WMOContainer.CreateEnabledGroupSignature(first),
            WMOContainer.CreateEnabledGroupSignature(sameContents));
        Assert.AreNotEqual(
            WMOContainer.CreateEnabledGroupSignature(first),
            WMOContainer.CreateEnabledGroupSignature(different));
    }

    [TestMethod]
    public void TransformCommand_UpdatesDocumentRendererAndUndoHistory()
    {
        var document = new EditorDocument("Test map");
        var objectId = EditorObjectId.New();
        document.AddObject(new EditorObjectSnapshot(objectId, "Crate", "M2", ObjectTransform.Identity));
        document.MarkSaved();

        var expected = ObjectTransform.Identity with { Position = new Vector3(10, 20, 30) };
        var sink = new RecordingSceneSink();
        var history = new UndoService();

        history.Execute(new TransformObjectCommand(
            document,
            objectId,
            ObjectTransform.Identity,
            expected,
            sink));

        Assert.IsTrue(document.IsDirty);
        Assert.IsTrue(history.CanUndo);
        Assert.AreEqual(expected, GetObject(document, objectId).Transform);

        history.Undo();
        Assert.AreEqual(ObjectTransform.Identity, GetObject(document, objectId).Transform);
        Assert.IsTrue(history.CanRedo);

        history.Redo();
        Assert.AreEqual(expected, GetObject(document, objectId).Transform);
        Assert.AreEqual(3, sink.Updates.Count);
    }

    [TestMethod]
    public void UndoTransaction_GroupsDragUpdatesIntoOneHistoryEntry()
    {
        var value = 0;
        var history = new UndoService();

        using (var transaction = history.BeginTransaction("Drag object"))
        {
            history.Execute(new DelegateCommand("Step 1", () => value++, () => value--));
            history.Execute(new DelegateCommand("Step 2", () => value++, () => value--));
            transaction.Commit();
        }

        Assert.AreEqual(2, value);
        Assert.AreEqual("Drag object", history.UndoDescription);
        history.Undo();
        Assert.AreEqual(0, value);
        history.Redo();
        Assert.AreEqual(2, value);
    }

    [TestMethod]
    public void SelectionService_DeduplicatesAndTracksPrimaryObject()
    {
        var first = EditorObjectId.New();
        var second = EditorObjectId.New();
        var selection = new SelectionService();

        selection.Set([first, second, first], second);

        Assert.AreEqual(2, selection.Current.ObjectIds.Count);
        Assert.AreEqual(second, selection.Current.Primary);
        selection.Clear();
        Assert.AreEqual(0, selection.Current.ObjectIds.Count);
        Assert.IsNull(selection.Current.Primary);
    }

    [TestMethod]
    public void PerformanceAnalyzer_ClassifiesCpuGpuAndBalancedFrames()
    {
        Assert.AreEqual(
            PerformanceBottleneck.Cpu,
            PerformanceAnalyzer.Classify(cpuMilliseconds: 12, gpuMilliseconds: 5));
        Assert.AreEqual(
            PerformanceBottleneck.Gpu,
            PerformanceAnalyzer.Classify(cpuMilliseconds: 4, gpuMilliseconds: 11));
        Assert.AreEqual(
            PerformanceBottleneck.Balanced,
            PerformanceAnalyzer.Classify(cpuMilliseconds: 10, gpuMilliseconds: 10.5));
        Assert.AreEqual(
            PerformanceBottleneck.Unknown,
            PerformanceAnalyzer.Classify(cpuMilliseconds: 10, gpuMilliseconds: null));
    }

    [TestMethod]
    public void PerformanceAnalyzer_UsesRecentComparableSamplesForStableDiagnosis()
    {
        var samples = Enumerable.Range(1, 35)
            .Select(index => new FrameProfileSnapshot(
                index,
                DateTimeOffset.UtcNow,
                16.6,
                index <= 5 ? 50 : 4,
                index <= 5 ? 2 : 12,
                Array.Empty<FrameTimingStep>(),
                0,
                0,
                0))
            .ToArray();

        Assert.AreEqual(PerformanceBottleneck.Gpu, PerformanceAnalyzer.ClassifyRecent(samples));
    }

    [TestMethod]
    public void PerformanceAnalyzer_DetectsCpuSubmissionStarvation()
    {
        var samples = Enumerable.Range(1, 30)
            .Select(index => new FrameProfileSnapshot(
                index,
                DateTimeOffset.UtcNow,
                80,
                75,
                68,
                Array.Empty<FrameTimingStep>(),
                9_500,
                0,
                0)
            {
                RenderWorkload = new RenderWorkloadMetrics(
                    [new RenderPassMetrics("Terrain", 3, 67, null, 9_500, 9_500, "draws")],
                    0,
                    0,
                    0,
                    0)
            })
            .ToArray();

        Assert.IsTrue(PerformanceAnalyzer.IsLikelyCpuSubmissionStarved(samples));
        Assert.IsFalse(PerformanceAnalyzer.IsLikelyCpuSubmissionStarved(
            samples.Select(sample => sample with { CpuFrameMilliseconds = 5, GpuFrameMilliseconds = 4 })));
    }

    [TestMethod]
    public void PerformanceWorkload_ReportsIndexedTrianglesAndExplicitCullReasons()
    {
        var snapshot = new FrameProfileSnapshot(
            1, DateTimeOffset.UtcNow, 16, 5, 4,
            Array.Empty<FrameTimingStep>(), 2, 30, 0);
        var pass = new RenderPassMetrics("M2", 1, 1, 1, 2, 4, "instances", 21);
        var culling = new CullingMetrics(
            10, 100,
            5, 20,
            8, 50,
            SizeCulledWorldModels: 3,
            SizeCulledDoodads: 7);

        Assert.AreEqual(10, snapshot.SubmittedTriangles);
        Assert.AreEqual(7, pass.SubmittedTriangles);
        Assert.AreEqual(90, culling.CulledTerrainChunks);
        Assert.AreEqual(12, culling.CulledWorldModels);
        Assert.AreEqual(35, culling.CulledDoodads);
    }

    [TestMethod]
    public void PerformanceCaptureAnalyzer_ComputesInterpolatedPercentiles()
    {
        var distribution = PerformanceCaptureAnalyzer.Summarize(
            Enumerable.Range(1, 100).Select(value => (double)value));

        Assert.AreEqual(100, distribution.SampleCount);
        Assert.AreEqual(50.5d, distribution.Mean, 0.0001d);
        Assert.AreEqual(50.5d, distribution.Median, 0.0001d);
        Assert.AreEqual(95.05d, distribution.P95, 0.0001d);
        Assert.AreEqual(99.01d, distribution.P99, 0.0001d);
        Assert.AreEqual(100d, distribution.Maximum, 0.0001d);
    }

    [TestMethod]
    public void PerformanceCaptureAnalyzer_PreservesRawSamplesAndGroupsSteps()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            new FrameProfileSnapshot(
                1, startedAt, 10, 4, 6,
                [new FrameTimingStep("Terrain drawing (GPU)", 5, FrameTimingDomain.Gpu)],
                10, 100, 0) { EngineFrameMilliseconds = 4.5 },
            new FrameProfileSnapshot(
                2, startedAt.AddMilliseconds(10), 10, 5, 8,
                [new FrameTimingStep("Terrain drawing (GPU)", 7, FrameTimingDomain.Gpu)],
                12, 120, 0) { EngineFrameMilliseconds = 5.5 }
        };
        var context = new PerformanceCaptureContext(
            "terrain-only", 1920, 1080, Vector3.Zero, Vector3.UnitX,
            true, false, false, true, 20_000, 20_000, 4);

        var capture = PerformanceCaptureAnalyzer.Create(
            startedAt,
            startedAt.AddSeconds(10),
            context,
            samples);

        Assert.AreEqual(2, capture.Samples.Count);
        Assert.AreEqual(1, capture.Summary.Steps.Count);
        Assert.AreEqual(6d, capture.Summary.Steps[0].DurationMilliseconds.Median, 0.0001d);
        Assert.AreEqual(7d, capture.Summary.GpuFrameMilliseconds.Median, 0.0001d);
    }

    [TestMethod]
    public void AutomatedBenchmarkOptions_ReadAndClampEnvironmentValues()
    {
        var values = new Dictionary<string, string>
        {
            ["WTEDITOR_BENCHMARK"] = "true",
            ["WTEDITOR_BENCHMARK_MINIMUM_LOAD_SECONDS"] = "2.5",
            ["WTEDITOR_BENCHMARK_STABLE_FRAMES"] = "0",
            ["WTEDITOR_BENCHMARK_WARMUP_SECONDS"] = "3",
            ["WTEDITOR_BENCHMARK_CAPTURE_SECONDS"] = "12.5",
            ["WTEDITOR_BENCHMARK_TIMEOUT_SECONDS"] = "900"
        };

        var options = AutomatedBenchmarkOptions.FromEnvironment(name =>
            values.TryGetValue(name, out var value) ? value : null);

        Assert.IsTrue(options.Enabled);
        Assert.AreEqual(2.5d, options.MinimumLoadDuration.TotalSeconds, 0.001d);
        Assert.AreEqual(1, options.StableFrameCount);
        Assert.AreEqual(3d, options.WarmupDuration.TotalSeconds, 0.001d);
        Assert.AreEqual(12.5d, options.CaptureDuration.TotalSeconds, 0.001d);
        Assert.AreEqual(900d, options.Timeout.TotalSeconds, 0.001d);
    }

    [TestMethod]
    public void AutomatedBenchmarkCoordinator_WaitsForIdleStableWorldWorkload()
    {
        var options = new AutomatedBenchmarkOptions(
            true,
            TimeSpan.FromSeconds(1),
            StableFrameCount: 2,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30));
        var coordinator = new AutomatedBenchmarkCoordinator(options);
        var startedAt = DateTimeOffset.UtcNow;

        Assert.AreEqual(
            AutomatedBenchmarkAction.None,
            coordinator.Observe(CreateBenchmarkSnapshot(startedAt, pendingAssets: 1)));
        Assert.AreEqual(
            AutomatedBenchmarkAction.None,
            coordinator.Observe(CreateBenchmarkSnapshot(startedAt.AddSeconds(1), drawCalls: 100)));
        Assert.AreEqual(1, coordinator.StableFrames);

        // A changing workload restarts the consecutive stable-frame window.
        Assert.AreEqual(
            AutomatedBenchmarkAction.None,
            coordinator.Observe(CreateBenchmarkSnapshot(startedAt.AddSeconds(1.1), drawCalls: 101)));
        Assert.AreEqual(1, coordinator.StableFrames);
        Assert.AreEqual(
            AutomatedBenchmarkAction.StartCapture,
            coordinator.Observe(CreateBenchmarkSnapshot(startedAt.AddSeconds(1.2), drawCalls: 101)));
    }

    [TestMethod]
    public void AutomatedBenchmarkCoordinator_TimesOutWithoutWorldWorkload()
    {
        var options = new AutomatedBenchmarkOptions(
            true,
            TimeSpan.Zero,
            StableFrameCount: 1,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(10));
        var coordinator = new AutomatedBenchmarkCoordinator(options);
        var startedAt = DateTimeOffset.UtcNow;

        Assert.AreEqual(
            AutomatedBenchmarkAction.None,
            coordinator.Observe(CreateBenchmarkSnapshot(startedAt, drawCalls: 0, hasWorld: false)));
        Assert.AreEqual(
            AutomatedBenchmarkAction.Timeout,
            coordinator.Observe(CreateBenchmarkSnapshot(startedAt.AddSeconds(10), drawCalls: 0, hasWorld: false)));
    }

    [TestMethod]
    public void TerrainBatching_MergesContiguousChunksWithSharedBoundResources()
    {
        var common = CreateTerrainBatch(material: 10, scale: 1f);
        var batches = new[]
        {
            common,
            CreateTerrainBatch(material: 10, scale: 1f),
            CreateTerrainBatch(material: 20, scale: 1f),
            CreateTerrainBatch(material: 10, scale: 1f)
        };

        Assert.AreEqual(
            2,
            TerrainBatching.CountCompatibleContiguousChunks(
                0,
                new[] { 0, 1, 2, 3 },
                new[] { true, true, true, true },
                batches));
        Assert.AreEqual(
            1,
            TerrainBatching.CountCompatibleContiguousChunks(
                0,
                new[] { 0, 1 },
                new[] { true, false },
                batches));
        Assert.AreEqual(
            1,
            TerrainBatching.CountCompatibleContiguousChunks(
                0,
                new[] { 0, 2 },
                new[] { true, true },
                batches));
        Assert.IsTrue(TerrainBatching.AreCompatible(
            CreateTerrainBatch(material: 10, scale: 1f),
            CreateTerrainBatch(material: 10, scale: 2f)));
    }

    [TestMethod]
    public void TerrainShaderVariants_SelectSmallestFixedLayerBucket()
    {
        Assert.AreEqual(1, TerrainBatching.GetShaderLayerCount(1));
        Assert.AreEqual(2, TerrainBatching.GetShaderLayerCount(2));
        Assert.AreEqual(4, TerrainBatching.GetShaderLayerCount(3));
        Assert.AreEqual(4, TerrainBatching.GetShaderLayerCount(4));
        Assert.AreEqual(8, TerrainBatching.GetShaderLayerCount(5));
    }

    private static ADTRenderBatch CreateTerrainBatch(int material, float scale) => new()
    {
        layerCount = 1,
        materialFDIDs = [material, -1, -1, -1, -1, -1, -1, -1],
        heightMaterialFDIDs = [material, -1, -1, -1, -1, -1, -1, -1],
        scales = [scale, 1, 1, 1, 1, 1, 1, 1],
        heightScales = [1, 1, 1, 1, 1, 1, 1, 1],
        heightOffsets = [0, 0, 0, 0, 0, 0, 0, 0]
    };

    private static FrameProfileSnapshot CreateBenchmarkSnapshot(
        DateTimeOffset capturedAt,
        int pendingAssets = 0,
        int drawCalls = 100,
        bool hasWorld = true) =>
        new(
            1,
            capturedAt,
            16,
            5,
            4,
            Array.Empty<FrameTimingStep>(),
            drawCalls,
            1_000,
            pendingAssets)
        {
            Culling = new CullingMetrics(
                hasWorld ? 10 : 0,
                hasWorld ? 10 : 0,
                0,
                0,
                0,
                0)
        };

    [TestMethod]
    public void ContainerTransformChange_InvalidatesMatrixAndWorldBounds()
    {
        var container = new Container3D(default, 1, 1)
        {
            Position = new Vector3(10, 20, 30),
            Rotation = Vector3.Zero,
            Scale = 1
        };
        var firstMatrix = container.GetModelMatrix();
        container.CachedBoundingSphere = new BoundingSphere(Vector3.Zero, 1);
        container.CachedBoundingBox = new BoundingBox(Vector3.Zero, Vector3.One);

        container.Position = new Vector3(11, 20, 30);

        Assert.IsNull(container.ModelMatrix);
        Assert.IsNull(container.CachedBoundingSphere);
        Assert.IsNull(container.CachedBoundingBox);
        Assert.AreNotEqual(firstMatrix, container.GetModelMatrix());
    }

    private static EditorObjectSnapshot GetObject(EditorDocument document, EditorObjectId id)
    {
        Assert.IsTrue(document.TryGetObject(id, out var snapshot));
        return snapshot;
    }

    private sealed class RecordingSceneSink : IEditorSceneSink
    {
        public List<(Guid DocumentId, EditorObjectId ObjectId, ObjectTransform Transform)> Updates { get; } = [];

        public void UpdateObjectTransform(Guid documentId, EditorObjectId objectId, ObjectTransform transform) =>
            Updates.Add((documentId, objectId, transform));
    }

    private sealed class TestBoundsContainer(BoundingBox? bounds) : Container3D(default, 1, 1)
    {
        private BoundingBox? _bounds = bounds;

        public override BoundingBox? GetBoundingBox() => _bounds;

        public void SetBounds(BoundingBox? value)
        {
            _bounds = value;
            InvalidateTransform();
        }
    }

    private sealed class DelegateCommand(
        string description,
        Action execute,
        Action undo) : IEditorCommand
    {
        public string Description => description;
        public void Execute() => execute();
        public void Undo() => undo();
    }

    private sealed class MemorySettingsStore(EditorSettingsSnapshot initial) : IEditorSettingsStore
    {
        private EditorSettingsSnapshot _current = initial;
        public int SaveCount { get; private set; }

        public EditorSettingsSnapshot Load() => _current;

        public void Save(EditorSettingsSnapshot settings)
        {
            _current = settings;
            SaveCount++;
        }
    }
}
