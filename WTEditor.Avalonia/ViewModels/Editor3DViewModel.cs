using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTEditor.Application;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WoWRenderLib.DX11.Editing;
using WTEditor.Avalonia.Rendering;
using WoWRenderLib.DX11;

namespace WTEditor.Avalonia.ViewModels;

public partial class Editor3DViewModel : ViewModelBase, IDisposable
{
    private const int PerformanceHistoryCapacity = 180;
    private static readonly TimeSpan CaptureWarmupDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CaptureSampleDuration = TimeSpan.FromSeconds(10);
    private readonly EditorSession _session;
    private readonly IProjectService? _projectService;
    private readonly Queue<FrameProfileSnapshot> _performanceSamples = [];
    private readonly Stopwatch _performancePublishTimer = Stopwatch.StartNew();
    private readonly Process _currentProcess = Process.GetCurrentProcess();
    private TimeSpan _lastProcessCpuTime;
    private long _lastAllocatedBytes;
    private bool _synchronizingRenderingSettings;
    private readonly List<FrameProfileSnapshot> _activeCaptureSamples = [];
    private FrameProfileSnapshot? _latestProfileSnapshot;
    private DateTimeOffset _captureSampleStartedAt;
    private DateTimeOffset _captureEndsAt;
    private PerformanceCaptureContext? _captureContext;
    private int _lastCaptureStatusSecond = -1;
    private bool _isAutomatedPerformanceCapture;

    public event EventHandler<ClientConfiguration>? ClientConfigurationChanged;
    public event EventHandler<RenderingConfiguration>? RenderingConfigurationChanged;
    public event EventHandler<KeyboardLayoutMode>? KeyboardLayoutChanged;
    public event EventHandler<string>? AutomatedPerformanceCaptureSaved;
    public event EventHandler<string>? AutomatedPerformanceCaptureFailed;
    public event EventHandler<ObjectTransform>? SelectedObjectTransformRequested;
    public event EventHandler<WmoPlacementSelection>? SelectedWmoPlacementRequested;
    public UndoService UndoService { get; }

    public ClientConfiguration ClientConfiguration => _session.Current.Client;
    public RenderingConfiguration RenderingConfiguration => _session.Current.Rendering with
    {
        RenderADT = RenderTerrain,
        RenderWMO = RenderWorldModels,
        RenderM2 = RenderDoodads,
        EnableWmoPortalCulling = WmoPortalCullingEnabled,
        MinimumModelScreenSizePixels = MinimumModelScreenSizePixels,
        TerrainLodTransitionPixels = TerrainLodTransitionPixels,
        TerrainRenderDistance = TerrainRenderDistance,
        ModelRenderDistance = ModelRenderDistance,
        TileLoadingDistance = TileLoadingDistance,
        ShowBoundingBoxes = ShowBoundingBoxes,
        ShowBoundingSpheres = ShowBoundingSpheres,
        ShowTerrainGrid = ShowTerrainGrid,
        ShowTerrainWireframe = ShowTerrainWireframe
    };
    public KeyboardLayoutMode KeyboardLayout => _session.Current.KeyboardLayout;
    public bool HasInitialCameraPosition => _session.Current.Camera != null;
    public Vector3 InitialCameraPosition => _session.Current.Camera?.Position ?? Vector3.Zero;
    public bool HasInitialCameraDirection => _session.Current.Camera != null;
    public Vector3 InitialCameraDirection => _session.Current.Camera?.Direction ?? Vector3.Zero;
    public string PerformanceEnvironmentLabel => Dx11RuntimeOptions.ProfilerEnvironmentLabel;
    public bool IsPerformanceEnvironmentWarningVisible =>
        Dx11RuntimeOptions.IsDebugBuild || Dx11RuntimeOptions.IsDebugLayerRequested;

    [ObservableProperty] private double _fps;
    [ObservableProperty] private double _frameTime;
    [ObservableProperty] private double _presentationInterval;
    [ObservableProperty] private Vector3 _cameraPosition;
    [ObservableProperty] private Vector3 _cameraDirection;
    [ObservableProperty] private int _drawCalls;
    [ObservableProperty] private long _submittedTriangleCount;
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
    [ObservableProperty] private bool _wmoPortalCullingEnabled;
    [ObservableProperty] private float _minimumModelScreenSizePixels;
    [ObservableProperty] private float _terrainLodTransitionPixels;
    [ObservableProperty] private float _terrainRenderDistance;
    [ObservableProperty] private float _modelRenderDistance;
    [ObservableProperty] private int _tileLoadingDistance;
    [ObservableProperty] private bool _showBoundingBoxes;
    [ObservableProperty] private bool _showBoundingSpheres;
    [ObservableProperty] private bool _showTerrainGrid;
    [ObservableProperty] private bool _showTerrainWireframe;
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
    [ObservableProperty] private RenderWorkloadMetrics _profileRenderWorkload = RenderWorkloadMetrics.Empty;
    [ObservableProperty] private double _profileProcessCpuPercent;
    [ObservableProperty] private double _profileManagedMemoryMegabytes;
    [ObservableProperty] private double _profileWorkingSetMegabytes;
    [ObservableProperty] private double _profileAllocationMegabytesPerSecond;
    [ObservableProperty] private int _profileGen0Collections;
    [ObservableProperty] private bool _isPerformanceCaptureActive;
    [ObservableProperty] private bool _isDetailedGpuProfilingEnabled;
    [ObservableProperty] private string _performanceCaptureStatus =
        "Choose a visibility preset, keep the camera still, then capture.";
    [ObservableProperty] private string? _performanceCaptureFilePath;
    [ObservableProperty] private EditorObjectSnapshot? _selectedObject;
    [ObservableProperty] private bool _hasUnsavedTerrainChanges;
    [ObservableProperty] private IReadOnlyList<ModifiedTerrainTile> _modifiedTerrainTiles = [];

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
    [ObservableProperty] private EditorModeId _editorMode = EditorModeId.Selection;
    [ObservableProperty] private double _terrainBrushSize = 50;
    [ObservableProperty] private double _terrainBrushInnerRadius = 0.35;
    [ObservableProperty] private int _terrainBrushToolMode;
    [ObservableProperty] private double _terrainBrushSpeed = 5;
    [ObservableProperty] private double _terrainFlattenHeight;
    [ObservableProperty] private int _terrainSmoothIterations = 1;


    public Editor3DViewModel(
        EditorSession session,
        UndoService? undoService = null,
        IProjectService? projectService = null)
    {
        _session = session;
        _projectService = projectService;
        UndoService = undoService ?? new UndoService();
        _moveSpeed = session.Current.Rendering.MovementSpeed;
        _mouseSensitivity = session.Current.Rendering.MouseSensitivity;
        _renderTerrain = session.Current.Rendering.RenderADT;
        _renderWorldModels = session.Current.Rendering.RenderWMO;
        _renderDoodads = session.Current.Rendering.RenderM2;
        _wmoPortalCullingEnabled = session.Current.Rendering.EnableWmoPortalCulling;
        _minimumModelScreenSizePixels = session.Current.Rendering.MinimumModelScreenSizePixels;
        _terrainLodTransitionPixels = session.Current.Rendering.TerrainLodTransitionPixels;
        _terrainRenderDistance = session.Current.Rendering.TerrainRenderDistance;
        _modelRenderDistance = session.Current.Rendering.ModelRenderDistance;
        _tileLoadingDistance = session.Current.Rendering.TileLoadingDistance;
        _showBoundingBoxes = session.Current.Rendering.ShowBoundingBoxes;
        _showBoundingSpheres = session.Current.Rendering.ShowBoundingSpheres;
        _showTerrainGrid = session.Current.Rendering.ShowTerrainGrid;
        _showTerrainWireframe = session.Current.Rendering.ShowTerrainWireframe;
        _cameraPosition = session.Current.Camera?.Position ?? Vector3.Zero;
        _cameraDirection = session.Current.Camera?.Direction ?? Vector3.Zero;
        _lastProcessCpuTime = _currentProcess.TotalProcessorTime;
        _lastAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);

        session.ClientConfigurationChanged += OnClientConfigurationChanged;
        session.RenderingConfigurationChanged += OnRenderingConfigurationChanged;
        session.KeyboardLayoutChanged += OnKeyboardLayoutChanged;
    }

    public IUndoTransaction BeginEditAction(string description) =>
        UndoService.BeginTransaction(description);

    public void RecordAppliedEdit(IEditorCommand command) =>
        UndoService.RecordExecuted(command);

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
    partial void OnWmoPortalCullingEnabledChanged(bool value) => PublishViewportRenderingConfiguration();
    partial void OnMinimumModelScreenSizePixelsChanged(float value)
    {
        var normalized = Math.Clamp(value, 0f, 16f);
        if (normalized != value)
        {
            MinimumModelScreenSizePixels = normalized;
            return;
        }

        PublishViewportRenderingConfiguration();
    }
    partial void OnTerrainLodTransitionPixelsChanged(float value)
    {
        var normalized = Math.Clamp(value, 0f, 256f);
        if (normalized != value)
        {
            TerrainLodTransitionPixels = normalized;
            return;
        }

        PublishViewportRenderingConfiguration();
    }
    partial void OnTerrainRenderDistanceChanged(float value) => PublishViewportRenderingConfiguration();
    partial void OnModelRenderDistanceChanged(float value) => PublishViewportRenderingConfiguration();
    partial void OnTileLoadingDistanceChanged(int value) => PublishViewportRenderingConfiguration();
    partial void OnShowBoundingBoxesChanged(bool value) => PublishViewportRenderingConfiguration();
    partial void OnShowBoundingSpheresChanged(bool value) => PublishViewportRenderingConfiguration();
    partial void OnShowTerrainGridChanged(bool value) => PublishViewportRenderingConfiguration();
    partial void OnShowTerrainWireframeChanged(bool value) => PublishViewportRenderingConfiguration();

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
        PresentationInterval = telemetry.PresentationIntervalMilliseconds;
        CameraPosition = telemetry.CameraPosition;
        CameraDirection = telemetry.CameraDirection;
        DrawCalls = telemetry.DrawCalls;
        SubmittedTriangleCount = telemetry.SubmittedTriangleCount;
        _session.UpdateCamera(telemetry.CameraPosition, telemetry.CameraDirection);
    }

    public void UpdateRendererStatus(RendererStatus status)
    {
        RendererState = status.State;
        RendererStatusMessage = status.Message;
        RendererError = status.Error;
        IsRendererStatusVisible = status.State != RendererLifecycleState.Ready || status.HasError;
    }

    public void UpdateSelectedObject(EditorObjectSnapshot? selection)
    {
        if (SelectedObject != selection)
            SelectedObject = selection;
    }

    public void UpdateTerrainDirtyState(
        bool hasUnsavedChanges,
        IReadOnlyList<ModifiedTerrainTile> modifiedTiles)
    {
        HasUnsavedTerrainChanges = hasUnsavedChanges;
        ModifiedTerrainTiles = modifiedTiles;
    }

    public void RequestSelectedObjectTransform(ObjectTransform transform) =>
        SelectedObjectTransformRequested?.Invoke(this, transform);

    public void RequestSelectedWmoPlacement(WmoPlacementSelection selection) =>
        SelectedWmoPlacementRequested?.Invoke(this, selection);

    partial void OnTerrainBrushSizeChanged(double value)
    {
        var normalized = Math.Clamp(value, 1, 1000);
        if (normalized != value)
        {
            TerrainBrushSize = normalized;
            return;
        }

    }

    partial void OnTerrainBrushSpeedChanged(double value)
    {
        var normalized = Math.Clamp(value, 0.1, 50);
        if (normalized != value)
        {
            TerrainBrushSpeed = normalized;
            return;
        }
    }

    partial void OnTerrainBrushInnerRadiusChanged(double value)
    {
        var normalized = Math.Clamp(value, 0, 1);
        if (normalized != value)
        {
            TerrainBrushInnerRadius = normalized;
            return;
        }

    }

    public void UpdatePerformanceProfile(FrameProfileSnapshot snapshot)
    {
        var hadProfileSnapshot = _latestProfileSnapshot != null;
        _latestProfileSnapshot = snapshot;
        if (!hadProfileSnapshot)
            StartPerformanceCaptureCommand.NotifyCanExecuteChanged();

        CollectPerformanceCaptureSample(snapshot);

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
            .Where(step => step.Name != "Resource uploads (GPU timeline)" || step.DurationMilliseconds >= 0.001d)
            .ToArray();
        ProfileCpuMilliseconds = snapshot.CpuFrameMilliseconds;
        ProfileGpuMilliseconds = snapshot.GpuFrameMilliseconds;
        ProfileFrameMilliseconds = snapshot.EngineFrameMilliseconds;
        ProfilePendingAssets = snapshot.PendingAssetOperations;
        ProfileUploadedResources = snapshot.UploadedResources;
        ProfileCulling = snapshot.Culling;
        ProfileRenderWorkload = snapshot.RenderWorkload;

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
        ProfileBottleneck = PerformanceAnalyzer.IsLikelyCpuSubmissionStarved(PerformanceHistory)
            ? "CPU submission bound · GPU starvation likely"
            : PerformanceAnalyzer.ClassifyRecent(PerformanceHistory) switch
            {
                PerformanceBottleneck.Cpu => "CPU bound",
                PerformanceBottleneck.Gpu => "GPU timeline bound",
                PerformanceBottleneck.Balanced => "CPU / GPU timeline balanced",
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
        ProfileRenderWorkload = RenderWorkloadMetrics.Empty;
        ProfileProcessCpuPercent = 0;
        ProfileManagedMemoryMegabytes = 0;
        ProfileWorkingSetMegabytes = 0;
        ProfileAllocationMegabytesPerSecond = 0;
        ProfileGen0Collections = 0;
        ProfileBottleneck = "Waiting for timing samples";
    }

    [RelayCommand]
    private void CloseMetricsPanel() => IsMetricsPanelVisible = false;

    private bool CanStartPerformanceCapture() =>
        !IsPerformanceCaptureActive && _latestProfileSnapshot != null;

    [RelayCommand(CanExecute = nameof(CanStartPerformanceCapture))]
    private void StartPerformanceCapture() =>
        TryStartPerformanceCapture(
            CaptureWarmupDuration,
            CaptureSampleDuration,
            isAutomated: false);

    public bool TryStartAutomatedPerformanceCapture(TimeSpan warmupDuration, TimeSpan captureDuration) =>
        TryStartPerformanceCapture(warmupDuration, captureDuration, isAutomated: true);

    private bool TryStartPerformanceCapture(
        TimeSpan warmupDuration,
        TimeSpan captureDuration,
        bool isAutomated)
    {
        var latest = _latestProfileSnapshot;
        if (latest == null || IsPerformanceCaptureActive)
            return false;

        var requestedAt = DateTimeOffset.UtcNow;
        _captureSampleStartedAt = requestedAt + warmupDuration;
        _captureEndsAt = _captureSampleStartedAt + captureDuration;
        _activeCaptureSamples.Clear();
        _lastCaptureStatusSecond = -1;
        _isAutomatedPerformanceCapture = isAutomated;
        PerformanceCaptureFilePath = null;
        _captureContext = new PerformanceCaptureContext(
            DescribeVisibilityScenario(),
            latest.ViewportWidth,
            latest.ViewportHeight,
            CameraPosition,
            CameraDirection,
            RenderTerrain,
            RenderWorldModels,
            RenderDoodads,
            IsDetailedGpuProfilingEnabled,
            RenderingConfiguration.TerrainRenderDistance,
            RenderingConfiguration.ModelRenderDistance,
            RenderingConfiguration.TileLoadingDistance)
        {
            BuildConfiguration = Dx11RuntimeOptions.BuildConfiguration,
            D3D11DebugLayerEnabled = Dx11RuntimeOptions.IsDebugLayerRequested,
            MinimumModelScreenSizePixels = MinimumModelScreenSizePixels,
            TerrainLodTransitionPixels = TerrainLodTransitionPixels
        };
        IsPerformanceCaptureActive = true;
        IsProfilingPaused = false;
        PerformanceCaptureStatus =
            $"Warming up GPU timing ({warmupDuration.TotalSeconds:0.#} s); keep the camera still.";
        StartPerformanceCaptureCommand.NotifyCanExecuteChanged();
        return true;
    }

    public void UpdateAutomatedBenchmarkWaitingStatus(
        TimeSpan elapsed,
        int stableFrames,
        int requiredStableFrames)
    {
        if (IsPerformanceCaptureActive)
            return;

        PerformanceCaptureStatus = stableFrames > 0
            ? $"Automated benchmark: workload stable {stableFrames}/{requiredStableFrames} frames."
            : $"Automated benchmark: waiting for world streaming ({elapsed.TotalSeconds:0} s).";
    }

    public void FailAutomatedPerformanceCapture(string message)
    {
        PerformanceCaptureStatus = message;
        AutomatedPerformanceCaptureFailed?.Invoke(this, message);
    }

    private void CollectPerformanceCaptureSample(FrameProfileSnapshot snapshot)
    {
        if (!IsPerformanceCaptureActive)
            return;

        var capturedAt = snapshot.CapturedAt;
        if (capturedAt < _captureSampleStartedAt)
            return;

        if (capturedAt < _captureEndsAt)
        {
            _activeCaptureSamples.Add(snapshot);
            var remainingSeconds = Math.Max(1, (int)Math.Ceiling((_captureEndsAt - capturedAt).TotalSeconds));
            if (remainingSeconds != _lastCaptureStatusSecond)
            {
                _lastCaptureStatusSecond = remainingSeconds;
                PerformanceCaptureStatus = $"Capturing {_captureContext?.Scenario}: {remainingSeconds} s remaining.";
            }
            return;
        }

        FinishPerformanceCapture(capturedAt);
    }

    private void FinishPerformanceCapture(DateTimeOffset endedAt)
    {
        var context = _captureContext;
        var samples = _activeCaptureSamples.ToArray();
        var wasAutomated = _isAutomatedPerformanceCapture;
        _activeCaptureSamples.Clear();
        _captureContext = null;
        _isAutomatedPerformanceCapture = false;
        IsPerformanceCaptureActive = false;
        StartPerformanceCaptureCommand.NotifyCanExecuteChanged();

        if (context == null || samples.Length == 0)
        {
            PerformanceCaptureStatus = "Capture failed: no frame samples were recorded.";
            if (wasAutomated)
                AutomatedPerformanceCaptureFailed?.Invoke(this, PerformanceCaptureStatus);
            return;
        }

        var capture = PerformanceCaptureAnalyzer.Create(
            _captureSampleStartedAt,
            endedAt,
            context,
            samples);
        PerformanceCaptureStatus = $"Saving {samples.Length} samples...";
        _ = SavePerformanceCaptureAsync(capture, wasAutomated);
    }

    private async Task SavePerformanceCaptureAsync(
        PerformanceCaptureFile capture,
        bool isAutomated)
    {
        try
        {
            var captureDirectory = _projectService?.GetPath("PerformanceCaptures") ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WTEditor",
                "PerformanceCaptures");
            Directory.CreateDirectory(captureDirectory);
            var scenario = string.Concat(capture.Context.Scenario.Select(character =>
                char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-'));
            var fileName = $"{capture.StartedAt:yyyyMMdd-HHmmss}-{scenario}.json";
            var filePath = Path.Combine(captureDirectory, fileName);
            var json = JsonSerializer.Serialize(capture, new JsonSerializerOptions
            {
                WriteIndented = true,
                IncludeFields = true
            });
            await File.WriteAllTextAsync(filePath, json);

            Dispatcher.UIThread.Post(() =>
            {
                PerformanceCaptureFilePath = filePath;
                PerformanceCaptureStatus =
                    $"Saved {capture.Samples.Count} samples: {filePath}";
                if (isAutomated)
                    AutomatedPerformanceCaptureSaved?.Invoke(this, filePath);
            });
        }
        catch (Exception exception)
        {
            Dispatcher.UIThread.Post(() =>
            {
                PerformanceCaptureStatus = $"Capture save failed: {exception.Message}";
                if (isAutomated)
                    AutomatedPerformanceCaptureFailed?.Invoke(this, PerformanceCaptureStatus);
            });
        }
    }

    private string DescribeVisibilityScenario() =>
        (RenderTerrain, RenderWorldModels, RenderDoodads) switch
        {
            (true, false, false) => "terrain-only",
            (false, true, false) => "wmo-only",
            (false, false, true) => "m2-only",
            (true, true, true) => "mixed-world",
            (false, false, false) => "empty-world",
            _ => $"terrain-{RenderTerrain}-wmo-{RenderWorldModels}-m2-{RenderDoodads}"
        };

    private void OnClientConfigurationChanged(object? sender, ClientConfiguration configuration) =>
        ClientConfigurationChanged?.Invoke(this, configuration);

    private void OnRenderingConfigurationChanged(object? sender, RenderingConfiguration configuration)
    {
        _synchronizingRenderingSettings = true;
        try
        {
            MoveSpeed = configuration.MovementSpeed;
            MouseSensitivity = configuration.MouseSensitivity;
            MinimumModelScreenSizePixels = configuration.MinimumModelScreenSizePixels;
            TerrainLodTransitionPixels = configuration.TerrainLodTransitionPixels;
            WmoPortalCullingEnabled = configuration.EnableWmoPortalCulling;
            TerrainRenderDistance = configuration.TerrainRenderDistance;
            ModelRenderDistance = configuration.ModelRenderDistance;
            TileLoadingDistance = configuration.TileLoadingDistance;
            ShowBoundingBoxes = configuration.ShowBoundingBoxes;
            ShowBoundingSpheres = configuration.ShowBoundingSpheres;
            ShowTerrainGrid = configuration.ShowTerrainGrid;
            ShowTerrainWireframe = configuration.ShowTerrainWireframe;
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
