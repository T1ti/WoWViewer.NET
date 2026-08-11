using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using WoWRenderLib.DX11;

namespace WTEditor.Avalonia.ViewModels
{
    public partial class Editor3DViewModel : ViewModelBase
    {
        public event EventHandler<WowClientConfig>? ClientConfigChanged;
        public event EventHandler<RendererSettings>? RendererSettingsChanged;
        public event EventHandler<string>? KeyboardLayoutChanged;

        public WowClientConfig ClientConfig { get; private set; } = new()
        {
            wowDir = @"C:\Program Files (x86)\World of Warcraft",
            wowProduct = "wow_classic_era"
        };

        // overlay display data for the control
        [ObservableProperty]
        private double _fps;
        [ObservableProperty]
        private double _frameTime;
        [ObservableProperty]
        private Vector3 _cameraPosition;
        [ObservableProperty]
        private Vector3 _cameraDirection;

        public bool HasInitialCameraPosition { get; private set; }
        public Vector3 InitialCameraPosition { get; private set; }
        public bool HasInitialCameraDirection { get; private set; }
        public Vector3 InitialCameraDirection { get; private set; }

        public void SetInitialCameraPosition(Vector3? position)
        {
            HasInitialCameraPosition = position.HasValue;
            InitialCameraPosition = position ?? Vector3.Zero;
            CameraPosition = InitialCameraPosition;
        }

        public void SetInitialCameraDirection(Vector3? direction)
        {
            HasInitialCameraDirection = direction.HasValue;
            InitialCameraDirection = direction ?? Vector3.Zero;
            CameraDirection = InitialCameraDirection;
        }
        [ObservableProperty]
        private int _drawCalls;
        [ObservableProperty]
        private int _vertexCount;

        [ObservableProperty]
        private float _moveSpeed = 150f;
        [ObservableProperty]
        private float _mouseSensitivity = 0.1f;

        partial void OnMoveSpeedChanged(float value)
        {
            RendererSettings.MovementSpeed = value;
        }

        partial void OnMouseSensitivityChanged(float value)
        {
            RendererSettings.MouseSensitivity = value;
        }

        public RendererSettings RendererSettings { get; private set; } = new();
        public string KeyboardLayout { get; private set; } = "Auto";

        // input states
        // keyboard
        [ObservableProperty] private bool _forward; // W or Z for qwerty/azerty
        [ObservableProperty] private bool _backward;
        [ObservableProperty] private bool _left;
        [ObservableProperty] private bool _right;
        [ObservableProperty] private bool _up;
        [ObservableProperty] private bool _down;
        [ObservableProperty] private bool _shift;
        [ObservableProperty] private bool _ctrl;
        [ObservableProperty] private bool _space;
        // mouse
        [ObservableProperty] private bool _leftMouseDown;
        [ObservableProperty] private bool _rightMouseDown;
        [ObservableProperty] private float _mouseWheel;
        [ObservableProperty] private Vector2 _mousePosition;

        public void SetClientConfig(WowClientConfig config)
        {
            ClientConfig = config;
            ClientConfigChanged?.Invoke(this, config);
        }

        public void SetRendererSettings(RendererSettings settings)
        {
            RendererSettings = settings.Clone();
            MoveSpeed = RendererSettings.MovementSpeed;
            MouseSensitivity = RendererSettings.MouseSensitivity;
            RendererSettingsChanged?.Invoke(this, RendererSettings);
        }

        public void SetKeyboardLayout(string layout)
        {
            KeyboardLayout = layout is "QWERTY" or "AZERTY" ? layout : "Auto";
            KeyboardLayoutChanged?.Invoke(this, KeyboardLayout);
        }

    }
}
