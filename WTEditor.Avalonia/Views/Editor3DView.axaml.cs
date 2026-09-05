using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using WTEditor.Application.Models;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Views;

public partial class Editor3DView : UserControl
{
    public static readonly StyledProperty<ViewportRenderActivity> RenderActivityProperty =
        AvaloniaProperty.Register<Editor3DView, ViewportRenderActivity>(
            nameof(RenderActivity),
            ViewportRenderActivity.Foreground);

    private Editor3DViewModel? _subscribedViewModel;
    private MetricsWindow? _metricsWindow;
    // private bool _leftMouseDown = false;
    // private bool _rightMouseDown = false;
    // private Point _lastMousePos;

    [DllImport("user32.dll")]
    private static extern long GetKeyboardLayoutName(StringBuilder pwszKLID);

    private bool _AzertyInput = true; // AZERTY keyboard support
    private bool _DetectedAzertyInput;
    private Key _MoveForwardKey = Key.W; // rebindable hotkeys for azerty support
    private Key _MoveLeftKey = Key.A;
    private Key _MoveRightKey = Key.D;
    private Key _MoveBackwardKey = Key.S;
    private Key _MoveDownKey = Key.E;
    private Key _MoveUpKey = Key.Q;

    public Editor3DViewModel? ViewModel
    {
        get => DataContext as Editor3DViewModel;
        set => DataContext = value;
    }
    public Editor3DView()
    {
        InitializeComponent();

        StringBuilder name = new StringBuilder(9);

        GetKeyboardLayoutName(name);
        
        switch(name.ToString())
        {
            case "0000040C": // French (France) - AZERTY
            case "0000080C": // French (Belgium) - AZERTY
            case "00000C0C": // French (Switzerland) - AZERTY
            case "0000140C": // French (Luxembourg) - AZERTY
                _AzertyInput = true;
                break;
            default:
                _AzertyInput = false;
                break;
        }

        _DetectedAzertyInput = _AzertyInput;
        SetKeyboardMode(_AzertyInput);
    }

    public ViewportRenderActivity RenderActivity
    {
        get => GetValue(RenderActivityProperty);
        set => SetValue(RenderActivityProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.KeyboardLayoutChanged -= OnKeyboardLayoutChanged;
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnDataContextChanged(e);

        if (DataContext is Editor3DViewModel viewModel)
        {
            _subscribedViewModel = viewModel;
            viewModel.KeyboardLayoutChanged += OnKeyboardLayoutChanged;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyKeyboardLayout(viewModel.KeyboardLayout);
            UpdateMetricsWindow();
        }
        else
        {
            _subscribedViewModel = null;
        }
    }

    private void OnKeyboardLayoutChanged(object? sender, KeyboardLayoutMode layout) => ApplyKeyboardLayout(layout);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Editor3DViewModel.IsMetricsPanelVisible))
            UpdateMetricsWindow();
    }

    private void UpdateMetricsWindow()
    {
        var viewModel = ViewModel;
        if (viewModel?.IsMetricsPanelVisible == true && VisualRoot != null)
        {
            if (_metricsWindow != null)
                return;

            _metricsWindow = new MetricsWindow { DataContext = viewModel };
            _metricsWindow.Closed += OnMetricsWindowClosed;
            if (TopLevel.GetTopLevel(this) is Window owner)
                _metricsWindow.Show(owner);
            else
                _metricsWindow.Show();
        }
        else if (_metricsWindow != null)
        {
            var window = _metricsWindow;
            _metricsWindow = null;
            window.Closed -= OnMetricsWindowClosed;
            window.Close();
        }
    }

    private void OnMetricsWindowClosed(object? sender, EventArgs e)
    {
        if (_metricsWindow != sender)
            return;

        _metricsWindow = null;
        if (ViewModel != null)
            ViewModel.IsMetricsPanelVisible = false;
    }

    private void ApplyKeyboardLayout(KeyboardLayoutMode layout)
    {
        SetKeyboardMode(layout == KeyboardLayoutMode.Azerty ||
                        layout == KeyboardLayoutMode.Auto && _DetectedAzertyInput);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Focusable = true;
        Focus();
        UpdateMetricsWindow();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        var window = _metricsWindow;
        _metricsWindow = null;
        if (window != null)
        {
            window.Closed -= OnMetricsWindowClosed;
            window.Close();
        }

        base.OnDetachedFromVisualTree(e);
    }

    public void SetKeyboardMode(bool Azerty)
    {
        if (Azerty)
        {
            _MoveForwardKey = Key.Z;
            _MoveLeftKey = Key.Q;
            _MoveRightKey = Key.D;
            _MoveBackwardKey = Key.S;
            _MoveDownKey = Key.E;
            _MoveUpKey = Key.A;
        }
        else // qwerty
        {
            _MoveForwardKey = Key.W;
            _MoveLeftKey = Key.A;
            _MoveRightKey = Key.D;
            _MoveBackwardKey = Key.S;
            _MoveDownKey = Key.E;
            _MoveUpKey = Key.Q;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var viewModel = ViewModel;
        if (viewModel == null)
            return;

        var props = e.GetCurrentPoint(this).Properties;

        switch (props.PointerUpdateKind)
        {
            case PointerUpdateKind.LeftButtonPressed:
                viewModel.LeftMouseDown = true;
                break;

            case PointerUpdateKind.RightButtonPressed:
                viewModel.RightMouseDown = true;
                break;
        }

        var pos = e.GetPosition(this);
        viewModel.MousePosition = new System.Numerics.Vector2((float)pos.X, (float)pos.Y);

        e.Pointer.Capture(this);

        Focus();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel == null)
            return;

        var props = e.GetCurrentPoint(this).Properties;
        switch (props.PointerUpdateKind)
        {
            case PointerUpdateKind.LeftButtonReleased:
                viewModel.LeftMouseDown = false;
                break;

            case PointerUpdateKind.RightButtonReleased:
                viewModel.RightMouseDown = false;
                break;
        }

        // Only release capture if no buttons are still held
        if (!viewModel.LeftMouseDown && !viewModel.RightMouseDown)
        {
            e.Pointer.Capture(null);
        }

        base.OnPointerReleased(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (DataContext is not Editor3DViewModel vm)
            return;

        base.OnPointerMoved(e);

        // avalonia already gives coordinates relative to control

        var pos = e.GetPosition(this);
        vm.MousePosition = new System.Numerics.Vector2((float)pos.X, (float)pos.Y);
    }

    // keyboard
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;

        if (e.Key == _MoveForwardKey) vm.Forward = true;
        if (e.Key == _MoveBackwardKey) vm.Backward = true;
        if (e.Key == _MoveLeftKey) vm.Left = true;
        if (e.Key == _MoveRightKey) vm.Right = true;
        if (e.Key == _MoveDownKey) vm.Down = true;
        if (e.Key == _MoveUpKey) vm.Up = true;
        if (e.Key == Key.LeftShift || e.Key == Key.RightShift) vm.Shift = true;
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl) vm.Ctrl = true;
        if (e.Key == Key.Space) vm.Space = true;

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;

        if (e.Key == _MoveForwardKey) vm.Forward = false;
        if (e.Key == _MoveBackwardKey) vm.Backward = false;
        if (e.Key == _MoveLeftKey) vm.Left = false;
        if (e.Key == _MoveRightKey) vm.Right = false;
        if (e.Key == _MoveDownKey) vm.Down = false;
        if (e.Key == _MoveUpKey) vm.Up = false;
        if (e.Key == Key.LeftShift || e.Key == Key.RightShift) vm.Shift = false;
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl) vm.Ctrl = false;
        if (e.Key == Key.Space) vm.Space = false;

        base.OnKeyUp(e);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);

        var vm = ViewModel;
        if (vm == null) return;

        // reset inputs
        vm.Forward = false;
        vm.Backward = false;
        vm.Left = false;
        vm.Right = false;
        vm.Down = false;
        vm.Up = false;
        vm.Shift = false;
        vm.Ctrl = false;
        vm.Space = false;

        vm.LeftMouseDown = false;
        vm.RightMouseDown = false;
    }
}
