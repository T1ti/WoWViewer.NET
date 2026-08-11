using System.Numerics;
using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11;
using WTEditor.Avalonia.Services;

namespace WTEditor.Avalonia.Tests;

[TestClass]
[DoNotParallelize]
public sealed class EditorSettingsSmokeTests
{
    [TestMethod]
    public void PersistedSettings_RoundTripPreservesStartupConfiguration()
    {
        var clientConfig = new WowClientConfig
        {
            wowDir = @"D:\Games\World of Warcraft",
            wowProduct = "wow",
            buildConfig = "build-config-key",
            cdnConfig = "cdn-config-key"
        };
        var renderer = new RendererSettings
        {
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
        };
        var cameraPosition = new Vector3(10.5f, -20.25f, 30.75f);
        var cameraDirection = Vector3.Normalize(new Vector3(-0.25f, 0.5f, 0.75f));

        var persisted = PersistedEditorSettings.From(
            clientConfig,
            renderer,
            "AZERTY",
            cameraPosition,
            cameraDirection);
        persisted.HasWindowBounds = true;
        persisted.WindowState = "FullScreen";
        persisted.WindowX = 42;
        persisted.WindowY = 84;
        persisted.WindowWidth = 1_920;
        persisted.WindowHeight = 1_080;

        var json = JsonSerializer.Serialize(persisted);
        var restored = JsonSerializer.Deserialize<PersistedEditorSettings>(json);

        Assert.IsNotNull(restored);
        Assert.AreEqual(clientConfig.wowDir, restored.ToClientConfig().wowDir);
        Assert.AreEqual(clientConfig.wowProduct, restored.ToClientConfig().wowProduct);
        Assert.AreEqual(clientConfig.buildConfig, restored.ToClientConfig().buildConfig);
        Assert.AreEqual(clientConfig.cdnConfig, restored.ToClientConfig().cdnConfig);
        Assert.AreEqual("AZERTY", restored.KeyboardLayout);
        Assert.AreEqual(cameraPosition, restored.GetCameraPosition());
        Assert.IsTrue(restored.HasCameraPosition);
        Assert.AreEqual(cameraDirection, restored.GetCameraDirection());
        Assert.IsTrue(restored.HasCameraDirection);
        Assert.AreEqual(renderer.TerrainRenderDistance, restored.Renderer.TerrainRenderDistance);
        Assert.AreEqual(renderer.ModelRenderDistance, restored.Renderer.ModelRenderDistance);
        Assert.AreEqual(renderer.TileLoadingDistance, restored.Renderer.TileLoadingDistance);
        Assert.IsFalse(restored.Renderer.RenderADT);
        Assert.IsTrue(restored.Renderer.ShowBoundingBoxes);
        Assert.IsTrue(restored.HasWindowBounds);
        Assert.AreEqual("FullScreen", restored.WindowState);
        Assert.AreEqual(42, restored.WindowX);
        Assert.AreEqual(84, restored.WindowY);
        Assert.AreEqual(1_920, restored.WindowWidth);
        Assert.AreEqual(1_080, restored.WindowHeight);
    }

    [TestMethod]
    public void RendererSettings_FromPersistedSettingsIsIndependentClone()
    {
        var source = new RendererSettings { TerrainRenderDistance = 5_000f };
        var persisted = PersistedEditorSettings.From(new WowClientConfig(), source, "QWERTY");

        source.TerrainRenderDistance = 1f;

        Assert.AreEqual(5_000f, persisted.Renderer.TerrainRenderDistance);
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
    public void EditorSettingsStore_PreservesNormalBoundsWhenSavingFullscreenState()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        var previousSettings = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
        var clientConfig = new WowClientConfig { wowDir = @"D:\Games\World of Warcraft", wowProduct = "wow" };
        var renderer = new RendererSettings();

        try
        {
            EditorSettingsStore.Save(
                clientConfig,
                renderer,
                "QWERTY",
                new Vector3(1, 2, 3),
                windowX: 10,
                windowY: 20,
                windowWidth: 1_280,
                windowHeight: 720,
                windowState: "Normal",
                cameraDirection: new Vector3(0, 1, 0));

            EditorSettingsStore.Save(
                clientConfig,
                renderer,
                "QWERTY",
                new Vector3(1, 2, 3),
                windowState: "FullScreen",
                cameraDirection: new Vector3(0, 1, 0));

            var restored = EditorSettingsStore.Load();

            Assert.IsTrue(restored.HasWindowBounds);
            Assert.AreEqual("FullScreen", restored.WindowState);
            Assert.AreEqual(10, restored.WindowX);
            Assert.AreEqual(20, restored.WindowY);
            Assert.AreEqual(1_280, restored.WindowWidth);
            Assert.AreEqual(720, restored.WindowHeight);
            Assert.IsTrue(restored.HasCameraDirection);
            Assert.AreEqual(new Vector3(0, 1, 0), restored.GetCameraDirection());
        }
        finally
        {
            if (previousSettings is null)
                File.Delete(settingsPath);
            else
                File.WriteAllText(settingsPath, previousSettings);
        }
    }
}
