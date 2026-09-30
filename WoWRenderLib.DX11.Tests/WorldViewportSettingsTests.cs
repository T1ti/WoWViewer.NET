using System.Numerics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class WorldViewportSettingsTests
{
    [TestMethod]
    public void ClientModeIgnoresViewportFiltersOverlaysAndDistancesWithoutChangingEitherBank()
    {
        var saved = new RendererSettings
        {
            RenderADT = false, RenderLiquid = false, RenderWMO = false, RenderM2 = false,
            RenderParticles = false, RenderFog = false, AnimateModels = false,
            ShowWmoCollisionMesh = true, ShowBoundingBoxes = true, ShowBoundingSpheres = true,
            ShowTerrainGrid = true, ShowTerrainWireframe = true,
            ShowTerrainTexture = false, ShowTerrainVertexColor = false,
            EditorDisableScreenGlow = true, DisableScreenGlow = false,
            UseConfiguredLighting = true, EnableWmoPortalCulling = true,
            TerrainRenderDistance = 100_000f, ModelRenderDistance = 80_000f,
            MinimumModelScreenSizePixels = 16f, TerrainLodTransitionPixels = 256f,
            TileLoadingDistance = 0, AnimationRenderDistancePercent = 0f,
            ParticleRenderDistancePercent = 0f,
            WrathFarClip = 700f, WrathFarClipOverride = true, SkyCloudLod = 2,
            ParticleDensity = 0.4f, EnvironmentDetail = 0.75f
        };
        var before = JsonSerializer.Serialize(saved);
        var client = WorldViewportSettings.Resolve(saved);
        Assert.IsTrue(client.RenderADT && client.RenderLiquid && client.RenderWMO &&
            client.RenderM2 && client.RenderParticles && client.RenderFog && client.AnimateModels);
        Assert.IsFalse(client.ShowWmoCollisionMesh || client.ShowBoundingBoxes ||
            client.ShowBoundingSpheres || client.ShowTerrainGrid || client.ShowTerrainWireframe);
        Assert.IsTrue(client.ShowTerrainTexture && client.ShowTerrainVertexColor);
        Assert.IsFalse(client.UseConfiguredLighting);
        Assert.IsFalse(client.DisableScreenGlow);
        Assert.AreEqual(100f, client.AnimationRenderDistancePercent);
        Assert.AreEqual(100f, client.ParticleRenderDistancePercent);
        Assert.AreEqual(WorldViewportSettings.FallbackTileLoadingDistance, client.TileLoadingDistance);
        Assert.AreEqual(700f, client.WrathFarClip);
        Assert.IsTrue(client.WrathFarClipOverride);
        Assert.AreEqual(2, client.SkyCloudLod);
        Assert.AreEqual(0.4f, client.ParticleDensity);
        Assert.AreEqual(0.75f, client.EnvironmentDetail);
        Assert.AreEqual(before, JsonSerializer.Serialize(saved));

        saved.UseClientRenderingRules = false;
        var editor = WorldViewportSettings.Resolve(saved);
        Assert.IsFalse(editor.RenderADT || editor.RenderLiquid || editor.RenderWMO ||
            editor.RenderM2 || editor.RenderParticles || editor.RenderFog || editor.AnimateModels);
        Assert.IsTrue(editor.ShowWmoCollisionMesh && editor.ShowBoundingBoxes &&
            editor.ShowBoundingSpheres && editor.ShowTerrainGrid && editor.ShowTerrainWireframe);
        Assert.IsFalse(editor.ShowTerrainTexture || editor.ShowTerrainVertexColor);
        Assert.IsTrue(editor.DisableScreenGlow);
        Assert.AreEqual(100_000f, editor.TerrainRenderDistance);
        Assert.AreEqual(80_000f, editor.ModelRenderDistance);
        Assert.AreEqual(0, editor.TileLoadingDistance);
        Assert.AreEqual(0f, editor.AnimationRenderDistancePercent);
        Assert.AreEqual(0f, editor.ParticleRenderDistancePercent);
    }

    [TestMethod]
    public void ClientGlowCVarAndFogAreIndependentOfTheEditorOverrides()
    {
        var saved = new RendererSettings
        {
            DisableScreenGlow = true, EditorDisableScreenGlow = false, RenderFog = false
        };
        var client = WorldViewportSettings.Resolve(saved);
        Assert.IsTrue(client.DisableScreenGlow);
        Assert.IsTrue(client.RenderFog);
        saved.UseClientRenderingRules = false;
        var editor = WorldViewportSettings.Resolve(saved);
        Assert.IsFalse(editor.DisableScreenGlow);
        Assert.IsFalse(editor.RenderFog);
    }

    [TestMethod]
    public void ClientLightingIgnoresManualColorsAndTimeWhileEditorLightingRestoresThem()
    {
        var editor = WorldLightingSettings.Defaults with
        {
            Time = 123, IsDynamic = false,
            AmbientColor = Vector3.UnitX, DiffuseColor = Vector3.UnitY,
            OceanCloseColor = Vector3.UnitZ, WaterShallowAlpha = 0.2f
        };
        var client = WorldViewportSettings.ResolveLighting(true, editor);
        Assert.AreEqual(WorldLightingSettings.Defaults with { IsDynamic = true }, client);
        Assert.AreEqual(editor, WorldViewportSettings.ResolveLighting(false, editor));
    }
}
