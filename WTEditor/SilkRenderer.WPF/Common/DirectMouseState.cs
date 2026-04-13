using System.Runtime.InteropServices;
using System.Windows;

namespace WTEditor.SilkRenderer.WPF.Common;

public class DirectMouseState
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;
    private const int VK_MBUTTON = 0x04;
    private const int KEY_DOWN_MASK = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private Point? _lastPosition;
    private readonly IntPtr _hwnd;
    private bool _wasRightButtonDown;
    private bool _wasLeftButtonDown;
    private bool _wasMiddleButtonDown;

    private Point _controlOffset;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    public event Action<float, float> MouseDelta;
    public event Action<MouseButton, bool> MouseButtonChanged;

    public Point CurrentPosition { get; private set; }

    public enum MouseButton
    {
        Left,
        Right,
        Middle
    }

    public DirectMouseState(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    public void SetControlOffset(Point offset)
    {
        _controlOffset = offset;
    }

    public void SetDpiScale(double scaleX, double scaleY)
    {
        _dpiScaleX = scaleX;
        _dpiScaleY = scaleY;
    }

    public void Update()
    {
        bool isRightDown = (GetAsyncKeyState(VK_RBUTTON) & KEY_DOWN_MASK) != 0;
        bool isLeftDown = (GetAsyncKeyState(VK_LBUTTON) & KEY_DOWN_MASK) != 0;
        bool isMiddleDown = (GetAsyncKeyState(VK_MBUTTON) & KEY_DOWN_MASK) != 0;

        if (isRightDown != _wasRightButtonDown)
        {
            _wasRightButtonDown = isRightDown;
            MouseButtonChanged?.Invoke(MouseButton.Right, isRightDown);

            if (isRightDown)
                _lastPosition = null;
        }

        if (isLeftDown != _wasLeftButtonDown)
        {
            _wasLeftButtonDown = isLeftDown;
            MouseButtonChanged?.Invoke(MouseButton.Left, isLeftDown);
        }

        if (isMiddleDown != _wasMiddleButtonDown)
        {
            _wasMiddleButtonDown = isMiddleDown;
            MouseButtonChanged?.Invoke(MouseButton.Middle, isMiddleDown);
        }

        if (GetCursorPos(out POINT screenPoint))
        {
            POINT clientPoint = screenPoint;
            ScreenToClient(_hwnd, ref clientPoint);

            Point currentPos = new Point(
                (clientPoint.X - _controlOffset.X) / _dpiScaleX,
                (clientPoint.Y - _controlOffset.Y) / _dpiScaleY
            );

            CurrentPosition = currentPos;

            if (isRightDown)
            {
                if (_lastPosition.HasValue)
                {
                    float deltaX = (float)(currentPos.X - _lastPosition.Value.X);
                    float deltaY = (float)(currentPos.Y - _lastPosition.Value.Y);

                    if (deltaX != 0 || deltaY != 0)
                    {
                        MouseDelta?.Invoke(deltaX, deltaY);
                    }
                }

                _lastPosition = currentPos;
            }
            else
            {
                _lastPosition = null;
            }
        }
    }
}
