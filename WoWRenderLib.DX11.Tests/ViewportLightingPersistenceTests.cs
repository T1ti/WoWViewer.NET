using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class ViewportLightingPersistenceTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ViewSettingsRetainEvaluatedLightingWithoutCameraMovement(bool clientMode, bool liveTime)
    {
        // ApplySettings only needs CPU state. Avoid creating a device or loading a client.
        var engine = (WowViewerEngine)RuntimeHelpers.GetUninitializedObject(typeof(WowViewerEngine));
        var scene = (SceneManager)RuntimeHelpers.GetUninitializedObject(typeof(SceneManager));
        var saved = new RendererSettings
        {
            UseClientRenderingRules = clientMode,
            UseConfiguredLighting = false // WTEditor uses evaluated lighting, not settings colors.
        };
        SetEngineField(engine, "<Settings>k__BackingField", saved);
        SetEngineField(engine, "sceneManager", scene);
        var camera = new Camera(Vector3.Zero, 0f, 0f, 1f);
        SetEngineField(engine, "<activeCamera>k__BackingField", camera);
        SetEngineField(engine, "_editorWorldLighting", WorldLightingSettings.Defaults with
        {
            Time = 1200, IsDynamic = liveTime
        });
        var lighting = WorldLightingSettings.Defaults with
        {
            LightParamId = 42, Time = 1200, IsDynamic = liveTime,
            AmbientColor = new Vector3(0.2f, 0.3f, 0.4f),
            DiffuseColor = new Vector3(0.6f, 0.5f, 0.4f)
        };
        WorldLightingContribution[] contributors =
        [new(WorldLightingSourceKind.Local, 17, 42, 1f)];
        scene.ApplyWorldLighting(lighting, contributors);
        var expected = scene.ActiveWorldLighting;

        Action<RendererSettings>[] toggles =
        [
            settings => settings.RenderFog = !settings.RenderFog,
            settings => settings.RenderWMO = !settings.RenderWMO,
            settings => settings.RenderM2 = !settings.RenderM2,
            settings => settings.RenderADT = !settings.RenderADT,
            settings => settings.RenderLiquid = !settings.RenderLiquid,
            settings => settings.RenderParticles = !settings.RenderParticles,
            settings => settings.AnimateModels = !settings.AnimateModels,
            settings => settings.ShowTerrainWireframe = !settings.ShowTerrainWireframe,
            settings => settings.ShowTerrainGrid = !settings.ShowTerrainGrid
        ];
        foreach (var toggle in toggles)
        {
            var settings = engine.Settings.Clone();
            toggle(settings);
            engine.ApplySettings(settings);
            Assert.AreEqual(expected, engine.ActiveWorldLighting);
            CollectionAssert.AreEqual(contributors, engine.ActiveWorldLightingContributions.ToArray());
            Assert.AreEqual(Vector3.Zero, camera.Position);
        }
    }

    private static void SetEngineField(WowViewerEngine engine, string name, object value) =>
        typeof(WowViewerEngine).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(engine, value);
}
