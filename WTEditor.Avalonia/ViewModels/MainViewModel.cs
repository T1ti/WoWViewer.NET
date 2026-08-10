using System;
using System.Collections.Generic;
using System.Text;
using WTEditor.Avalonia.Services;
using WoWRenderLib.DX11;

namespace WTEditor.Avalonia.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        public Editor3DViewModel ViewportVM { get; } = new();

        public WowClientConfig ClientConfig { get; private set; } = new()
        {
            wowDir = @"C:\Program Files (x86)\World of Warcraft",
            wowProduct = "wow_classic_era"
        };
        public RendererSettings RendererSettings { get; private set; } = new();
        public string KeyboardLayout { get; private set; } = "Auto";

        public MainViewModel()
        {
            var persisted = EditorSettingsStore.Load();
            ClientConfig = persisted.ToClientConfig();
            RendererSettings = persisted.Renderer ?? new RendererSettings();
            KeyboardLayout = persisted.KeyboardLayout is "QWERTY" or "AZERTY"
                ? persisted.KeyboardLayout
                : "Auto";

            ViewportVM.SetRendererSettings(RendererSettings);
            ViewportVM.SetKeyboardLayout(KeyboardLayout);
            ViewportVM.SetClientConfig(ClientConfig);
        }

        public void ApplyClientConfig(WowClientConfig config)
        {
            ClientConfig = config;
            ViewportVM.SetClientConfig(config);
            EditorSettingsStore.Save(ClientConfig, RendererSettings, KeyboardLayout);
        }

        public void ApplyEditorSettings(WowClientConfig config, RendererSettings rendererSettings, string keyboardLayout)
        {
            ClientConfig = config;
            RendererSettings = rendererSettings.Clone();
            KeyboardLayout = keyboardLayout;
            ViewportVM.SetRendererSettings(RendererSettings);
            ViewportVM.SetKeyboardLayout(KeyboardLayout);
            ViewportVM.SetClientConfig(ClientConfig);
            EditorSettingsStore.Save(ClientConfig, RendererSettings, KeyboardLayout);
        }
    }
}
