using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Hexa.NET.ImGui;
using Silk.NET.Windowing;
using WoWRenderLib;
using WTEditor.Rendering;
using WTEditor.SilkRenderer.WPF.Common;
using WTEditor.SilkRenderer.WPF.OpenGL;

namespace WTEditor;

public partial class EditorView : UserControl
{
    private WowClientConfig _wowConfig;

    private WowViewerEngine wowViewerEngine;

    private WPFImGuiBackend wpfImGuiBackend;

    private bool _controlLoaded = false;

    // private InputFrame inputFrame;

    bool _frameUpdated = false;


    private bool _hasFocus = false;
    private HashSet<Key> _pressedKeys = new HashSet<Key>();

    // private Point? _mouseDownPosition;
    // private bool _wasLeftMouseDown = false;

    private readonly DirectKeyboardState directKeyboard = new();
    private DirectMouseState directMouse;
    private bool hasFocus;

    public EditorView()
    {
        InitializeComponent();

        Preview.Setting = new SilkRenderer.WPF.OpenGL.Settings()
        {
            MajorVersion = 4,
            MinorVersion = 5,
            GraphicsProfile = ContextProfile.Compatability // TODO check if it should use core
        };

        Preview.MouseDown += OnMouseDown;
        Preview.MouseUp += OnMouseUp;

        Preview.Ready += Game_Ready;
        Preview.Render += Game_Render;
        Preview.UpdateFrame += Game_UpdateFrame;
        Preview.SizeChanged += OnSizeChanged;

        Preview.Loaded += OnLoad;

        Preview.MouseEnter += (s, e) => Keyboard.Focus(Preview);

        Preview.GotKeyboardFocus += (s, e) =>
        {
            _hasFocus = true;
            wowViewerEngine.SetHasFocus(_hasFocus);
        };

        Preview.LostKeyboardFocus += (s, e) =>
        {
            _hasFocus = false;
            _pressedKeys.Clear();
            wowViewerEngine.SetHasFocus(_hasFocus);
        };

        Preview.Start();
    }

    private void OnLoad(object sender, RoutedEventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this);
        
        if (source != null)
        {
            directMouse = new DirectMouseState(source.Handle);
            directMouse.MouseDelta += OnDirectMouseDelta;
            directMouse.MouseButtonChanged += OnDirectMouseButtonChanged;
        
            UpdateControlOffset();
        }
        
        directKeyboard.AddKeys(
            Key.W, Key.A, Key.S, Key.D,
            Key.Up, Key.Down,
            Key.LeftShift, Key.RightShift,
            Key.R, Key.Space
        );
        
        directKeyboard.KeyStateChanged += OnDirectKeyStateChanged;


        wowViewerEngine.Resize((uint)ActualWidth, (uint)ActualHeight);
        wpfImGuiBackend?.Resize((int)ActualWidth, (int)ActualHeight);

        _controlLoaded = true;
    }

    // TODO bug : this is never called
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        wpfImGuiBackend?.Resize((int)e.NewSize.Width, (int)e.NewSize.Height);
        UpdateControlOffset();

        wowViewerEngine.Resize((uint)e.NewSize.Width, (uint)e.NewSize.Height);
    }

    // mouse moved
    private void OnDirectMouseDelta(float deltaX, float deltaY)
    {
        if (!_hasFocus /*|| activeCamera == null*/)
            return;
        // UpdateMovementDirection();
    }

    private void OnDirectMouseButtonChanged(DirectMouseState.MouseButton button, bool isDown)
    {
        if (!_hasFocus)
            return;

        // Update ImGui mouse button state
        if (wpfImGuiBackend._controller != null)
        {
            if (button == DirectMouseState.MouseButton.Left)
                wpfImGuiBackend._controller.SetMouseButton(0, isDown);
            else if (button == DirectMouseState.MouseButton.Right)
                wpfImGuiBackend._controller.SetMouseButton(1, isDown);
            else if (button == DirectMouseState.MouseButton.Middle)
                wpfImGuiBackend._controller.SetMouseButton(2, isDown);
        }

        if (button == DirectMouseState.MouseButton.Right)
        {
            if (isDown)
            {
                Mouse.Capture(Preview);
            }
            else
            {
                Mouse.Capture(null);
            }
        }
    }

    private void OnDirectKeyStateChanged(Key key, bool isKeyDown)
    {
        if (!_hasFocus)
            return;

        if (wpfImGuiBackend._controller != null)
        {
            var imguiKey = ImGuiController.ConvertWpfKeyToImGui(key);
            if (imguiKey != ImGuiKey.None)
            {
                wpfImGuiBackend._controller.SetKeyDown(imguiKey, isKeyDown);
            }

            if (key == Key.LeftCtrl || key == Key.RightCtrl)
                wpfImGuiBackend._controller.SetControlKey(isKeyDown);
            if (key == Key.LeftShift || key == Key.RightShift)
                wpfImGuiBackend._controller.SetShiftKey(isKeyDown);
            if (key == Key.LeftAlt || key == Key.RightAlt)
                wpfImGuiBackend._controller.SetAltKey(isKeyDown);
        }

        if (isKeyDown)
        {
            _pressedKeys.Add(key);
        }
        else
        {
            _pressedKeys.Remove(key);
        }

        // UpdateMovementDirection();
    }

    private void Game_Ready()
    {
        // gl = window.CreateOpenGL(); // Unlike silk window, gl is already ready here

        wpfImGuiBackend = new WPFImGuiBackend(Preview);

        wowViewerEngine = new WowViewerEngine(_wowConfig, wpfImGuiBackend);

        var size = new Silk.NET.Maths.Vector2D<int>((int)ActualWidth, (int)ActualHeight);
        wowViewerEngine.Initialize(RenderContext.GL, size);

        wowViewerEngine.Resize((uint)size.X, (uint)size.Y);
    }


    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Only used to ensure focus, actual button handling is done by direct polling
        Keyboard.Focus(Preview);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        // Handled by direct polling
    }

    private void Game_Render(TimeSpan obj)
    {
        // if (!hasFocus)
        //     return;

        // this event fires before first update
        if (!_frameUpdated)
            return;


        double delta = obj.TotalMilliseconds / 1000;

        wowViewerEngine.Render(delta);

        _frameUpdated = false;
    }

    private void Game_UpdateFrame(object arg1, TimeSpan arg2)
    {
        // hackfix : call every tick for now
        // doesn't get called when moving window or changing layout
        UpdateControlOffset();

        // Poll input state directly for immediate response (only when focused)
        if (_hasFocus)
        {
            directKeyboard.Update();
            directMouse?.Update();

            // Update ImGui mouse position
            if (wpfImGuiBackend._controller != null && directMouse != null)
            {
                var mousePos = directMouse.CurrentPosition;
                wpfImGuiBackend._controller.SetMousePosition((float)mousePos.X, (float)mousePos.Y);
            }
        }

        // Note : directMouse.CurrentPosition is in control-relative coordinates

        // reset frame inputs
        var inputFrame = new InputFrame
            {
                MousePosition = new Vector2((float)directMouse.CurrentPosition.X, (float)directMouse.CurrentPosition.Y),
                LeftMouseDown = directMouse._wasLeftButtonDown,
                RightMouseDown = directMouse._wasRightButtonDown,
                KeysDown = new HashSet<Silk.NET.Input.Key>(),
                MouseWheel = 0f // TODO
        };

        // build silk keys map from system inputs.
        // TODO this doesn't handle Azerty keyboard
        foreach (var key in _pressedKeys)
        {
            if (key == Key.W)
                inputFrame.KeysDown.Add(Silk.NET.Input.Key.W);
            else if (key == Key.Z)
                inputFrame.KeysDown.Add(Silk.NET.Input.Key.Z);
            else if (key == Key.A)
                inputFrame.KeysDown.Add(Silk.NET.Input.Key.A);
            else if (key == Key.S)
                inputFrame.KeysDown.Add(Silk.NET.Input.Key.S);
            else if (key == Key.D)
                inputFrame.KeysDown.Add(Silk.NET.Input.Key.D);
            else if (key == Key.Q)
                inputFrame.KeysDown.Add(Silk.NET.Input.Key.Q);
            else if (key == Key.E)
                inputFrame.KeysDown.Add(Silk.NET.Input.Key.E);
            else if (key == Key.Up)
                inputFrame.KeysDown.Add(Silk.NET.Input.Key.Up);
            else if (key == Key.Down)
                inputFrame.KeysDown.Add(Silk.NET.Input.Key.Down);
            else if (key == Key.LeftShift || key == Key.RightShift)
                inputFrame.KeysDown.Add(Silk.NET.Input.Key.ShiftLeft);
            else if (key == Key.R)
                inputFrame.KeysDown.Add(Silk.NET.Input.Key.R);
            else if (key == Key.Space)
                inputFrame.KeysDown.Add(Silk.NET.Input.Key.Space);
        }

        double delta = arg2.TotalMilliseconds / 1000;

        wowViewerEngine.Update(delta, inputFrame);

        _frameUpdated = true;
    }

    // This needs to be ran where DPI, preview scree or window screen are moved
    // easier to just run it every frames for now..
    private void UpdateControlOffset()
    {
        if (directMouse == null || Preview == null)
            return;

        try
        {
            var source = (HwndSource)PresentationSource.FromVisual(Preview);
            if (source != null)
            {
                // Get DPI scale factors
                var dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                var dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                Debug.WriteLine($"DPI Scale: {dpiScaleX}x, {dpiScaleY}y");

                // Set DPI scale in DirectMouseState
                directMouse.SetDpiScale(dpiScaleX, dpiScaleY);

                // Get the Preview control's screen position
                var previewScreenPos = Preview.PointToScreen(new Point(0, 0));
                Debug.WriteLine($"Preview screen position: {previewScreenPos.X}, {previewScreenPos.Y}");

                // Get the window's screen position (client area)
                var windowScreenPos = source.RootVisual.PointToScreen(new Point(0, 0));
                Debug.WriteLine($"Window screen position: {windowScreenPos.X}, {windowScreenPos.Y}");

                // Calculate offset in physical pixels (difference between control and window in screen coordinates)
                var controlTopLeft = new Point(
                    previewScreenPos.X - windowScreenPos.X,
                    previewScreenPos.Y - windowScreenPos.Y
                );

                directMouse.SetControlOffset(controlTopLeft);
                Debug.WriteLine($"Control offset set to: {controlTopLeft.X}, {controlTopLeft.Y} (physical pixels)");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating control offset: {ex.Message}");
        }
    }

}
