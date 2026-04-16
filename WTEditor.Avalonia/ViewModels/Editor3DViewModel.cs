using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WTEditor.Avalonia.ViewModels
{
    public partial class Editor3DViewModel : ViewModelBase
    {
        // overlay display data for the control
        [ObservableProperty]
        private double _fps;
        [ObservableProperty]
        private double _frameTime;
        [ObservableProperty]
        private Vector3 _cameraPosition;
        [ObservableProperty]
        private int _drawCalls;
        [ObservableProperty]
        private int _vertexCount;

        [ObservableProperty]
        private float _moveSpeed = 5f;
        [ObservableProperty]
        private float _mouseSensitivity = 0.0025f;

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

    }
}
