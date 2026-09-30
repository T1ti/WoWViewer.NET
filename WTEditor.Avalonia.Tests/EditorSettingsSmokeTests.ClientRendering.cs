using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Application;
using WTEditor.Application.Models;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Views;
using WoWRenderLib.DX11.Renderer;

namespace WTEditor.Avalonia.Tests;

public sealed partial class EditorSettingsSmokeTests
{
    [TestMethod]
    public void ClientRenderingDefaultsAndLegacySettingsUseUltraWithoutOverwritingSavedCVars()
    {
        var fresh = new RenderingConfiguration();
        var legacy = JsonSerializer.Deserialize<PersistedRenderingSettings>("{}")!.ToModel();
        var vm = new ClientSettingsViewModel(new EditorSettingsSnapshot());
        Assert.AreEqual("Ultra", vm.ClientQualityLabel);
        Assert.AreEqual(fresh, vm.ApplyTo(new EditorSettingsSnapshot()).Rendering);
        Assert.AreEqual(fresh, legacy);

        var renderer = fresh.ToDx11().Clone();
        Assert.IsTrue(renderer.UseClientRenderingRules);
        Assert.AreEqual(1277f, renderer.WrathFarClip);
        Assert.AreEqual(6, renderer.ClientRenderingQuality);
        Assert.AreEqual(1, renderer.TerrainMip);
        Assert.AreEqual(1f, renderer.ParticleDensity);
        Assert.AreEqual(1.5f, renderer.EnvironmentDetail);
        Assert.AreEqual(64, renderer.GroundEffectDensity);
        Assert.AreEqual(140, renderer.GroundEffectDist);
        Assert.AreEqual(1, renderer.BaseMip);
        Assert.AreEqual(4, renderer.ExtShadowQuality);
        Assert.AreEqual(5, renderer.TextureFilteringMode);
        Assert.AreEqual(3, renderer.WeatherDensity);
        Assert.AreEqual(9, renderer.ComponentTextureLevel);
        Assert.AreEqual(true, renderer.Specular);
        Assert.AreEqual(true, renderer.FfxDeath);
        Assert.AreEqual(true, renderer.ProjectedTextures);
        Assert.IsFalse(renderer.DisableScreenGlow);

        var saved = JsonSerializer.Deserialize<PersistedRenderingSettings>(
            """{"WrathFarClip":350,"DisableScreenGlow":true}""")!.ToModel();
        Assert.AreEqual(350f, saved.WrathFarClip);
        Assert.IsTrue(saved.DisableScreenGlow);
        Assert.AreEqual(6, saved.ClientRenderingQuality);
    }

    [TestMethod]
    public void ClientRenderingDialogChangesRoundTripThroughPersistenceAndRendererClone()
    {
        var original = new EditorSettingsSnapshot
        {
            Rendering = new RenderingConfiguration { TerrainRenderDistance = 50_000f }
        };
        var vm = new ClientSettingsViewModel(original)
        {
            WrathFarClip = 700f,
            FullScreenGlow = false,
            TerrainMip = 0,
            ParticleDensity = 0.4f,
            EnvironmentDetail = 0.75f,
            GroundEffectDensity = 32,
            GroundEffectDist = 90,
            BaseMip = 0,
            ExtShadowQuality = 2,
            TextureFilteringMode = 3,
            WeatherDensity = 1,
            ComponentTextureLevel = 8,
            Specular = false,
            FfxDeath = false,
            ProjectedTextures = false
        };
        Assert.AreEqual("Custom", vm.ClientQualityLabel);
        Assert.IsTrue(vm.DisableScreenGlow);
        vm.DisableScreenGlow = false;
        Assert.IsTrue(vm.FullScreenGlow);
        vm.FullScreenGlow = false;

        var applied = vm.ApplyTo(original).Normalize();
        var restored = JsonSerializer.Deserialize<PersistedRenderingSettings>(
            JsonSerializer.Serialize(PersistedRenderingSettings.From(applied.Rendering)))!
            .ToModel().Normalize();
        Assert.AreEqual(applied.Rendering, restored);
        Assert.AreEqual(50_000f, restored.TerrainRenderDistance);
        var renderer = restored.ToDx11().Clone();
        Assert.IsTrue(renderer.UseClientRenderingRules);
        Assert.AreEqual(700f, renderer.WrathFarClip);
        Assert.IsTrue(renderer.DisableScreenGlow);
        Assert.AreEqual(restored.ClientRenderingQuality, renderer.ClientRenderingQuality);
        Assert.AreEqual(restored.TerrainMip, renderer.TerrainMip);
        Assert.AreEqual(restored.ParticleDensity, renderer.ParticleDensity);
        Assert.AreEqual(restored.EnvironmentDetail, renderer.EnvironmentDetail);
        Assert.AreEqual(restored.GroundEffectDensity, renderer.GroundEffectDensity);
        Assert.AreEqual(restored.GroundEffectDist, renderer.GroundEffectDist);
        Assert.AreEqual(restored.BaseMip, renderer.BaseMip);
        Assert.AreEqual(restored.ExtShadowQuality, renderer.ExtShadowQuality);
        Assert.AreEqual(restored.TextureFilteringMode, renderer.TextureFilteringMode);
        Assert.AreEqual(restored.WeatherDensity, renderer.WeatherDensity);
        Assert.AreEqual(restored.ComponentTextureLevel, renderer.ComponentTextureLevel);
        Assert.AreEqual(restored.Specular, renderer.Specular);
        Assert.AreEqual(restored.FfxDeath, renderer.FfxDeath);
        Assert.AreEqual(restored.ProjectedTextures, renderer.ProjectedTextures);
    }

    [TestMethod]
    public void ClientRenderingCVarRangesClampAndRejectNonFiniteValues()
    {
        var normalized = (new RenderingConfiguration
        {
            ClientRenderingQuality = 99, TerrainMip = -1,
            ParticleDensity = float.NaN, EnvironmentDetail = float.PositiveInfinity,
            GroundEffectDensity = 100, GroundEffectDist = 0, BaseMip = 9,
            ExtShadowQuality = -1, TextureFilteringMode = 10,
            WeatherDensity = 8, ComponentTextureLevel = 0
        }).Normalize();
        Assert.AreEqual(6, normalized.ClientRenderingQuality);
        Assert.AreEqual(0, normalized.TerrainMip);
        Assert.AreEqual(1f, normalized.ParticleDensity);
        Assert.AreEqual(1.5f, normalized.EnvironmentDetail);
        Assert.AreEqual(64, normalized.GroundEffectDensity);
        Assert.AreEqual(70, normalized.GroundEffectDist);
        Assert.AreEqual(1, normalized.BaseMip);
        Assert.AreEqual(0, normalized.ExtShadowQuality);
        Assert.AreEqual(5, normalized.TextureFilteringMode);
        Assert.AreEqual(3, normalized.WeatherDensity);
        Assert.AreEqual(8, normalized.ComponentTextureLevel);
    }

    [TestMethod]
    public void SettingsWindowClientRenderingTabBindsActiveCVarsAndDisablesUnsupportedOptions()
    {
        var original = new EditorSettingsSnapshot
        {
            Rendering = new RenderingConfiguration { UseClientRenderingRules = false }
        };
        var vm = new ClientSettingsViewModel(original);
        var window = new SettingsWindow { DataContext = vm };
        try
        {
            window.Styles.Add(new FluentTheme());
            window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 1;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tab = window.FindControl<TabItem>("ClientRenderingTab")!;
            Assert.AreEqual("Client Rendering", tab.Header);
            tab.IsSelected = true;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var farclip = window.FindControl<Slider>("ClientFarClipSlider")!;
            var glow = window.FindControl<CheckBox>("ClientGlowCheckBox")!;
            Assert.IsTrue(farclip.IsEffectivelyEnabled);
            Assert.IsFalse(window.GetLogicalDescendants().OfType<ToggleSwitch>().Any());
            Assert.AreEqual(1277d, farclip.Value);
            farclip.Value = 700;
            Assert.AreEqual(700f, vm.WrathFarClip);
            Assert.IsFalse(vm.ApplyTo(original).Rendering.UseClientRenderingRules);
            Assert.IsTrue(glow.IsEffectivelyEnabled);
            glow.IsChecked = false;
            Assert.IsTrue(vm.DisableScreenGlow);

            Assert.IsFalse(window.FindControl<Slider>("ClientQualitySlider")!.IsEffectivelyEnabled);
            Assert.IsFalse(window.FindControl<Slider>("ClientTerrainMipSlider")!.IsEffectivelyEnabled);
            Assert.IsFalse(window.FindControl<Slider>("ClientParticleDensitySlider")!.IsEffectivelyEnabled);
            Assert.IsFalse(window.FindControl<Slider>("ClientEnvironmentDetailSlider")!.IsEffectivelyEnabled);
            Assert.IsFalse(window.FindControl<Slider>("ClientGroundEffectDensitySlider")!.IsEffectivelyEnabled);
            Assert.IsFalse(window.FindControl<Slider>("ClientGroundEffectDistSlider")!.IsEffectivelyEnabled);
            Assert.IsFalse(window.FindControl<Slider>("ClientBaseMipSlider")!.IsEffectivelyEnabled);
            Assert.IsFalse(window.FindControl<Slider>("ClientExtShadowQualitySlider")!.IsEffectivelyEnabled);
            Assert.IsFalse(window.FindControl<Slider>("ClientTextureFilteringModeSlider")!.IsEffectivelyEnabled);
            Assert.IsFalse(window.FindControl<Slider>("ClientWeatherDensitySlider")!.IsEffectivelyEnabled);
            Assert.IsFalse(window.FindControl<Slider>("ClientComponentTextureLevelSlider")!.IsEffectivelyEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [TestMethod]
    public void ViewportFogTogglePersistsAndSynchronizesIndependentlyOfRenderingMode()
    {
        var session = new EditorSession(new MemorySettingsStore(new EditorSettingsSnapshot()));
        using var vm = new Editor3DViewModel(session);
        var view = new Editor3DView { DataContext = vm };
        var toggle = view.FindControl<ToggleButton>("ViewportFogToggle")!;
        Assert.IsFalse(toggle.IsChecked);
        Assert.IsFalse(toggle.IsEnabled);
        Assert.IsFalse(vm.RenderFog);
        Assert.IsFalse(session.Current.Rendering.RenderFog);
        Assert.IsTrue(session.Current.Rendering.UseClientRenderingRules);
        var restored = JsonSerializer.Deserialize<PersistedRenderingSettings>(
            JsonSerializer.Serialize(PersistedRenderingSettings.From(session.Current.Rendering)))!.ToModel();
        Assert.IsFalse(restored.ToDx11().Clone().RenderFog);
        Assert.IsFalse(JsonSerializer.Deserialize<PersistedRenderingSettings>("{}")!.ToModel().RenderFog);
        Assert.IsTrue(WorldViewportSettings.Resolve(restored.ToDx11()).RenderFog);

        vm.UseClientRenderingRules = false;
        Assert.IsFalse(vm.RenderFog);
        Assert.IsTrue(toggle.IsEnabled);
        toggle.IsChecked = true;
        Assert.IsTrue(vm.RenderFog);
        Assert.IsTrue(session.Current.Rendering.RenderFog);
        Assert.IsTrue(toggle.IsChecked);
        session.UpdateRendering(session.Current.Rendering with { RenderFog = false });
        Assert.IsFalse(toggle.IsChecked);
        var modeToggle = view.FindControl<ToggleSwitch>("ViewportRenderingModeToggle")!;
        Assert.IsNotNull(modeToggle);
        // Flyout content receives its DataContext when opened. Exercise the
        // existing mode switch through its view model without attaching DX11.
        vm.UseClientRenderingRules = true;
        Assert.IsTrue(session.Current.Rendering.UseClientRenderingRules);
        Assert.IsFalse(toggle.IsChecked);
        Assert.IsFalse(toggle.IsEnabled);
        Assert.IsTrue(WorldViewportSettings.Resolve(session.Current.Rendering.ToDx11()).RenderFog);
        view.DataContext = null;
    }

    [TestMethod]
    public void ViewportGlowOverrideCannotOverwriteTheClientGameSetting()
    {
        var original = new EditorSettingsSnapshot();
        var session = new EditorSession(new MemorySettingsStore(original));
        using var viewport = new Editor3DViewModel(session);
        viewport.DisableScreenGlow = true;
        Assert.IsTrue(session.Current.Rendering.EditorDisableScreenGlow);
        Assert.IsFalse(session.Current.Rendering.DisableScreenGlow);
        var client = new ClientSettingsViewModel(session.Current) { FullScreenGlow = false };
        session.Apply(client.ApplyTo(session.Current), save: false);
        Assert.IsTrue(session.Current.Rendering.DisableScreenGlow);
        client.FullScreenGlow = true;
        session.Apply(client.ApplyTo(session.Current), save: false);
        Assert.IsFalse(session.Current.Rendering.DisableScreenGlow);
        Assert.IsTrue(viewport.DisableScreenGlow);
        var restored = JsonSerializer.Deserialize<PersistedRenderingSettings>(
            JsonSerializer.Serialize(PersistedRenderingSettings.From(session.Current.Rendering)))!.ToModel();
        Assert.IsTrue(restored.ToDx11().Clone().EditorDisableScreenGlow);
        Assert.IsFalse(WorldViewportSettings.Resolve(restored.ToDx11()).DisableScreenGlow);
        viewport.UseClientRenderingRules = false;
        Assert.IsTrue(WorldViewportSettings.Resolve(session.Current.Rendering.ToDx11()).DisableScreenGlow);
    }
}
