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

[TestClass]
[DoNotParallelize]
public sealed partial class EditorSettingsSmokeTests
{
    [AssemblyInitialize]
    public static void InitializeAvalonia(TestContext _)
    {
        AppBuilder.Configure<global::Avalonia.Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
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
                WorldLightingTime = 1800,
                UseLocalWorldLightingTime = true,
                MovementSpeed = 225f,
                MouseSensitivity = 0.25f,
                RenderADT = false,
                RenderLiquid = false,
                RenderWMO = true,
                ShowWmoCollisionMesh = true,
                RenderM2 = false,
                RenderParticles = false,
                AnimateModels = false,
                EnableWmoPortalCulling = true,
                ShowBoundingBoxes = true,
                ShowBoundingSpheres = true,
                ShowTerrainGrid = true,
                ShowTerrainWireframe = true,
                ShowTerrainTexture = false,
                ShowTerrainVertexColor = false
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
    public void ClientSettingsCanChangeTheExistingGlowOption()
    {
        var original = new EditorSettingsSnapshot
        {
            Rendering = new RenderingConfiguration { DisableScreenGlow = true }
        };
        var viewModel = new ClientSettingsViewModel(original);
        Assert.IsTrue(viewModel.DisableScreenGlow);

        viewModel.DisableScreenGlow = false;
        var changed = viewModel.ApplyTo(original);
        Assert.IsFalse(changed.Rendering.DisableScreenGlow);
        Assert.AreEqual(original.Rendering.TerrainRenderDistance,
            changed.Rendering.TerrainRenderDistance);
    }

    [TestMethod]
    public void LegacyRenderingSettings_DefaultTerrainLayersToVisible()
    {
        var restored = JsonSerializer.Deserialize<PersistedRenderingSettings>("{}");

        Assert.IsNotNull(restored);
        Assert.IsTrue(restored.ToModel().ShowTerrainTexture);
        Assert.IsTrue(restored.ToModel().ShowTerrainVertexColor);
        Assert.IsTrue(restored.ToModel().ToDx11().ShowTerrainTexture);
        Assert.IsTrue(restored.ToModel().ToDx11().ShowTerrainVertexColor);
    }

    [TestMethod]
    public void LightingViewModel_ShowsWmoSidnPulseAndTimedSunSpecularColor()
    {
        var viewModel = new LightingViewModel();
        Assert.AreEqual("0", viewModel.WmoSidnPulseDisplay);
        Assert.AreEqual("(1, 0.969, 0.871)", viewModel.SpecularColorDisplay);

        var sky = WorldSkyLighting.None with
        {
            SunColor = new Vector3(0.25f, 0.5f, 0.75f),
            HasSunCloudData = true
        };
        viewModel.Update(LightingSettingsProjection.ToDisplay(
            WorldLightingSettings.Defaults with { Time = 780 }, sky));

        Assert.AreEqual("0.5", viewModel.WmoSidnPulseDisplay);
        Assert.AreEqual("(0.125, 0.25, 0.375)", viewModel.SpecularColorDisplay);
    }

    [TestMethod]
    public void LightingViewModelLabelsWrathCloudBandsWithoutModernLayerNames()
    {
        var viewModel = new LightingViewModel();
        var sky = WorldSkyLighting.None with
        {
            LegacyCloudEmissiveColor = new Vector3(1f, 0f, 0f),
            LegacyCloudBodyColor = new Vector3(0f, 1f, 0f),
            LegacyCloudAmbientColor = new Vector3(0f, 0f, 1f),
            HasLegacyCloudData = true,
            HasSunCloudData = true
        };
        viewModel.Update(LightingSettingsProjection.ToDisplay(WorldLightingSettings.Defaults, sky));

        var clouds = viewModel.RuntimeGroups.Single(group => group.Title == "Clouds");
        CollectionAssert.AreEqual(
            new[] { "Emissive", "Body", "Ambient", "Density" },
            clouds.Values.Select(value => value.Label).ToArray());
    }

    [TestMethod]
    public void ViewportStreamingSettingsSurviveSessionSaveAndReload()
    {
        var store = new MemorySettingsStore(new EditorSettingsSnapshot());
        var session = new EditorSession(store);
        using var firstViewport = new Editor3DViewModel(session);
        using var secondViewport = new Editor3DViewModel(session);

        firstViewport.TileLoadingDistance = 8;
        firstViewport.TerrainRenderDistance = 12_000f;
        firstViewport.ModelRenderDistance = 9_000f;
        firstViewport.AnimationRenderDistancePercent = 65f;
        firstViewport.ParticleRenderDistancePercent = 15f;

        Assert.AreEqual(8, session.Current.Rendering.TileLoadingDistance);
        Assert.AreEqual(8, secondViewport.TileLoadingDistance);
        Assert.AreEqual(65f, secondViewport.AnimationRenderDistancePercent);
        Assert.AreEqual(15f, secondViewport.ParticleRenderDistancePercent);
        Assert.AreEqual(65f, session.Current.Rendering.ToDx11().Clone().AnimationRenderDistancePercent);
        Assert.AreEqual(15f, session.Current.Rendering.ToDx11().Clone().ParticleRenderDistancePercent);
        session.Save();

        var restored = new EditorSession(store);
        Assert.AreEqual(8, restored.Current.Rendering.TileLoadingDistance);
        Assert.AreEqual(12_000f, restored.Current.Rendering.TerrainRenderDistance);
        Assert.AreEqual(9_000f, restored.Current.Rendering.ModelRenderDistance);
        Assert.AreEqual(65f, restored.Current.Rendering.AnimationRenderDistancePercent);
        Assert.AreEqual(15f, restored.Current.Rendering.ParticleRenderDistancePercent);
    }

    [TestMethod]
    public void WorldViewportVisibilityIsPersistedAndSharedAcrossViewports()
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
        firstViewport.RenderLiquid = false;
        firstViewport.RenderWorldModels = false;
        firstViewport.ShowWmoCollisionMesh = true;
        firstViewport.RenderDoodads = false;
        firstViewport.RenderParticles = false;
        firstViewport.AnimateModels = false;
        firstViewport.WmoPortalCullingEnabled = true;
        firstViewport.ShowBoundingBoxes = true;
        firstViewport.ShowBoundingSpheres = true;
        firstViewport.ShowTerrainGrid = true;
        firstViewport.ShowTerrainWireframe = true;
        firstViewport.ShowTerrainTexture = false;
        firstViewport.ShowTerrainVertexColor = false;

        Assert.IsNotNull(published);
        Assert.IsFalse(published.RenderADT);
        Assert.IsFalse(published.RenderM2);
        Assert.IsFalse(published.RenderWMO);
        Assert.IsTrue(published.ShowWmoCollisionMesh);
        Assert.IsTrue(published.ToDx11().Clone().ShowWmoCollisionMesh);
        Assert.IsFalse(published.RenderLiquid);
        Assert.IsFalse(published.RenderParticles);
        Assert.IsFalse(published.ToDx11().RenderParticles);
        Assert.IsFalse(published.ToDx11().Clone().RenderParticles);
        Assert.IsFalse(published.AnimateModels);
        Assert.IsTrue(published.EnableWmoPortalCulling);
        Assert.IsTrue(published.ShowTerrainGrid);
        Assert.IsTrue(published.ShowTerrainWireframe);
        Assert.IsFalse(published.ShowTerrainTexture);
        Assert.IsFalse(published.ShowTerrainVertexColor);
        Assert.IsFalse(published.ToDx11().Clone().ShowTerrainTexture);
        Assert.IsFalse(published.ToDx11().Clone().ShowTerrainVertexColor);
        Assert.IsFalse(secondViewport.RenderTerrain);
        Assert.IsFalse(secondViewport.RenderLiquid);
        Assert.IsFalse(secondViewport.RenderWorldModels);
        Assert.IsTrue(secondViewport.ShowWmoCollisionMesh);
        Assert.IsFalse(secondViewport.RenderDoodads);
        Assert.IsFalse(secondViewport.RenderParticles);
        Assert.IsFalse(secondViewport.AnimateModels);
        Assert.IsTrue(secondViewport.WmoPortalCullingEnabled);
        Assert.IsTrue(secondViewport.ShowBoundingBoxes);
        Assert.IsTrue(secondViewport.ShowBoundingSpheres);
        Assert.IsTrue(secondViewport.ShowTerrainGrid);
        Assert.IsTrue(secondViewport.ShowTerrainWireframe);
        Assert.IsFalse(secondViewport.ShowTerrainTexture);
        Assert.IsFalse(secondViewport.ShowTerrainVertexColor);
        Assert.IsFalse(session.Current.Rendering.RenderADT);
        Assert.IsFalse(session.Current.Rendering.RenderM2);
        Assert.IsFalse(session.Current.Rendering.RenderParticles);
        Assert.IsTrue(session.Current.Rendering.EnableWmoPortalCulling);
        Assert.IsTrue(session.Current.Rendering.ShowTerrainGrid);
        Assert.IsTrue(session.Current.Rendering.ShowTerrainWireframe);
        Assert.IsFalse(session.Current.Rendering.ShowTerrainTexture);
        Assert.IsFalse(session.Current.Rendering.ShowTerrainVertexColor);
        Assert.AreEqual(0, store.SaveCount);
        session.Save();
        Assert.AreEqual(session.Current.Rendering, new EditorSession(store).Current.Rendering);
    }

    [TestMethod]
    public void ViewportLightingPreferencesSurviveSessionSaveAndReload()
    {
        var store = new MemorySettingsStore(new EditorSettingsSnapshot());
        var session = new EditorSession(store);
        using var viewport = new Editor3DViewModel(session);
        viewport.UpdateActiveLighting(new LightingSettingsSnapshot(
            12, 1440, Vector3.UnitZ, Vector3.One, Vector3.One,
            Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
            1f, 1f, 1f, 1f, false, false, false));

        viewport.Lighting.Time = 1800;
        viewport.Lighting.IsDynamic = true;
        session.Save();

        var restored = new EditorSession(store);
        using var restoredViewport = new Editor3DViewModel(restored);
        Assert.AreEqual(1800, restored.Current.Rendering.WorldLightingTime);
        Assert.IsTrue(restored.Current.Rendering.UseLocalWorldLightingTime);
        Assert.AreEqual(1800L, restoredViewport.Lighting.Time);
        Assert.IsTrue(restoredViewport.Lighting.IsDynamic);
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

            using var viewport = new Editor3DViewModel(session);
            viewport.TileLoadingDistance = 8;
            viewport.RenderTerrain = false;
            session.UpdateWindow(session.Current.Window with { State = "FullScreen" }, save: true);

            var restored = store.Load();
            Assert.AreEqual(8, restored.Rendering.TileLoadingDistance);
            Assert.IsFalse(restored.Rendering.RenderADT);
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

    private sealed class RecordingTexturePreviewService : ITerrainTexturePreviewService
    {
        public (uint FileDataId, string DisplayName)? LastRequest { get; private set; }

        public Task ShowAsync(uint fileDataId, string displayName)
        {
            LastRequest = (fileDataId, displayName);
            return Task.CompletedTask;
        }
    }

    private sealed class NullTextureThumbnailService : ITerrainTextureThumbnailService
    {
        public Task<global::Avalonia.Media.IImage?> LoadAsync(
            uint fileDataId,
            CancellationToken cancellationToken = default) => Task.FromResult<global::Avalonia.Media.IImage?>(null);
    }

    private sealed class DeferredClientFileCatalogService : IClientFileCatalogService
    {
        public TaskCompletionSource<IReadOnlyList<ClientFileCatalogEntry>> Completion { get; } = new();
        public CancellationToken Token { get; private set; }

        public Task<IReadOnlyList<ClientFileCatalogEntry>> GetFilesAsync(
            CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            return Completion.Task;
        }
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

    private sealed class RecordingTool(string id, bool failOnActivation = false) : IEditorTool
    {
        public string Id { get; } = id;
        public string DisplayName => Id;
        public int ActivationCount { get; private set; }
        public int DeactivationCount { get; private set; }

        public void Activate()
        {
            if (failOnActivation)
                throw new InvalidOperationException("Activation failed.");
            ActivationCount++;
        }

        public void Deactivate() => DeactivationCount++;
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
