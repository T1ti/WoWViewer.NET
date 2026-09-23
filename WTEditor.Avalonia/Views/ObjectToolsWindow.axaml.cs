using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using System.ComponentModel;
using System.Runtime.InteropServices;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Views;

public partial class ObjectToolsWindow : Window
{
    private ObjectEditingViewModel? _objectEditor;
    private double _expandedWidth = 850d;
    private PixelPoint _dragPointerOffset;
    private PixelPoint _dragStartPointer;
    private readonly DispatcherTimer _dragTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private bool _isDragging;
    private bool _hasMoved;

    public Action<PixelPoint>? DragMoved { get; set; }
    public Action<PixelPoint>? DockRequested { get; set; }

    public ObjectToolsWindow()
    {
        InitializeComponent();
        _dragTimer.Tick += OnDragTimerTick;
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) =>
        {
            if (_objectEditor?.IsBrowserVisible == true && WindowState == WindowState.Normal)
                _expandedWidth = Width;
        };
        Closed += (_, _) =>
        {
            CancelDrag();
            Unsubscribe();
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Unsubscribe();
        _objectEditor = (DataContext as MainViewModel)?.ObjectEditor;
        if (_objectEditor == null)
            return;
        _objectEditor.PropertyChanged += OnObjectEditorPropertyChanged;
        UpdateBrowserWidth();
    }

    private void OnObjectEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ObjectEditingViewModel.IsBrowserVisible))
            UpdateBrowserWidth();
    }

    private void UpdateBrowserWidth()
    {
        if (_objectEditor?.IsBrowserVisible == true)
        {
            MinWidth = 500d;
            Width = Math.Max(500d, _expandedWidth);
        }
        else
        {
            _expandedWidth = Math.Max(_expandedWidth, Width);
            MinWidth = 320d;
            Width = 320d;
        }
    }

    private void Unsubscribe()
    {
        if (_objectEditor != null)
            _objectEditor.PropertyChanged -= OnObjectEditorPropertyChanged;
        _objectEditor = null;
    }

    private void WindowDragHandle_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        BeginDrag(this.PointToScreen(e.GetPosition(this)));
        e.Handled = true;
    }

    public void BeginDrag(PixelPoint screenPointer, bool alreadyMoved = false)
    {
        _dragPointerOffset = new PixelPoint(
            screenPointer.X - Position.X,
            screenPointer.Y - Position.Y);
        _dragStartPointer = screenPointer;
        _hasMoved = alreadyMoved;
        _isDragging = true;
        _dragTimer.Start();
    }

    public void CancelDrag()
    {
        _isDragging = false;
        _dragTimer.Stop();
    }

    private void OnDragTimerTick(object? sender, EventArgs e)
    {
        if (!_isDragging || !OperatingSystem.IsWindows() || !GetCursorPos(out var cursor))
        {
            CancelDrag();
            return;
        }

        var pointer = new PixelPoint(cursor.X, cursor.Y);
        if (Math.Abs(pointer.X - _dragStartPointer.X) >= 6 ||
            Math.Abs(pointer.Y - _dragStartPointer.Y) >= 6)
            _hasMoved = true;
        if ((GetAsyncKeyState(LeftMouseButton) & 0x8000) == 0)
        {
            CancelDrag();
            if (_hasMoved)
            {
                Position = new PixelPoint(
                    pointer.X - _dragPointerOffset.X,
                    pointer.Y - _dragPointerOffset.Y);
                DockRequested?.Invoke(pointer);
            }
            return;
        }

        if (!_hasMoved)
            return;
        Position = new PixelPoint(
            pointer.X - _dragPointerOffset.X,
            pointer.Y - _dragPointerOffset.Y);
        DragMoved?.Invoke(pointer);
    }

    private const int LeftMouseButton = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
