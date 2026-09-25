using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTEditor.Application;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Application.Geometry;
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
    public event EventHandler<LightingSettingsSnapshot>? LightingSettingsChanged;
    public event EventHandler<KeyboardLayoutMode>? KeyboardLayoutChanged;
    public event EventHandler<string>? AutomatedPerformanceCaptureSaved;
    public event EventHandler<string>? AutomatedPerformanceCaptureFailed;
    public event EventHandler<ObjectTransform>? SelectedObjectTransformRequested;
    public event EventHandler? CopySelectionRequested;
    public event EventHandler<WmoPlacementSelection>? SelectedWmoPlacementRequested;
    public event EventHandler<WorldNavigationRequest>? WorldNavigationRequested;
    public event EventHandler<Vector2>? TerrainChunkTexturesRequested;
    public event EventHandler<IReadOnlyList<TerrainChunkTextureLayer>>? TerrainChunkTexturesPicked;
    public event EventHandler<Vector2>? DominantTerrainTextureRequested;
    public event EventHandler<TerrainChunkTextureLayer>? DominantTerrainTexturePicked;
    public event EventHandler? CurrentTerrainTileTexturesRequested;
    public event EventHandler<IReadOnlyList<TerrainChunkTextureLayer>>? CurrentTerrainTileTexturesPicked;
    public UndoService UndoService { get; }
    public LightingViewModel Lighting { get; } = new();
    public WorldNavigationRequest? CurrentWorldNavigation { get; private set; }

    public ClientConfiguration ClientConfiguration => _session.Current.Client;
    public RenderingConfiguration RenderingConfiguration => _session.Current.Rendering;
    public KeyboardLayoutMode KeyboardLayout => _session.Current.KeyboardLayout;
    public bool HasInitialCameraPosition => _session.Current.Camera != null;
    public Vector3 InitialCameraPosition => _session.Current.Camera?.Position ?? Vector3.Zero;
    public bool HasInitialCameraDirection => _session.Current.Camera != null;
    public Vector3 InitialCameraDirection => _session.Current.Camera?.Direction ?? Vector3.Zero;
    public string PerformanceEnvironmentLabel => Dx11RuntimeOptions.ProfilerEnvironmentLabel;
    public bool IsPerformanceEnvironmentWarningVisible =>
        Dx11RuntimeOptions.IsDebugBuild || Dx11RuntimeOptions.IsDebugLayerRequested;
    public Vector3 CameraClientPosition => MapCoordinates.TerrainToClient(CameraPosition);
    public Vector3 CameraClientDirection => MapCoordinates.TerrainDirectionToClient(CameraDirection);
    public string ProfileGpuMillisecondsDisplay => ProfileGpuMilliseconds is { } value
        ? $"{value:F2} ms"
        : "—";
    public ObservableCollection<ProfilerRenderPassViewModel> ProfileRenderPasses { get; } = [];

    [ObservableProperty] private double _fps;
    [ObservableProperty] private double _frameTime;
    [ObservableProperty] private double _presentationInterval;
    [ObservableProperty] private Vector3 _cameraPosition;
    [ObservableProperty] private Vector3 _cameraDirection;
    [ObservableProperty] private int _activeMapId = -1;

    partial void OnCameraPositionChanged(Vector3 value) => OnPropertyChanged(nameof(CameraClientPosition));
    partial void OnCameraDirectionChanged(Vector3 value) => OnPropertyChanged(nameof(CameraClientDirection));
    [ObservableProperty] private int _drawCalls;
    [ObservableProperty] private long _submittedTriangleCount;
    [ObservableProperty] private float _moveSpeed;
    [ObservableProperty] private float _mouseSensitivity;
    [ObservableProperty] private RendererLifecycleState _rendererState = RendererLifecycleState.Detached;
    [ObservableProperty] private string _rendererStatusMessage = "Renderer not attached.";
    [ObservableProperty] private string? _rendererError;
    [ObservableProperty] private bool _isRendererStatusVisible = true;
    [ObservableProperty] private bool _isMetricsPanelVisible;
    [ObservableProperty] private bool _isLightingPanelVisible;
    [ObservableProperty] private bool _renderTerrain;
    [ObservableProperty] private bool _renderLiquid;
    [ObservableProperty] private bool _renderWorldModels;
    [ObservableProperty] private bool _renderDoodads;
    [ObservableProperty] private bool _renderParticles;
    [ObservableProperty] private bool _animateModels;
    [ObservableProperty] private bool _wmoPortalCullingEnabled;
    [ObservableProperty] private float _minimumModelScreenSizePixels;
    [ObservableProperty] private float _terrainLodTransitionPixels;
    [ObservableProperty] private float _terrainRenderDistance;
    [ObservableProperty] private float _modelRenderDistance;
    [ObservableProperty] private float _animationRenderDistancePercent;
    [ObservableProperty] private float _particleRenderDistancePercent;
    [ObservableProperty] private int _tileLoadingDistance;
    [ObservableProperty] private bool _showBoundingBoxes;
    [ObservableProperty] private bool _showBoundingSpheres;
    [ObservableProperty] private bool _showTerrainGrid;
    [ObservableProperty] private bool _showTerrainWireframe;
    [ObservableProperty] private bool _disableScreenGlow;
    [ObservableProperty] private bool _isProfilingPaused;
    [ObservableProperty] private IReadOnlyList<FrameProfileSnapshot> _performanceHistory = Array.Empty<FrameProfileSnapshot>();
    public ObservableCollection<ProfilerFrameStepViewModel> CurrentFrameSteps { get; } = [];
    [ObservableProperty] private double _profileCpuMilliseconds;
    [ObservableProperty] private double? _profileGpuMilliseconds;
    [ObservableProperty] private double _profileFrameMilliseconds;
    [ObservableProperty] private double _profileStreamingBudgetMilliseconds;
    [ObservableProperty] private string _profileBottleneck = "Waiting for timing samples";
    [ObservableProperty] private int _profilePendingAssets;
    [ObservableProperty] private int _profileUploadedResources;
    [ObservableProperty] private AssetStreamingProfile _profileAssetStreaming = AssetStreamingProfile.Empty;
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

    partial void OnProfileGpuMillisecondsChanged(double? value) =>
        OnPropertyChanged(nameof(ProfileGpuMillisecondsDisplay));

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
    [ObservableProperty] private double _brushSize = 10;
    [ObservableProperty] private double _brushFalloff = 0.35;
    [ObservableProperty] private bool _brushHasFalloff = true;
    [ObservableProperty] private BrushShape _brushShape = BrushShape.Circle;
    [ObservableProperty] private BrushFalloffProfile _brushFalloffProfile = BrushFalloffProfile.Smooth;
    [ObservableProperty] private int _terrainBrushToolMode;
    [ObservableProperty] private double _terrainBrushSpeed = 5;
    [ObservableProperty] private double _terrainFlattenHeight;
    [ObservableProperty] private int _terrainFlattenTarget;
    [ObservableProperty] private int _terrainSmoothIterations = 1;
    [ObservableProperty] private int _textureBrushToolMode;
    [ObservableProperty] private uint _textureBrushTextureFileDataId;
    [ObservableProperty] private double _textureBrushOpacity = 255;
    [ObservableProperty] private double _textureBrushStrength = 1;
    [ObservableProperty] private bool _texturePickerModeActive;


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
        _renderLiquid = session.Current.Rendering.RenderLiquid;
        _renderWorldModels = session.Current.Rendering.RenderWMO;
        _renderDoodads = session.Current.Rendering.RenderM2;
        _renderParticles = session.Current.Rendering.RenderParticles;
        _animateModels = session.Current.Rendering.AnimateModels;
        _wmoPortalCullingEnabled = session.Current.Rendering.EnableWmoPortalCulling;
        _minimumModelScreenSizePixels = session.Current.Rendering.MinimumModelScreenSizePixels;
        _terrainLodTransitionPixels = session.Current.Rendering.TerrainLodTransitionPixels;
        _terrainRenderDistance = session.Current.Rendering.TerrainRenderDistance;
        _modelRenderDistance = session.Current.Rendering.ModelRenderDistance;
        _animationRenderDistancePercent = session.Current.Rendering.AnimationRenderDistancePercent;
        _particleRenderDistancePercent = session.Current.Rendering.ParticleRenderDistancePercent;
        _tileLoadingDistance = session.Current.Rendering.TileLoadingDistance;
        _showBoundingBoxes = session.Current.Rendering.ShowBoundingBoxes;
        _showBoundingSpheres = session.Current.Rendering.ShowBoundingSpheres;
        _showTerrainGrid = session.Current.Rendering.ShowTerrainGrid;
        _showTerrainWireframe = session.Current.Rendering.ShowTerrainWireframe;
        _disableScreenGlow = session.Current.Rendering.DisableScreenGlow;
        Lighting.SetPreferences(
            session.Current.Rendering.WorldLightingTime,
            session.Current.Rendering.UseLocalWorldLightingTime);
        _cameraPosition = session.Current.Camera?.Position ?? Vector3.Zero;
        _cameraDirection = session.Current.Camera?.Direction ?? Vector3.Zero;
        _lastProcessCpuTime = _currentProcess.TotalProcessorTime;
        _lastAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
        Lighting.Changed += OnLightingChanged;

        session.ClientConfigurationChanged += OnClientConfigurationChanged;
        session.RenderingConfigurationChanged += OnRenderingConfigurationChanged;
        session.KeyboardLayoutChanged += OnKeyboardLayoutChanged;
    }

    public IUndoTransaction BeginEditAction(string description) =>
        UndoService.BeginTransaction(description);

    public void RecordAppliedEdit(IEditorCommand command) =>
        UndoService.RecordExecuted(command);

    public void RequestTerrainChunkTextures(Vector2 mousePosition) =>
        TerrainChunkTexturesRequested?.Invoke(this, mousePosition);

    public void RequestCopySelection() => CopySelectionRequested?.Invoke(this, EventArgs.Empty);

    public void PublishTerrainChunkTextures(IReadOnlyList<TerrainChunkTextureLayer> layers) =>
        TerrainChunkTexturesPicked?.Invoke(this, layers);

    public void RequestDominantTerrainTexture(Vector2 mousePosition) =>
        DominantTerrainTextureRequested?.Invoke(this, mousePosition);

    public void PublishDominantTerrainTexture(TerrainChunkTextureLayer texture) =>
        DominantTerrainTexturePicked?.Invoke(this, texture);

    public void RequestCurrentTerrainTileTextures() =>
        CurrentTerrainTileTexturesRequested?.Invoke(this, EventArgs.Empty);

    public void PublishCurrentTerrainTileTextures(IReadOnlyList<TerrainChunkTextureLayer> textures) =>
        CurrentTerrainTileTexturesPicked?.Invoke(this, textures);

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

    partial void OnRenderTerrainChanged(bool value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { RenderADT = value });
    partial void OnRenderLiquidChanged(bool value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { RenderLiquid = value });
    partial void OnRenderWorldModelsChanged(bool value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { RenderWMO = value });
    partial void OnRenderDoodadsChanged(bool value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { RenderM2 = value });
    partial void OnRenderParticlesChanged(bool value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { RenderParticles = value });
    partial void OnAnimateModelsChanged(bool value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { AnimateModels = value });
    partial void OnWmoPortalCullingEnabledChanged(bool value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { EnableWmoPortalCulling = value });
    partial void OnMinimumModelScreenSizePixelsChanged(float value)
    {
        var normalized = Math.Clamp(value, 0f, 16f);
        if (normalized != value)
        {
            MinimumModelScreenSizePixels = normalized;
            return;
        }

        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { MinimumModelScreenSizePixels = value });
    }
    partial void OnTerrainLodTransitionPixelsChanged(float value)
    {
        var normalized = Math.Clamp(value, 0f, 256f);
        if (normalized != value)
        {
            TerrainLodTransitionPixels = normalized;
            return;
        }

        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { TerrainLodTransitionPixels = value });
    }
    partial void OnTerrainRenderDistanceChanged(float value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { TerrainRenderDistance = value });
    partial void OnModelRenderDistanceChanged(float value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { ModelRenderDistance = value });
    partial void OnAnimationRenderDistancePercentChanged(float value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { AnimationRenderDistancePercent = value });
    partial void OnParticleRenderDistancePercentChanged(float value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { ParticleRenderDistancePercent = value });
    partial void OnTileLoadingDistanceChanged(int value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { TileLoadingDistance = value });
    partial void OnShowBoundingBoxesChanged(bool value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { ShowBoundingBoxes = value });
    partial void OnShowBoundingSpheresChanged(bool value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { ShowBoundingSpheres = value });
    partial void OnShowTerrainGridChanged(bool value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { ShowTerrainGrid = value });
    partial void OnShowTerrainWireframeChanged(bool value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { ShowTerrainWireframe = value });
    partial void OnDisableScreenGlowChanged(bool value) =>
        UpdatePersistedRenderingConfiguration(_session.Current.Rendering with { DisableScreenGlow = value });

    private void UpdatePersistedRenderingConfiguration(RenderingConfiguration rendering)
    {
        if (!_synchronizingRenderingSettings)
            _session.UpdateRendering(rendering);
    }

    public void UpdateTelemetry(ViewportTelemetry telemetry)
    {
        Fps = telemetry.FramesPerSecond;
        FrameTime = telemetry.FrameTimeMilliseconds;
        PresentationInterval = telemetry.PresentationIntervalMilliseconds;
        CameraPosition = telemetry.CameraPosition;
        CameraDirection = telemetry.CameraDirection;
        ActiveMapId = telemetry.ActiveMapId;
        DrawCalls = telemetry.DrawCalls;
        SubmittedTriangleCount = telemetry.SubmittedTriangleCount;
        _session.UpdateCamera(telemetry.CameraPosition, telemetry.CameraDirection);
    }

    public void UpdateActiveLighting(LightingSettingsSnapshot lighting)
    {
        Lighting.Update(lighting);
    }

    private void OnLightingChanged(object? sender, LightingSettingsSnapshot lighting)
    {
        _session.UpdateRendering(RenderingConfiguration with
        {
            WorldLightingTime = checked((int)lighting.Time),
            UseLocalWorldLightingTime = lighting.IsDynamic
        });
        LightingSettingsChanged?.Invoke(this, lighting);
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

    public void RequestWorldNavigation(WorldNavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Navigation is state, not a transient notification. The renderer view
        // may still be attaching or restarting when the first map is chosen;
        // retaining the target lets that renderer replay the selection instead
        // of silently dropping the only request that supplies the map ID.
        CurrentWorldNavigation = request;
        WorldNavigationRequested?.Invoke(this, request);
    }

    partial void OnBrushSizeChanged(double value)
    {
        var normalized = Math.Clamp(value, 1, 1000);
        if (normalized != value)
        {
            BrushSize = normalized;
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

    partial void OnBrushFalloffChanged(double value)
    {
        var normalized = Math.Clamp(value, 0, 1);
        if (normalized != value)
        {
            BrushFalloff = normalized;
            return;
        }

    }

    partial void OnTextureBrushOpacityChanged(double value)
    {
        var normalized = Math.Clamp(value, 0, 255);
        if (normalized != value)
        {
            TextureBrushOpacity = normalized;
            return;
        }
    }

    partial void OnTextureBrushStrengthChanged(double value)
    {
        var normalized = Math.Clamp(value, 0, 1);
        if (normalized != value)
        {
            TextureBrushStrength = normalized;
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
        UpdateCurrentFrameSteps(snapshot.Steps);
        ProfileCpuMilliseconds = snapshot.CpuFrameMilliseconds;
        ProfileGpuMilliseconds = snapshot.GpuFrameMilliseconds;
        ProfileFrameMilliseconds = snapshot.EngineFrameMilliseconds;
        ProfileStreamingBudgetMilliseconds = snapshot.StreamingBudgetMilliseconds;
        ProfilePendingAssets = snapshot.PendingAssetOperations;
        ProfileUploadedResources = snapshot.UploadedResources;
        ProfileAssetStreaming = snapshot.AssetStreaming;
        ProfileCulling = snapshot.Culling;
        ProfileRenderWorkload = snapshot.RenderWorkload;
        UpdateRenderPasses(snapshot.RenderWorkload.Passes);

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

    private void UpdateCurrentFrameSteps(IReadOnlyList<FrameTimingStep> steps)
    {
        var visibleSteps = FrameTimingCategoryCatalog.OrderSteps(steps);

        for (var index = 0; index < visibleSteps.Count; index++)
        {
            if (index == CurrentFrameSteps.Count)
                CurrentFrameSteps.Add(new ProfilerFrameStepViewModel(visibleSteps[index]));
            else
                CurrentFrameSteps[index].Update(visibleSteps[index]);
        }

        while (CurrentFrameSteps.Count > visibleSteps.Count)
            CurrentFrameSteps.RemoveAt(CurrentFrameSteps.Count - 1);
    }

    private void UpdateRenderPasses(IReadOnlyList<RenderPassMetrics> passes)
    {
        for (var index = 0; index < passes.Count; index++)
        {
            if (index == ProfileRenderPasses.Count)
                ProfileRenderPasses.Add(new ProfilerRenderPassViewModel(passes[index]));
            else
                ProfileRenderPasses[index].Update(passes[index]);
        }

        while (ProfileRenderPasses.Count > passes.Count)
            ProfileRenderPasses.RemoveAt(ProfileRenderPasses.Count - 1);
    }

    [RelayCommand]
    private void ClearPerformanceHistory()
    {
        _performanceSamples.Clear();
        PerformanceHistory = Array.Empty<FrameProfileSnapshot>();
        CurrentFrameSteps.Clear();
        ProfileCpuMilliseconds = 0;
        ProfileGpuMilliseconds = null;
        ProfileFrameMilliseconds = 0;
        ProfileStreamingBudgetMilliseconds = 0;
        ProfilePendingAssets = 0;
        ProfileUploadedResources = 0;
        ProfileAssetStreaming = AssetStreamingProfile.Empty;
        ProfileCulling = new CullingMetrics(0, 0, 0, 0, 0, 0);
        ProfileRenderWorkload = RenderWorkloadMetrics.Empty;
        ProfileRenderPasses.Clear();
        ProfileProcessCpuPercent = 0;
        ProfileManagedMemoryMegabytes = 0;
        ProfileWorkingSetMegabytes = 0;
        ProfileAllocationMegabytesPerSecond = 0;
        ProfileGen0Collections = 0;
        ProfileBottleneck = "Waiting for timing samples";
    }

    [RelayCommand]
    private void CloseMetricsPanel() => IsMetricsPanelVisible = false;

    [RelayCommand]
    private void CloseLightingPanel() => IsLightingPanelVisible = false;

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
            TerrainLodTransitionPixels = TerrainLodTransitionPixels,
            RenderLiquid = RenderLiquid,
            RenderParticles = RenderParticles,
            AnimateModels = AnimateModels,
            AnimationRenderDistancePercent = AnimationRenderDistancePercent,
            ParticleRenderDistancePercent = ParticleRenderDistancePercent
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
        (RenderTerrain, RenderLiquid, RenderWorldModels, RenderDoodads) switch
        {
            (true, true, false, false) => "terrain-liquid-only",
            (true, false, false, false) => "terrain-only",
            (false, _, true, false) => "wmo-only",
            (false, _, false, true) => "m2-only",
            (true, true, true, true) => "mixed-world",
            (false, false, false, false) => "empty-world",
            _ => $"terrain-{RenderTerrain}-liquid-{RenderLiquid}-wmo-{RenderWorldModels}-m2-{RenderDoodads}"
        };

    private void OnClientConfigurationChanged(object? sender, ClientConfiguration configuration)
    {
        CurrentWorldNavigation = null;
        ClientConfigurationChanged?.Invoke(this, configuration);
    }

    private void OnRenderingConfigurationChanged(object? sender, RenderingConfiguration configuration)
    {
        _synchronizingRenderingSettings = true;
        try
        {
            MoveSpeed = configuration.MovementSpeed;
            MouseSensitivity = configuration.MouseSensitivity;
            RenderTerrain = configuration.RenderADT;
            RenderWorldModels = configuration.RenderWMO;
            RenderDoodads = configuration.RenderM2;
            RenderParticles = configuration.RenderParticles;
            MinimumModelScreenSizePixels = configuration.MinimumModelScreenSizePixels;
            TerrainLodTransitionPixels = configuration.TerrainLodTransitionPixels;
            WmoPortalCullingEnabled = configuration.EnableWmoPortalCulling;
            TerrainRenderDistance = configuration.TerrainRenderDistance;
            ModelRenderDistance = configuration.ModelRenderDistance;
            AnimationRenderDistancePercent = configuration.AnimationRenderDistancePercent;
            ParticleRenderDistancePercent = configuration.ParticleRenderDistancePercent;
            TileLoadingDistance = configuration.TileLoadingDistance;
            RenderLiquid = configuration.RenderLiquid;
            AnimateModels = configuration.AnimateModels;
            ShowBoundingBoxes = configuration.ShowBoundingBoxes;
            ShowBoundingSpheres = configuration.ShowBoundingSpheres;
            ShowTerrainGrid = configuration.ShowTerrainGrid;
            ShowTerrainWireframe = configuration.ShowTerrainWireframe;
            DisableScreenGlow = configuration.DisableScreenGlow;
            if (Lighting.IsDynamic != configuration.UseLocalWorldLightingTime ||
                (!configuration.UseLocalWorldLightingTime &&
                 Lighting.Time != configuration.WorldLightingTime))
            {
                Lighting.SetPreferences(
                    configuration.WorldLightingTime,
                    configuration.UseLocalWorldLightingTime);
            }
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
        Lighting.Changed -= OnLightingChanged;
        _currentProcess.Dispose();
    }
}
