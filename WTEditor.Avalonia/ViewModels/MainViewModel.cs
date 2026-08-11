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
            ViewportVM.SetInitialCameraPosition(persisted.HasCameraPosition ? persisted.GetCameraPosition() : null);
            ViewportVM.SetInitialCameraDirection(persisted.HasCameraDirection ? persisted.GetCameraDirection() : null);
        }

        public void ApplyClientConfig(WowClientConfig config)
        {
            ClientConfig = config;
            ViewportVM.SetClientConfig(config);
            EditorSettingsStore.Save(
                ClientConfig,
                RendererSettings,
                KeyboardLayout,
                ViewportVM.CameraPosition,
                cameraDirection: ViewportVM.CameraDirection);
        }

        public void ApplyEditorSettings(WowClientConfig config, RendererSettings rendererSettings, string keyboardLayout)
        {
            ClientConfig = config;
            RendererSettings = rendererSettings.Clone();
            KeyboardLayout = keyboardLayout;
            ViewportVM.SetRendererSettings(RendererSettings);
            ViewportVM.SetKeyboardLayout(KeyboardLayout);
            ViewportVM.SetClientConfig(ClientConfig);
            EditorSettingsStore.Save(
                ClientConfig,
                RendererSettings,
                KeyboardLayout,
                ViewportVM.CameraPosition,
                cameraDirection: ViewportVM.CameraDirection);
        }

        public void SaveSettings()
        {
            EditorSettingsStore.Save(
                ClientConfig,
                RendererSettings,
                KeyboardLayout,
                ViewportVM.CameraPosition,
                cameraDirection: ViewportVM.CameraDirection);
        }

        public void SaveSettings(int windowX, int windowY, double windowWidth, double windowHeight)
        {
            EditorSettingsStore.Save(
                ClientConfig,
                RendererSettings,
                KeyboardLayout,
                ViewportVM.CameraPosition,
                windowX,
                windowY,
                windowWidth,
                windowHeight,
                cameraDirection: ViewportVM.CameraDirection);
        }

        public void SaveSettings(string windowState)
        {
            EditorSettingsStore.Save(
                ClientConfig,
                RendererSettings,
                KeyboardLayout,
                ViewportVM.CameraPosition,
                windowState: windowState,
                cameraDirection: ViewportVM.CameraDirection);
        }

        public void SaveSettings(int windowX, int windowY, double windowWidth, double windowHeight, string windowState)
        {
            EditorSettingsStore.Save(
                ClientConfig,
                RendererSettings,
                KeyboardLayout,
                ViewportVM.CameraPosition,
                windowX,
                windowY,
                windowWidth,
                windowHeight,
                windowState,
                cameraDirection: ViewportVM.CameraDirection);
        }
    }
}
