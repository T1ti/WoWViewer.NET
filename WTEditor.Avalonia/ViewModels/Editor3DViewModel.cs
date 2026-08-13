using System.Diagnostics;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTEditor.Application;
using WTEditor.Application.Models;

namespace WTEditor.Avalonia.ViewModels;

public partial class Editor3DViewModel : ViewModelBase, IDisposable
{
    private const int PerformanceHistoryCapacity = 180;
    private readonly EditorSession _session;
    private readonly Queue<FrameProfileSnapshot> _performanceSamples = [];
    private readonly Stopwatch _performancePublishTimer = Stopwatch.StartNew();
    private readonly Process _currentProcess = Process.GetCurrentProcess();
    private TimeSpan _lastProcessCpuTime;
    private long _lastAllocatedBytes;
    private bool _synchronizingRenderingSettings;

    public event EventHandler<ClientConfiguration>? ClientConfigurationChanged;
    public event EventHandler<RenderingConfiguration>? RenderingConfigurationChanged;
    public event EventHandler<KeyboardLayoutMode>? KeyboardLayoutChanged;

    public ClientConfiguration ClientConfiguration => _session.Current.Client;
    public RenderingConfiguration RenderingConfiguration => _session.Current.Rendering with
    {
        RenderADT = RenderTerrain,
        RenderWMO = RenderWorldModels,
        RenderM2 = RenderDoodads
    };
    public KeyboardLayoutMode KeyboardLayout => _session.Current.KeyboardLayout;
    public bool HasInitialCameraPosition => _session.Current.Camera != null;
    public Vector3 InitialCameraPosition => _session.Current.Camera?.Position ?? Vector3.Zero;
    public bool HasInitialCameraDirection => _session.Current.Camera != null;
    public Vector3 InitialCameraDirection => _session.Current.Camera?.Direction ?? Vector3.Zero;

    [ObservableProperty] private double _fps;
    [ObservableProperty] private double _frameTime;
    [ObservableProperty] private Vector3 _cameraPosition;
    [ObservableProperty] private Vector3 _cameraDirection;
    [ObservableProperty] private int _drawCalls;
    [ObservableProperty] private int _vertexCount;
    [ObservableProperty] private float _moveSpeed;
    [ObservableProperty] private float _mouseSensitivity;
    [ObservableProperty] private RendererLifecycleState _rendererState = RendererLifecycleState.Detached;
    [ObservableProperty] private string _rendererStatusMessage = "Renderer not attached.";
    [ObservableProperty] private string? _rendererError;
    [ObservableProperty] private bool _isRendererStatusVisible = true;
    [ObservableProperty] private bool _isMetricsPanelVisible;
    [ObservableProperty] private bool _renderTerrain;
    [ObservableProperty] private bool _renderWorldModels;
    [ObservableProperty] private bool _renderDoodads;
    [ObservableProperty] private bool _isProfilingPaused;
    [ObservableProperty] private IReadOnlyList<FrameProfileSnapshot> _performanceHistory = Array.Empty<FrameProfileSnapshot>();
    [ObservableProperty] private IReadOnlyList<FrameTimingStep> _currentFrameSteps = Array.Empty<FrameTimingStep>();
    [ObservableProperty] private double _profileCpuMilliseconds;
    [ObservableProperty] private double? _profileGpuMilliseconds;
    [ObservableProperty] private double _profileFrameMilliseconds;
    [ObservableProperty] private string _profileBottleneck = "Waiting for timing samples";
    [ObservableProperty] private int _profilePendingAssets;
    [ObservableProperty] private int _profileUploadedResources;
    [ObservableProperty] private CullingMetrics _profileCulling = new(0, 0, 0, 0, 0, 0);
    [ObservableProperty] private double _profileProcessCpuPercent;
    [ObservableProperty] private double _profileManagedMemoryMegabytes;
    [ObservableProperty] private double _profileWorkingSetMegabytes;
    [ObservableProperty] private double _profileAllocationMegabytesPerSecond;
    [ObservableProperty] private int _profileGen0Collections;

    // Viewport input state. This remains view-facing state while editor/session
    // configuration is owned centrally by EditorSession.
    [ObservableProperty] private bool _forward;
    [ObservableProperty] private bool _backward;
    [ObservableProperty] private bool _left;
    [ObservableProperty] private bool _right;
    [ObservableProperty] private bool _up;
    [ObservableProperty] private bool _down;
    [ObservableProperty] private bool _shift;
    [ObservableProperty] private bool _ctrl;
    [ObservableProperty] private bool _space;
    [ObservableProperty] private bool _leftMouseDown;
    [ObservableProperty] private bool _rightMouseDown;
    [ObservableProperty] private float _mouseWheel;
    [ObservableProperty] private Vector2 _mousePosition;

    public Editor3DViewModel(EditorSession session)
    {
        _session = session;
        _moveSpeed = session.Current.Rendering.MovementSpeed;
        _mouseSensitivity = session.Current.Rendering.MouseSensitivity;
        _renderTerrain = session.Current.Rendering.RenderADT;
        _renderWorldModels = session.Current.Rendering.RenderWMO;
        _renderDoodads = session.Current.Rendering.RenderM2;
        _cameraPosition = session.Current.Camera?.Position ?? Vector3.Zero;
        _cameraDirection = session.Current.Camera?.Direction ?? Vector3.Zero;
        _lastProcessCpuTime = _currentProcess.TotalProcessorTime;
        _lastAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);

        session.ClientConfigurationChanged += OnClientConfigurationChanged;
        session.RenderingConfigurationChanged += OnRenderingConfigurationChanged;
        session.KeyboardLayoutChanged += OnKeyboardLayoutChanged;
    }

    partial void OnMoveSpeedChanged(float value)
    {
        if (_synchronizingRenderingSettings)
            return;

        _session.UpdateRendering(RenderingConfiguration with { MovementSpeed = value });
    }

    partial void OnMouseSensitivityChanged(float value)
    {
        if (_synchronizingRenderingSettings)
            return;

        _session.UpdateRendering(RenderingConfiguration with { MouseSensitivity = value });
    }

    partial void OnRenderTerrainChanged(bool value) => PublishViewportRenderingConfiguration();
    partial void OnRenderWorldModelsChanged(bool value) => PublishViewportRenderingConfiguration();
    partial void OnRenderDoodadsChanged(bool value) => PublishViewportRenderingConfiguration();

    private void PublishViewportRenderingConfiguration()
    {
        if (_synchronizingRenderingSettings)
            return;

        RenderingConfigurationChanged?.Invoke(this, RenderingConfiguration);
    }

    public void UpdateTelemetry(ViewportTelemetry telemetry)
    {
        Fps = telemetry.FramesPerSecond;
        FrameTime = telemetry.FrameTimeMilliseconds;
        CameraPosition = telemetry.CameraPosition;
        CameraDirection = telemetry.CameraDirection;
        DrawCalls = telemetry.DrawCalls;
        VertexCount = telemetry.VertexCount;
        _session.UpdateCamera(telemetry.CameraPosition, telemetry.CameraDirection);
    }

    public void UpdateRendererStatus(RendererStatus status)
    {
        RendererState = status.State;
        RendererStatusMessage = status.Message;
        RendererError = status.Error;
        IsRendererStatusVisible = status.State != RendererLifecycleState.Ready || status.HasError;
    }

    public void UpdatePerformanceProfile(FrameProfileSnapshot snapshot)
    {
        if (IsProfilingPaused)
            return;

        _performanceSamples.Enqueue(snapshot);
        while (_performanceSamples.Count > PerformanceHistoryCapacity)
            _performanceSamples.Dequeue();

        // Publishing the immutable graph collection at 10 Hz avoids forcing a full
        // Avalonia layout/render pass for every engine frame.
        if (_performancePublishTimer.ElapsedMilliseconds < 100)
            return;

        var sampleSeconds = Math.Max(0.001d, _performancePublishTimer.Elapsed.TotalSeconds);
        _performancePublishTimer.Restart();
        PerformanceHistory = _performanceSamples.ToArray();
        CurrentFrameSteps = snapshot.Steps
            .Where(step => step.Name != "Resource uploads (GPU)" || step.DurationMilliseconds >= 0.001d)
            .ToArray();
        ProfileCpuMilliseconds = snapshot.CpuFrameMilliseconds;
        ProfileGpuMilliseconds = snapshot.GpuFrameMilliseconds;
        ProfileFrameMilliseconds = snapshot.EngineFrameMilliseconds;
        ProfilePendingAssets = snapshot.PendingAssetOperations;
        ProfileUploadedResources = snapshot.UploadedResources;
        ProfileCulling = snapshot.Culling;

        _currentProcess.Refresh();
        var processCpuTime = _currentProcess.TotalProcessorTime;
        ProfileProcessCpuPercent = Math.Clamp(
            (processCpuTime - _lastProcessCpuTime).TotalSeconds /
            sampleSeconds /
            Environment.ProcessorCount * 100d,
            0d,
            100d);
        _lastProcessCpuTime = processCpuTime;

        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
        ProfileAllocationMegabytesPerSecond = Math.Max(
            0d,
            (allocatedBytes - _lastAllocatedBytes) / sampleSeconds / 1_048_576d);
        _lastAllocatedBytes = allocatedBytes;
        ProfileManagedMemoryMegabytes = GC.GetTotalMemory(forceFullCollection: false) / 1_048_576d;
        ProfileWorkingSetMegabytes = _currentProcess.WorkingSet64 / 1_048_576d;
        ProfileGen0Collections = GC.CollectionCount(0);
        ProfileBottleneck = PerformanceAnalyzer.ClassifyRecent(PerformanceHistory) switch
        {
            PerformanceBottleneck.Cpu => "CPU bound",
            PerformanceBottleneck.Gpu => "GPU bound",
            PerformanceBottleneck.Balanced => "CPU / GPU balanced",
            _ => "Collecting GPU timing"
        };
    }

    [RelayCommand]
    private void ClearPerformanceHistory()
    {
        _performanceSamples.Clear();
        PerformanceHistory = Array.Empty<FrameProfileSnapshot>();
        CurrentFrameSteps = Array.Empty<FrameTimingStep>();
        ProfileCpuMilliseconds = 0;
        ProfileGpuMilliseconds = null;
        ProfileFrameMilliseconds = 0;
        ProfilePendingAssets = 0;
        ProfileUploadedResources = 0;
        ProfileCulling = new CullingMetrics(0, 0, 0, 0, 0, 0);
        ProfileProcessCpuPercent = 0;
        ProfileManagedMemoryMegabytes = 0;
        ProfileWorkingSetMegabytes = 0;
        ProfileAllocationMegabytesPerSecond = 0;
        ProfileGen0Collections = 0;
        ProfileBottleneck = "Waiting for timing samples";
    }

    [RelayCommand]
    private void CloseMetricsPanel() => IsMetricsPanelVisible = false;

    private void OnClientConfigurationChanged(object? sender, ClientConfiguration configuration) =>
        ClientConfigurationChanged?.Invoke(this, configuration);

    private void OnRenderingConfigurationChanged(object? sender, RenderingConfiguration configuration)
    {
        _synchronizingRenderingSettings = true;
        try
        {
            MoveSpeed = configuration.MovementSpeed;
            MouseSensitivity = configuration.MouseSensitivity;
        }
        finally
        {
            _synchronizingRenderingSettings = false;
        }

        RenderingConfigurationChanged?.Invoke(this, RenderingConfiguration);
    }

    private void OnKeyboardLayoutChanged(object? sender, KeyboardLayoutMode layout) =>
        KeyboardLayoutChanged?.Invoke(this, layout);

    public void Dispose()
    {
        _session.ClientConfigurationChanged -= OnClientConfigurationChanged;
        _session.RenderingConfigurationChanged -= OnRenderingConfigurationChanged;
        _session.KeyboardLayoutChanged -= OnKeyboardLayoutChanged;
        _currentProcess.Dispose();
    }
}
