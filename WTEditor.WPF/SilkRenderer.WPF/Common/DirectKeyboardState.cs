using System.Runtime.InteropServices;
using System.Windows.Input;

namespace WTEditor.SilkRenderer.WPF.Common;

public class DirectKeyboardState
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int KEY_DOWN_MASK = 0x8000;

    private readonly Dictionary<Key, bool> _previousState = [];
    private readonly HashSet<Key> _trackedKeys = [];

    public event Action<Key, bool> KeyStateChanged;

    public void TrackKey(Key key)
    {
        _trackedKeys.Add(key);
    }

    public void Update()
    {
        foreach (var key in _trackedKeys)
        {
            int vk = KeyInterop.VirtualKeyFromKey(key);
            bool isDown = (GetAsyncKeyState(vk) & KEY_DOWN_MASK) != 0;

            if (!_previousState.TryGetValue(key, out bool wasDown))
            {
                wasDown = false;
            }

            if (isDown != wasDown)
            {
                _previousState[key] = isDown;
                KeyStateChanged?.Invoke(key, isDown);
            }
        }
    }

    public bool IsKeyDown(Key key)
    {
        int vk = KeyInterop.VirtualKeyFromKey(key);
        return (GetAsyncKeyState(vk) & KEY_DOWN_MASK) != 0;
    }

    public void AddKeys(params Key[] keys)
    {
        foreach (var key in keys)
        {
            TrackKey(key);
        }
    }
}
