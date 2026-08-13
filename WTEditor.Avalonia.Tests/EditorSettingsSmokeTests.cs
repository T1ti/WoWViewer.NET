using System.Numerics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Application;
using WTEditor.Application.Commands;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.ViewModels;

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
