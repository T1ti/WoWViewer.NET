using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
public sealed class WorldRenderingRulesTests
{
    [TestMethod]
    public void EditorModeKeepsWholeWorldDistancesAndCustomCulling()
    {
        var settings = new RendererSettings
        {
            UseClientRenderingRules = false,
            TerrainRenderDistance = 100_000f, ModelRenderDistance = 50_000f,
            WrathFarClip = 350f, MinimumModelScreenSizePixels = 4f,
            TerrainLodTransitionPixels = 64f
        };
        var rules = WorldRenderingRules.Resolve(settings, true, 0, true);
        Assert.IsFalse(rules.UseClientRules);
        Assert.AreEqual(100_000f, rules.FarPlane);
        Assert.AreEqual(100_000f, rules.TerrainDistance);
        Assert.AreEqual(50_000f, rules.ModelDistance);
        Assert.AreEqual(4f, rules.MinimumModelPixels);
        Assert.AreEqual(64f, rules.TerrainLodPixels);
        Assert.IsFalse(rules.PortalCulling);
    }

    [TestMethod]
    public void ClientModeUsesMapCvarAndMemoryLimitsIndependentlyOfEditorDistances()
    {
        var settings = new RendererSettings
        {
            UseClientRenderingRules = true, TerrainRenderDistance = 100f,
            ModelRenderDistance = 20_000f, WrathFarClip = 1277f
        };
        var rules = WorldRenderingRules.Resolve(settings, true, 0, true);
        Assert.IsTrue(rules.UseClientRules);
        Assert.AreEqual(Wrath335FarClip.FixedNearClip, rules.NearPlane);
        Assert.AreEqual(Wrath335FarClip.StandardMaximum, rules.FarPlane);
        Assert.AreEqual(rules.FarPlane, rules.TerrainDistance);
        Assert.AreEqual(rules.FarPlane, rules.ModelDistance);
        Assert.IsTrue(rules.PortalCulling);
        Assert.AreEqual(0f, rules.MinimumModelPixels);
        Assert.AreEqual(0f, rules.TerrainLodPixels);
        Assert.AreEqual(1277f, WorldRenderingRules.Resolve(settings, true, 530, true).FarPlane);
        Assert.AreEqual(Wrath335FarClip.StandardMaximum,
            WorldRenderingRules.Resolve(settings, true, 530, false).FarPlane);
        settings.WrathFarClipOverride = true;
        Assert.AreEqual(1277f, WorldRenderingRules.Resolve(settings, true, 0, false).FarPlane);
        Assert.AreEqual(100f, settings.TerrainRenderDistance);
    }

    [TestMethod]
    public void ModeSwitchRestoresEditorControlsAndDoesNotApplyWrathRulesToOtherClients()
    {
        var settings = new RendererSettings
        {
            UseClientRenderingRules = true, TerrainRenderDistance = 75_000f,
            ModelRenderDistance = 40_000f, EnableWmoPortalCulling = false,
            MinimumModelScreenSizePixels = 2f
        };
        var other = WorldRenderingRules.Resolve(settings, false, 0, true);
        Assert.IsFalse(other.UseClientRules);
        Assert.AreEqual(WorldViewportSettings.FallbackRenderDistance, other.FarPlane);
        Assert.AreEqual(0f, other.MinimumModelPixels);
        Assert.IsFalse(other.PortalCulling);
        settings.UseClientRenderingRules = false;
        var editor = WorldRenderingRules.Resolve(settings, true, 0, true);
        Assert.AreEqual(75_000f, editor.FarPlane);
        Assert.AreEqual(40_000f, editor.ModelDistance);
        Assert.IsFalse(editor.PortalCulling);
        Assert.AreEqual(2f, editor.MinimumModelPixels);
    }
}
