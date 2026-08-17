using System.Numerics;
using WTEditor.Application.Models;
using WTEditor.Application.Services;

namespace WTEditor.Application;

public sealed class EditorSession
{
    private readonly IEditorSettingsStore _settingsStore;
    private EditorSettingsSnapshot _current;
    private bool _hasCamera;
    private bool _cameraDirty;
    private Vector3 _cameraPosition;
    private Vector3 _cameraDirection;

    public EditorSettingsSnapshot Current
    {
        get
        {
            if (!_cameraDirty)
                return _current;

            _current = _current with
            {
                Camera = _hasCamera ? new CameraState(_cameraPosition, _cameraDirection) : null
            };
            _cameraDirty = false;
            return _current;
        }
    }

    public event EventHandler<ClientConfiguration>? ClientConfigurationChanged;
    public event EventHandler<RenderingConfiguration>? RenderingConfigurationChanged;
    public event EventHandler<KeyboardLayoutMode>? KeyboardLayoutChanged;

    public EditorSession(IEditorSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        _current = settingsStore.Load().Normalize();
        _hasCamera = _current.Camera != null;
        _cameraPosition = _current.Camera?.Position ?? Vector3.Zero;
        _cameraDirection = _current.Camera?.Direction ?? Vector3.Zero;
    }

    public void Apply(EditorSettingsSnapshot settings, bool save = false)
        => ApplyCore(settings, save, forceClientReload: false);

    public void Reload(EditorSettingsSnapshot settings, bool save = false)
        => ApplyCore(settings, save, forceClientReload: true);

    private void ApplyCore(
        EditorSettingsSnapshot settings,
        bool save,
        bool forceClientReload)
    {
        var next = settings.Normalize();
        var previous = Current;
        _current = next;
        _hasCamera = next.Camera != null;
        _cameraPosition = next.Camera?.Position ?? Vector3.Zero;
        _cameraDirection = next.Camera?.Direction ?? Vector3.Zero;
        _cameraDirty = false;

        if (forceClientReload || previous.Client != next.Client)
            ClientConfigurationChanged?.Invoke(this, next.Client);

        if (previous.Rendering != next.Rendering)
            RenderingConfigurationChanged?.Invoke(this, next.Rendering);

        if (previous.KeyboardLayout != next.KeyboardLayout)
            KeyboardLayoutChanged?.Invoke(this, next.KeyboardLayout);

        if (save)
            Save();
    }

    public void UpdateRendering(RenderingConfiguration rendering, bool save = false) =>
        Apply(Current with { Rendering = rendering }, save);

    public void UpdateCamera(Vector3 position, Vector3 direction)
    {
        if (_hasCamera && _cameraPosition == position && _cameraDirection == direction)
            return;

        _hasCamera = true;
        _cameraPosition = position;
        _cameraDirection = direction;
        _cameraDirty = true;
    }

    public void UpdateWindow(WindowPlacement placement, bool save = false)
    {
        _current = Current with { Window = placement };
        if (save)
            Save();
    }

    public void Save() => _settingsStore.Save(Current.Normalize());
}
