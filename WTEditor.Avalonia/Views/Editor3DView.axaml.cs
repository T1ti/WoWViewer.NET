using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Views;

public partial class Editor3DView : UserControl
{
    // private bool _leftMouseDown = false;
    // private bool _rightMouseDown = false;
    // private Point _lastMousePos;

    private bool _AzertyInput = true; // AZERTY keyboard support
    private Key _MoveForwardKey = Key.W; // rebindable hotkeys for azerty support
    private Key _MoveLeftKey = Key.A;
    private Key _MoveRightKey = Key.D;
    private Key _MoveBackwardKey = Key.S;
    private Key _MoveDownKey = Key.E;
    private Key _MoveUpKey = Key.Q;

    public Editor3DViewModel ViewModel
    {
        get => DataContext as Editor3DViewModel;
        set => DataContext = value;
    }
    public Editor3DView()
    {
        InitializeComponent();

        SetKeyboardMode(_AzertyInput);

        // look into RoutingStrategies.Tunnel to make events fire from parents if needed
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
        Focus();

        var props = e.GetCurrentPoint(this).Properties;

        switch (props.PointerUpdateKind)
        {
            case PointerUpdateKind.LeftButtonPressed:
                ViewModel.LeftMouseDown = true;
                break;

            case PointerUpdateKind.RightButtonPressed:
                ViewModel.RightMouseDown = true;
                break;
        }

        // if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        // {
        //     _rightMouseDown = true;
        // }
        // if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        // {
        //     _leftMouseDown = true;
        // }

        var pos = e.GetPosition(this);
        ViewModel.MousePosition = new System.Numerics.Vector2((float)pos.X, (float)pos.Y);

        e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {

        var props = e.GetCurrentPoint(this).Properties;

        // if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        // {
        //     _rightMouseDown = false;
        // }
        // if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        // {
        //     _leftMouseDown = false;
        // }
        // e.Pointer.Capture(null);

        switch (props.PointerUpdateKind)
        {
            case PointerUpdateKind.LeftButtonReleased:
                ViewModel.LeftMouseDown = false;
                break;

            case PointerUpdateKind.RightButtonReleased:
                ViewModel.RightMouseDown = false;
                break;
        }

        // Only release capture if no buttons are still held
        if (!ViewModel.LeftMouseDown && !ViewModel.RightMouseDown)
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

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {

        base.OnLostFocus(e);
    }
}