using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Application.Models;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Tests;

public sealed partial class EditorSettingsSmokeTests
{
    [TestMethod]
    public void RenderingModePersistsAndSwitchingPreservesBothSettingsBanks()
    {
        var original = new EditorSettingsSnapshot
        {
            Rendering = new RenderingConfiguration
            {
                TerrainRenderDistance = 100_000f, ModelRenderDistance = 60_000f,
                WrathFarClip = 1200f, WrathFarClipOverride = true,
                MinimumModelScreenSizePixels = 3f
            }
        };
        var vm = new ClientSettingsViewModel(original) { UseClientRenderingRules = true };
        var applied = vm.ApplyTo(original).Normalize();
        var restored = JsonSerializer.Deserialize<PersistedRenderingSettings>(
            JsonSerializer.Serialize(PersistedRenderingSettings.From(applied.Rendering)))!.ToModel();
        Assert.IsTrue(restored.UseClientRenderingRules);
        Assert.IsTrue(restored.ToDx11().Clone().UseClientRenderingRules);
        Assert.AreEqual(100_000f, restored.TerrainRenderDistance);
        Assert.AreEqual(60_000f, restored.ModelRenderDistance);
        Assert.AreEqual(1200f, restored.WrathFarClip);
        Assert.IsTrue(restored.WrathFarClipOverride);
        vm.UseClientRenderingRules = false;
        Assert.AreEqual(original.Rendering, vm.ApplyTo(original).Rendering);
        Assert.IsFalse(JsonSerializer.Deserialize<PersistedRenderingSettings>("{}")!.ToModel().UseClientRenderingRules);
    }
}
