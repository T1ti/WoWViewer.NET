using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Application;
using WTEditor.Application.Commands;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.Controls;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Views;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Loaders;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;
using WoWRenderLib.Services;

namespace WTEditor.Avalonia.Tests;

[TestClass]
[DoNotParallelize]
public sealed class EditorSettingsSmokeTests
{
    [AssemblyInitialize]
    public static void InitializeAvalonia(TestContext _)
    {
        AppBuilder.Configure<global::Avalonia.Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
    }

    [TestMethod]
    public void MainView_XamlCanBePopulatedAtRuntime()
    {
        var view = new MainView();

        Assert.IsNotNull(view);
        Assert.AreEqual(7d, view.FindControl<Thumb>("InspectorLeftResizeHandle")!.Width);
        Assert.AreEqual(7d, view.FindControl<Thumb>("InspectorRightResizeHandle")!.Width);
        Assert.AreEqual(7d, view.FindControl<Thumb>("InspectorBottomResizeHandle")!.Height);
    }

    [TestMethod]
    public void MainWindow_ProvidesThreeNonClosableWorkspaceTabsInRequestedOrder()
    {
        var window = new MainWindow();
        var tabs = window.FindControl<TabControl>("WorkspaceTabs");

        Assert.IsNotNull(tabs);
        var items = tabs.Items.Cast<TabItem>().ToArray();
        CollectionAssert.AreEqual(
            new[] { "World Selection", "Main Editor", "Data Tools" },
            items.Select(tab => tab.Header).Cast<string>().ToArray());
    }

    [TestMethod]
    public void ViewportFrameRatePolicy_AppliesCapExactlyAndSuspendsAtOneFps()
    {
        Assert.IsNull(
            ViewportFrameRatePolicy.GetFrameIntervalSeconds(
                100,
                ViewportRenderActivity.Foreground,
                isForegroundFrameRateLimitEnabled: false));
        Assert.AreEqual(0.01d,
            ViewportFrameRatePolicy.GetFrameIntervalSeconds(
                100,
                ViewportRenderActivity.Foreground,
                isForegroundFrameRateLimitEnabled: true)!.Value,
            0.0001d);
        Assert.IsNull(
            ViewportFrameRatePolicy.GetFrameIntervalSeconds(
                100,
                ViewportRenderActivity.Background,
                isForegroundFrameRateLimitEnabled: false));
        Assert.AreEqual(0.01d,
            ViewportFrameRatePolicy.GetFrameIntervalSeconds(
                100,
                ViewportRenderActivity.Background,
                isForegroundFrameRateLimitEnabled: true)!.Value,
            0.0001d);
        Assert.AreEqual(1d,
            ViewportFrameRatePolicy.GetFrameIntervalSeconds(
                100,
                ViewportRenderActivity.Suspended,
                isForegroundFrameRateLimitEnabled: false)!.Value,
            0.0001d);
    }

    [TestMethod]
    public void ViewportFrameClock_DoesNotConsumeDeadlineUntilFrameIsPresented()
    {
        var clock = new ViewportFrameClock(dueToleranceSeconds: 0);
        const double interval = 0.01d;

        Assert.IsTrue(clock.IsFrameDue(1d, interval));
        clock.MarkFramePresented(1d, interval);

        // A due callback with no free presentation buffer does not call
        // MarkFramePresented. The next callback must therefore remain due.
        Assert.IsTrue(clock.IsFrameDue(1.01d, interval));
        Assert.IsTrue(clock.IsFrameDue(1.015d, interval));

        clock.MarkFramePresented(1.015d, interval);
        Assert.IsFalse(clock.IsFrameDue(1.019d, interval));
        Assert.IsTrue(clock.IsFrameDue(1.02d, interval));
    }

    [TestMethod]
    public void ViewportFrameClock_DisablingCapClearsExistingDeadline()
    {
        var clock = new ViewportFrameClock(dueToleranceSeconds: 0);

        clock.MarkFramePresented(1d, frameIntervalSeconds: 0.01d);

        Assert.IsTrue(clock.IsFrameDue(1.001d, frameIntervalSeconds: null));
        Assert.IsTrue(clock.IsFrameDue(1.001d, frameIntervalSeconds: 0.01d));
    }

    [TestMethod]
    public void StreamingBudgetUsesRemainingFrameTimeAndFpsCap()
    {
        Assert.AreEqual(
            9d,
            StreamingFrameBudget.CalculateMilliseconds(
                frameIntervalSeconds: 0.01d,
                elapsedBeforeRenderMilliseconds: 0d,
                estimatedRenderMilliseconds: 0d),
            0.001d);
        Assert.AreEqual(
            3d,
            StreamingFrameBudget.CalculateMilliseconds(
                frameIntervalSeconds: 0.01d,
                elapsedBeforeRenderMilliseconds: 1d,
                estimatedRenderMilliseconds: 5d),
            0.001d);
        Assert.AreEqual(
            0d,
            StreamingFrameBudget.CalculateMilliseconds(
                frameIntervalSeconds: 0.01d,
                elapsedBeforeRenderMilliseconds: 3d,
                estimatedRenderMilliseconds: 8d),
            0.001d);
        Assert.AreEqual(
            10d,
            StreamingFrameBudget.CalculateMilliseconds(
                frameIntervalSeconds: 1d / 60d,
                elapsedBeforeRenderMilliseconds: 0d,
                estimatedRenderMilliseconds: 0d),
            0.01d);
        Assert.IsTrue(double.IsFinite(StreamingFrameBudget.CalculateMilliseconds(
            frameIntervalSeconds: double.NaN,
            elapsedBeforeRenderMilliseconds: double.NaN,
            estimatedRenderMilliseconds: double.PositiveInfinity)));
    }

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
                IsForegroundFrameRateLimitEnabled = true,
                IsForegroundFrameRateLimitInitialized = true,
                ViewportFrameRateLimit = 144,
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
                EnableWmoPortalCulling = true,
                ShowBoundingBoxes = true,
                ShowBoundingSpheres = true,
                ShowTerrainGrid = true,
                ShowTerrainWireframe = true
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
    public void EditorSession_ProjectReloadRaisesClientRestartEventWhenConfigurationIsUnchanged()
    {
        var session = new EditorSession(new MemorySettingsStore(new EditorSettingsSnapshot()));
        var clientChanges = 0;
        session.ClientConfigurationChanged += (_, _) => clientChanges++;

        session.Reload(session.Current);

        Assert.AreEqual(1, clientChanges);
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
                RenderM2 = true,
                EnableWmoPortalCulling = false,
                ShowTerrainGrid = false,
                ShowTerrainWireframe = false
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
        firstViewport.WmoPortalCullingEnabled = true;
        firstViewport.ShowTerrainGrid = true;
        firstViewport.ShowTerrainWireframe = true;

        Assert.IsNotNull(published);
        Assert.IsFalse(published.RenderADT);
        Assert.IsFalse(published.RenderM2);
        Assert.IsTrue(published.RenderWMO);
        Assert.IsTrue(published.EnableWmoPortalCulling);
        Assert.IsTrue(published.ShowTerrainGrid);
        Assert.IsTrue(published.ShowTerrainWireframe);
        Assert.IsTrue(secondViewport.RenderTerrain);
        Assert.IsTrue(secondViewport.RenderDoodads);
        Assert.IsFalse(secondViewport.WmoPortalCullingEnabled);
        Assert.IsFalse(secondViewport.ShowTerrainGrid);
        Assert.IsFalse(secondViewport.ShowTerrainWireframe);
        Assert.IsTrue(session.Current.Rendering.RenderADT);
        Assert.IsTrue(session.Current.Rendering.RenderM2);
        Assert.IsFalse(session.Current.Rendering.EnableWmoPortalCulling);
        Assert.IsFalse(session.Current.Rendering.ShowTerrainGrid);
        Assert.IsFalse(session.Current.Rendering.ShowTerrainWireframe);
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
            new Vector3(-2, -3, -4),
            new Vector3(10, 5, 6),
            new Vector3(1, 20, 2)
        };

        var (box, radius) = WoWRenderLib.Loaders.M2Loader.CalculateRenderBounds(vertices);

        Assert.AreEqual(new Vector3(-2, -3, -4), box.Min);
        Assert.AreEqual(new Vector3(10, 20, 6), box.Max);
        foreach (var vertex in vertices)
        {
            Assert.IsTrue(
                Vector3.Distance(box.Center, vertex) <= radius + 0.0001f,
                $"Render vertex {vertex} escaped the calculated sphere.");
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
    public void UndoService_RecordExecutedDoesNotApplyLiveActionTwice()
    {
        var value = 1;
        var history = new UndoService();
        var command = new DelegateCommand("Live edit", () => value = 2, () => value = 1);

        value = 2;
        history.RecordExecuted(command);

        Assert.AreEqual(2, value);
        history.Undo();
        Assert.AreEqual(1, value);
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
    public void SelectionInspector_ComposesTransformAndReusableModelInformation()
    {
        var inspector = new SelectionInspectorViewModel([]);
        var selected = new EditorObjectSnapshot(
            EditorObjectId.New(),
            "Training Dummy",
            "M2 model",
            new ObjectTransform(
                new Vector3(10, 20, 30),
                Quaternion.Identity,
                new Vector3(2)),
            new M2ObjectData(
                1234, 5678, 6, false,
                "world/model/dummy.m2",
                [new AssetReference(99, "textures/dummy.blp")],
                42,
                "world/maps/test/test_1_2.adt",
                new ModelAdvancedData(3, 100, 50, 2, 1, 8, 4),
                new MapPlacementData(MapPlacementKind.Mddf, 42, 0x21),
                [new ModelMaterialData(0, 2, 5, "", "Diffuse_T1", "Combiners_Mod",
                    [new AssetReference(99, "textures/dummy.blp")],
                    TextureSlots: [new ModelTextureData(1, new AssetReference(99, "textures/dummy.blp"), 3)])],
                [new ModelBatchData(0, null, 0, 12, 30, 2, 5, "", "Diffuse_T1", "Combiners_Mod", [new AssetReference(99, "textures/dummy.blp")])],
                [new ModelGeosetData(0, 101, "Hair", 0, 0, 100, 0, 30, true)],
                [new ModelTextureData(0, new AssetReference(99, "textures/dummy.blp"), 3)]));

        inspector.Inspect(selected);

        Assert.IsTrue(inspector.HasSelection);
        Assert.AreEqual("Training Dummy", inspector.SelectionName);
        Assert.AreEqual(10d, inspector.PositionX);
        Assert.IsTrue(inspector.ModelInformation.HasModel);
        Assert.AreEqual("world/model/dummy.m2", inspector.ModelInformation.ModelFile!.Path);
        Assert.AreEqual("File data ID: 1234", inspector.ModelInformation.ModelFile.ToolTip);
        Assert.AreEqual(0, inspector.ModelInformation.Properties.Count);
        Assert.AreEqual("Geosets (1)", inspector.ModelInformation.GeosetsHeader);
        Assert.AreEqual("Textures (1)", inspector.ModelInformation.TexturesHeader);
        Assert.AreEqual("Materials (1)", inspector.ModelInformation.MaterialsHeader);
        Assert.AreEqual("Render batches (1)", inspector.ModelInformation.BatchesHeader);
        Assert.AreEqual("Particle emitters", inspector.ModelInformation.AdvancedProperties[4].Label);
        Assert.AreEqual("Geoset 101 · Hair", inspector.ModelInformation.SelectedGeoset!.Name);
        Assert.AreEqual("Alpha blend (2)", inspector.ModelInformation.SelectedMaterial!.Properties[0].Value);
        Assert.AreEqual("Unlit, Two Sided", inspector.ModelInformation.SelectedMaterial.Flags!.DisplayText);
        Assert.AreEqual(ModelMaterialType.M2, inspector.ModelInformation.SelectedMaterial.MaterialType);
        Assert.AreEqual("textures/dummy.blp", inspector.ModelInformation.SelectedMaterial.Name);
        Assert.AreSame(inspector.ModelInformation.SelectedMaterial, inspector.ModelInformation.SelectedBatch!.Material);
        Assert.AreEqual("Wrap X, Wrap Y", inspector.ModelInformation.Textures[0].Flags!.DisplayText);
        Assert.AreEqual("Wrap X, Wrap Y", inspector.ModelInformation.SelectedMaterial.TextureSlots[0].Flags!.DisplayText);
        Assert.AreEqual("textures/dummy.blp", inspector.ModelInformation.SelectedMaterial!.Textures[0].Path);
        Assert.IsTrue(inspector.PlacementInformation.HasPlacement);
        Assert.AreEqual("42", inspector.PlacementInformation.Properties[0].Value);
        CollectionAssert.AreEqual(
            new[] { "Biodome", "Liquid Known" },
            inspector.PlacementInformation.Flags!.ActiveFlags.ToArray());

        inspector.Inspect(null);
        Assert.IsFalse(inspector.HasSelection);
        Assert.IsFalse(inspector.ModelInformation.HasModel);
        Assert.AreEqual(0, inspector.Sections.Count);
    }

    [TestMethod]
    public void ModelInformation_WorldModelGroupsAreSelectable()
    {
        var model = new ModelInformationViewModel();
        var groups = new[]
        {
            new WorldModelGroupData(0, "Exterior", "Outside", 10, 2, 600, 300, 4, 0x20),
            new WorldModelGroupData(1, "Interior", "Hall", 11, 3, 900, 450, 8, 0x40)
        };

        model.SetModel(new EditorObjectSnapshot(
            EditorObjectId.New(), "Keep", "World model", ObjectTransform.Identity,
            new WorldModelObjectData(
                1, 2, 2, 1, 5, true, "world/wmo/keep.wmo", [], 77,
                "world/maps/test/test_1_2.adt", groups,
                new MapPlacementData(MapPlacementKind.Modf, 77, 0x1, 1, 2),
                new WorldModelRootData(0xFF102030, 0x11),
                [new ModelMaterialData(0, 4, 5, "Diffuse", "MapObjDiffuse_T1", "MapObjDiffuse", [new AssetReference(9, "stone.blp")])],
                [new ModelBatchData(0, 1, 0, 3, 60, 4, 0, "Diffuse", "MapObjDiffuse_T1", "MapObjDiffuse", [new AssetReference(9, "stone.blp")])],
                ["Default", "Winter"])));

        Assert.IsTrue(model.HasWorldModelGroups);
        Assert.AreEqual("WMO groups (2)", model.WorldModelGroupsHeader);
        Assert.AreEqual("Textures (0)", model.TexturesHeader);
        Assert.AreEqual("Materials (1)", model.MaterialsHeader);
        Assert.AreEqual("Render batches (1)", model.BatchesHeader);
        Assert.AreEqual("Exterior", model.SelectedGroup!.Name);
        model.SelectedGroup = groups[1];
        Assert.AreEqual("Hall", model.SelectedGroupProperties[0].Value);
        Assert.AreEqual("MOGI name", model.SelectedGroupProperties[0].Label);
        Assert.AreEqual("3", model.SelectedGroupProperties[2].Value);
        Assert.AreEqual("Exterior Lit", model.SelectedGroupFlags!.DisplayText);
        Assert.AreEqual(0xFF102030u, model.RootAmbientColor);
        Assert.IsFalse(model.Properties.Any(property => property.Label == "Asset state"));
        Assert.IsNotNull(model.RootFlags);
        Assert.AreEqual("Additive (4)", model.SelectedMaterial!.Properties[0].Value);
        Assert.AreEqual(ModelMaterialType.Wmo, model.SelectedMaterial.MaterialType);
        Assert.IsTrue(model.HasBatches);
        Assert.AreEqual("Batch 0 · Group 1", model.SelectedBatch!.Name);
        Assert.AreSame(model.SelectedMaterial, model.SelectedBatch.Material);
        Assert.AreEqual(5, model.SelectedBatch.Properties.Count);
        Assert.AreEqual("stone.blp", model.SelectedMaterial.TextureSlots![0].File.Path);
    }

    [TestMethod]
    public void PlacementInformation_UsesModsNamesAndPublishesModfSetSelection()
    {
        var inspector = new SelectionInspectorViewModel([]);
        WmoPlacementSelection? changed = null;
        inspector.WmoPlacementChanged += (_, selection) => changed = selection;
        inspector.Inspect(new EditorObjectSnapshot(
            EditorObjectId.New(), "Keep", "World model", ObjectTransform.Identity,
            new WorldModelObjectData(
                1, 2, 1, 2, 4, true, "keep.wmo", [], 77, "map.adt", [],
                new MapPlacementData(MapPlacementKind.Modf, 77, 0, 1, 2),
                new WorldModelRootData(0xFF102030, 0), null, null,
                ["Set_$DefaultGlobal", "Set_Winter"])));

        var placement = inspector.PlacementInformation;
        Assert.IsTrue(placement.HasWorldModelPlacement);
        Assert.AreEqual("(1) Set_Winter", placement.SelectedDoodadSet!.DisplayName);
        Assert.AreEqual("2", placement.Properties.Single(property => property.Label == "Name set").Value);
        Assert.IsFalse(placement.Properties.Any(property => property.Label == "Active doodad sets"));

        placement.SelectedDoodadSet = placement.DoodadSetOptions[0];
        Assert.IsNotNull(changed);
        Assert.AreEqual((ushort)0, changed.DoodadSet);
        Assert.AreEqual((ushort)2, changed.NameSet);
    }

    [TestMethod]
    public void LoadedWmoProjection_PopulatesModsComboGroupsAndMaterialData()
    {
        var rendererModel = new WoWRenderLib.DX11.Structs.WorldModel
        {
            rootWMOFileDataID = 123,
            ambientColor = 0xFF102030,
            flags = 1,
            doodadSets = ["Set_$DefaultGlobal", "Set_Day"],
            doodads = [new WMODoodad { filedataid = 456, doodadSet = 1 }],
            preppedMats =
            [
                new PreppedWMOMaterial
                {
                    Shader = 0,
                    BlendMode = 0,
                    Color1 = 0xFF112233,
                    Color2 = 0xFF445566,
                    GroundType = 7,
                    TexFileDataID0 = 1001,
                    TexFileDataID1 = 1002,
                    TexFileDataID2 = 1003,
                    VertexShader = WoWRenderLib.Renderer.ShaderEnums.WMOVertexShader.MapObjDiffuse_T1,
                    PixelShader = WoWRenderLib.Renderer.ShaderEnums.WMOPixelShader.MapObjDiffuse
                }
            ],
            wmoRenderBatches =
            [
                new WMORenderBatch
                {
                    groupID = 0,
                    materialIndex = 0,
                    shader = 0,
                    numFaces = 3,
                    materialFDIDs = []
                }
            ],
            groupBatches =
            [
                new WorldModelGroupBatches
                {
                    groupName = "Exterior",
                    mogiGroupName = "Outside",
                    groupID = 7,
                    verticeCount = 3,
                    doodadReferences = [0],
                    portalLinks = []
                }
            ]
        };

        var data = Dx11View.ProjectLoadedWorldModelObjectData(
            rendererModel, 123, 999, 42, 0, 1, 0, 1);
        Assert.IsTrue(data.IsLoaded);
        Assert.AreEqual(2, data.DoodadSets!.Count);
        Assert.AreEqual("Exterior", data.Groups![0].Name);
        Assert.AreEqual("Diffuse", data.Materials![0].Shader);
        Assert.AreEqual(3, data.Materials[0].TextureSlots!.Count);
        Assert.AreEqual(0xFF112233u, data.Materials[0].Color1);
        Assert.AreEqual(7u, data.Materials[0].GroundType);

        var snapshot = new EditorObjectSnapshot(
            EditorObjectId.New(), "Test WMO", "World model", ObjectTransform.Identity, data);
        var placement = new PlacementInformationViewModel();
        placement.SetPlacement(snapshot);
        Assert.AreEqual(2, placement.DoodadSetOptions.Count);
        Assert.AreEqual("(1) Set_Day", placement.SelectedDoodadSet!.DisplayName);
        var placementView = new PlacementInformationView { DataContext = placement };
        placementView.Measure(new global::Avalonia.Size(400, 800));
        var comboBox = placementView.FindControl<ComboBox>("DoodadSetComboBox");
        Assert.IsNotNull(comboBox);
        Assert.AreEqual(2, comboBox.ItemCount);

        var model = new ModelInformationViewModel();
        model.SetModel(snapshot);
        Assert.IsTrue(model.HasWorldModelGroups);
        Assert.AreEqual("Exterior", model.SelectedGroup!.Name);
        Assert.IsTrue(model.HasBatches);
        Assert.AreSame(model.SelectedMaterial, model.SelectedBatch!.Material);
        Assert.AreEqual("WMO groups (1)", model.WorldModelGroupsHeader);
        Assert.AreEqual("Textures (3)", model.TexturesHeader);
        Assert.AreEqual("Materials (1)", model.MaterialsHeader);
        Assert.AreEqual("Render batches (1)", model.BatchesHeader);
        Assert.AreEqual(3, model.SelectedMaterial!.TextureSlots!.Count);
        Assert.AreEqual(4, model.SelectedMaterial!.Colors!.Count);
        var modelView = new ModelInformationView { DataContext = model };
        modelView.Measure(new global::Avalonia.Size(400, 1200));
        var groupsList = modelView.FindControl<ListBox>("WmoGroupsList");
        Assert.IsNotNull(groupsList);
        Assert.AreEqual(1, groupsList.ItemCount);
        Assert.IsTrue(groupsList.IsVisible);
        Assert.IsNotNull(modelView.FindControl<MaterialDetailsView>("SelectedMaterialDetails"));
        Assert.IsNotNull(modelView.FindControl<MaterialDetailsView>("SelectedBatchMaterialDetails"));
    }

    [TestMethod]
    public void WmoDoodadSelection_EnablesDefaultAndPlacementSetsAndRejectsInvalidFdids()
    {
        var enabled = WMOContainer.BuildEnabledDoodadSetMask(3, [1u]);
        CollectionAssert.AreEqual(new[] { true, true, false }, enabled);
        Assert.IsTrue(SceneManager.IsWmoDoodadSpawnable(
            new WMODoodad { filedataid = 456, doodadSet = 1 }, enabled));
        Assert.IsFalse(SceneManager.IsWmoDoodadSpawnable(
            new WMODoodad { filedataid = 0, doodadSet = 1 }, enabled));
        Assert.IsFalse(SceneManager.IsWmoDoodadSpawnable(
            new WMODoodad { filedataid = 456, doodadSet = 2 }, enabled));
    }

    [TestMethod]
    public void WmoModnParser_PreservesOffsetsWhenConvertingMdxNames()
    {
        var bytes = Encoding.ASCII.GetBytes("world\\a.mdx\0world\\longer.mdx\0");
        var names = ReadMdxNames(bytes);

        Assert.AreEqual(2, names.Count);
        Assert.AreEqual("world\\a.m2", names[0].Name);
        Assert.AreEqual(0u, names[0].Offset);
        Assert.AreEqual((uint)"world\\a.mdx".Length + 1u, names[1].Offset);
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

        var sets = WMOLoader.ReadTextureCoordinateChunks(bytes, 2);

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

    private static List<(string Name, uint Offset)> ReadMdxNames(byte[] bytes)
    {
        var result = new List<(string Name, uint Offset)>();
        var offset = 0u;
        var start = 0;
        while (start < bytes.Length)
        {
            var end = Array.IndexOf(bytes, (byte)0, start);
            if (end < 0)
                end = bytes.Length;
            var name = Encoding.ASCII.GetString(bytes, start, end - start);
            if (name.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase))
                name = name[..^4] + ".m2";
            result.Add((name, offset));
            start = end + 1;
            offset = (uint)start;
        }
        return result;
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

        var retainedRunLengths = TerrainBatching.BuildCompatibleRunLengths(batches);
        CollectionAssert.AreEqual(new[] { 2, 1, 1, 1 }, retainedRunLengths);
        Assert.AreEqual(
            2,
            TerrainBatching.CountCompatibleContiguousChunks(
                0,
                new[] { 0, 1, 2, 3 },
                new[] { true, true, true, true },
                retainedRunLengths));
        Assert.AreEqual(
            1,
            TerrainBatching.CountCompatibleContiguousChunks(
                0,
                new[] { 0, 1 },
                new[] { true, false },
                retainedRunLengths));
    }

    [TestMethod]
    public void TerrainShaderVariants_SelectSmallestFixedLayerBucket()
    {
        Assert.AreEqual(1, TerrainBatching.GetShaderLayerCount(1));
        Assert.AreEqual(2, TerrainBatching.GetShaderLayerCount(2));
        Assert.AreEqual(4, TerrainBatching.GetShaderLayerCount(3));
        Assert.AreEqual(4, TerrainBatching.GetShaderLayerCount(4));
        Assert.AreEqual(8, TerrainBatching.GetShaderLayerCount(5));
        Assert.IsFalse(TerrainBatching.UsesHeightTextures(4, new[] { 0, 0, 0, 0 }));
        Assert.IsFalse(TerrainBatching.UsesHeightTextures(2, new[] { 0, 0, 123, 0 }));
        Assert.IsTrue(TerrainBatching.UsesHeightTextures(2, new[] { 0, 123, 0, 0 }));
    }

    [TestMethod]
    public void TerrainBrushTools_KeepSharedPipelineAndModeSpecificOperationsSeparate()
    {
        var vertices = new ADTVertex[145];
        var sample = new TerrainBrushSample(
            vertices,
            0,
            10f,
            2f,
            8f,
            20f,
            EditAction.Positive);

        Assert.AreEqual(12f, TerrainBrushTools.Get(TerrainBrushMode.Sculpt).Apply(sample));
        Assert.AreEqual(20f, TerrainBrushTools.Get(TerrainBrushMode.Flatten).Apply(sample));
        Assert.AreEqual(1, TerrainBrushTools.Get(TerrainBrushMode.Sculpt).GetPassCount(8));
        Assert.AreEqual(8, TerrainBrushTools.Get(TerrainBrushMode.Smooth).GetPassCount(8));
    }

    [TestMethod]
    public void EditorModeDefinitions_KeepCapabilitiesWithModeMetadata()
    {
        Assert.AreEqual(EditorModeDefinitions.SelectionId, EditorModeDefinitions.Selection.Id);
        Assert.IsTrue(EditorModeDefinitions.Selection.Capabilities.HasFlag(EditorModeCapabilities.Selection));
        Assert.IsTrue(EditorModeDefinitions.Terrain.Capabilities.HasFlag(EditorModeCapabilities.TerrainEditing));
        Assert.IsTrue(EditorModeDefinitions.Texture.Capabilities.HasFlag(EditorModeCapabilities.TextureEditing));
        Assert.IsNotNull(EditorModeDefinitions.Selection.Icon);
        Assert.IsNotNull(EditorModeDefinitions.Terrain.Icon);
        Assert.IsNotNull(EditorModeDefinitions.Texture.Icon);
        Assert.AreNotSame(EditorModeDefinitions.Selection.Icon, EditorModeDefinitions.Terrain.Icon);
        Assert.AreEqual(10d, new TerrainEditingViewModel().Brush.Size);
    }

    [TestMethod]
    public void BrushSettings_AreSharedWhileToolSpecificSettingsStaySeparate()
    {
        var terrain = new TerrainEditingViewModel();
        var texture = new TextureEditingViewModel();

        CollectionAssert.AreEquivalent(
            new[] { BrushShape.Circle, BrushShape.Square },
            terrain.Brush.AvailableBrushes.Select(option => option.Shape).Distinct().ToArray());
        CollectionAssert.AreEqual(
            terrain.Brush.AvailableBrushes.Select(option => option.Id).ToArray(),
            texture.Brush.AvailableBrushes.Select(option => option.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "Paint", "Colour" },
            texture.SubModes.Select(mode => mode.DisplayName).ToArray());
        Assert.IsTrue(texture.SubModes.All(mode => mode.Icon != null));
        Assert.IsTrue(texture.SubModes.All(mode => mode.UsesFalloff));

        terrain.Speed = 100;
        texture.Opacity = 300;
        texture.Strength = -1;
        Assert.AreEqual(50d, terrain.Speed);
        Assert.AreEqual(255d, texture.Opacity);
        Assert.AreEqual(0d, texture.Strength);
        Assert.IsTrue(terrain.Brush.HasFalloff);
        Assert.AreEqual(0.35d, terrain.Brush.Falloff);

        terrain.Brush.SelectedBrush = terrain.Brush.AvailableBrushes.Single(
            brush => brush.Id == BuiltInBrushPreset.HardRound);
        Assert.IsFalse(terrain.Brush.HasFalloff);
        Assert.IsFalse(terrain.Brush.IsFalloffVisible);
    }

    [TestMethod]
    public void BrushMath_SupportsSquareFootprintsAndOptionalFalloff()
    {
        var circle = new BrushInput(
            10f,
            0.5f,
            false,
            BrushShape.Circle,
            BrushFalloffProfile.Smooth);
        var square = circle with { Shape = BrushShape.Square };

        Assert.AreEqual(0f, BrushMath.CalculateInfluence(8f, 8f, circle));
        Assert.AreEqual(1f, BrushMath.CalculateInfluence(8f, 8f, square));
        Assert.AreEqual(1f, BrushMath.CalculateInfluence(9f, 0f, circle));

        var softened = circle with { HasFalloff = true };
        var edgeInfluence = BrushMath.CalculateInfluence(9f, 0f, softened);
        Assert.IsTrue(edgeInfluence > 0f && edgeInfluence < 1f);
        var linear = softened with { FalloffProfile = BrushFalloffProfile.Linear };
        var gaussian = softened with { FalloffProfile = BrushFalloffProfile.Gaussian };
        Assert.AreNotEqual(
            BrushMath.CalculateInfluence(6.25f, 0f, linear),
            BrushMath.CalculateInfluence(6.25f, 0f, softened));
        Assert.AreNotEqual(
            BrushMath.CalculateInfluence(6.25f, 0f, gaussian),
            BrushMath.CalculateInfluence(6.25f, 0f, softened));
        Assert.AreEqual(288, Marshal.SizeOf<ADTPerObjectCB>(),
            "The managed ADT constant buffer must retain the shader's 16-byte register layout.");
    }

    [TestMethod]
    public void NonlinearSlider_DoesNotOverwriteValueWhileRangeBindingsInitialize()
    {
        var slider = new NonlinearSlider
        {
            Value = 10d,
            Minimum = 1d
        };

        Assert.AreEqual(10d, slider.Value,
            "A temporary range must not write its clamp back through the two-way Value binding.");

        slider.Maximum = 1000d;
        Assert.AreEqual(10d, slider.Value);
    }

    [TestMethod]
    public void WmoPortalVisibility_CullsInteriorButNeverExteriorGroupsOrTheirModrDoodads()
    {
        var portal = new WmoPortal
        {
            Vertices =
            [
                new(-0.25f, -0.25f, 0f),
                new(0.25f, -0.25f, 0f),
                new(0.25f, 0.25f, 0f),
                new(-0.25f, 0.25f, 0f)
            ],
            Normal = Vector3.UnitZ,
            Distance = 0f,
            Bounds = new BoundingBox(new(-0.25f, -0.25f, 0f), new(0.25f, 0.25f, 0f))
        };
        var exterior = new WorldModelGroupBatches
        {
            flags = 0x8,
            boundingBox = new BoundingBox(new(-0.8f), new(0.8f)),
            portalLinks = [new WmoPortalLink { PortalIndex = 0, TargetGroupIndex = 1, Side = 1 }],
            doodadReferences = []
        };
        var interior = new WorldModelGroupBatches
        {
            flags = 0x2000,
            boundingBox = new BoundingBox(new(-0.4f), new(0.4f)),
            portalLinks = [],
            doodadReferences = [0]
        };
        var disconnectedExterior = new WorldModelGroupBatches
        {
            flags = 0x8,
            boundingBox = new BoundingBox(new(10f), new(11f)),
            portalLinks = [],
            doodadReferences = [1]
        };
        var unclassifiedOutdoor = new WorldModelGroupBatches
        {
            flags = 0,
            boundingBox = new BoundingBox(new(20f), new(21f)),
            portalLinks = [],
            doodadReferences = [2]
        };
        var ambiguousOutdoor = new WorldModelGroupBatches
        {
            flags = 0x8 | 0x2000,
            boundingBox = new BoundingBox(new(30f), new(31f)),
            portalLinks = [],
            doodadReferences = [3]
        };
        var exteriorLitWithoutInteriorFlag = new WorldModelGroupBatches
        {
            flags = 0x40,
            boundingBox = new BoundingBox(new(40f), new(41f)),
            portalLinks = [],
            doodadReferences = [4]
        };
        var wmo = new WorldModel
        {
            groupBatches =
            [
                exterior,
                interior,
                disconnectedExterior,
                unclassifiedOutdoor,
                ambiguousOutdoor,
                exteriorLitWithoutInteriorFlag
            ],
            portals = [portal],
            portalGraphValid = true,
            doodads = new WMODoodad[5],
            doodadsReferencedByGroups = [true, true, true, true, true]
        };
        var groups = new bool[6];
        var doodads = new bool[5];
        var scratch = new WmoPortalVisibilityScratch();

        Assert.IsTrue(WmoPortalVisibility.TryCompute(
            wmo,
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            new Vector3(0f, 0f, -0.5f),
            new[] { true, true, true, true, true, true },
            groups,
            doodads,
            scratch,
            out _));
        CollectionAssert.AreEqual(new[] { true, false, true, true, true, true }, groups);
        CollectionAssert.AreEqual(new[] { false, true, true, true, true }, doodads);

        wmo.groupBatches[0] = new WorldModelGroupBatches
        {
            flags = 0x8,
            boundingBox = exterior.boundingBox,
            portalLinks = [new WmoPortalLink { PortalIndex = 0, TargetGroupIndex = 1, Side = -1 }],
            doodadReferences = []
        };
        Assert.IsTrue(WmoPortalVisibility.TryCompute(
            wmo,
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            new Vector3(0f, 0f, -0.5f),
            new[] { true, true, true, true, true, true },
            groups,
            doodads,
            scratch,
            out _));
        CollectionAssert.AreEqual(new[] { true, true, true, true, true, true }, groups);
        CollectionAssert.AreEqual(new[] { true, true, true, true, true }, doodads);

        // A missing 0x2000 interior bit defines an exterior group even when the
        // redundant 0x8 exterior bit is absent. It must seed portal traversal.
        wmo.groupBatches[0] = new WorldModelGroupBatches
        {
            flags = 0,
            boundingBox = exterior.boundingBox,
            portalLinks = [new WmoPortalLink { PortalIndex = 0, TargetGroupIndex = 1, Side = -1 }],
            doodadReferences = []
        };
        Assert.IsTrue(WmoPortalVisibility.TryCompute(
            wmo,
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            new Vector3(0f, 0f, -0.5f),
            new[] { true, true, true, true, true, true },
            groups,
            doodads,
            scratch,
            out _));
        CollectionAssert.AreEqual(new[] { true, true, true, true, true, true }, groups);
        CollectionAssert.AreEqual(new[] { true, true, true, true, true }, doodads);
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
