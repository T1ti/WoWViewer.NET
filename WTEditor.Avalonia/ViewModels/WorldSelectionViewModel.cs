using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WTEditor.Application.Models;
using WTEditor.Avalonia.Models;
using WTEditor.Avalonia.Presentation;
using WTEditor.Avalonia.Services;
using WoWRenderLib.Diagnostics;

namespace WTEditor.Avalonia.ViewModels;

public sealed record MapFilterOption(int? Value, string DisplayName);

public sealed record WorldMapListItem(
    int Id,
    string Name,
    int ExpansionId,
    string ExpansionName,
    int InstanceType,
    string MapType,
    bool HasTerrain,
    Geometry ExpansionIcon,
    IBrush ExpansionBrush);

/// <summary>
/// Presentation state for the map-selection half of the World Selection workspace.
/// </summary>
public partial class WorldSelectionViewModel : ViewModelBase, IDisposable
{
    private static readonly Geometry[] ExpansionIcons =
    [
        EditorIcons.ExpansionCircle,
        EditorIcons.ExpansionDiamond,
        EditorIcons.ExpansionHexagon,
        EditorIcons.ExpansionShield,
        EditorIcons.ExpansionStar
    ];

    private static readonly IBrush[] ExpansionBrushes =
    [
        Brush.Parse("#708090"),
        Brush.Parse("#4A8FCA"),
        Brush.Parse("#A871D1"),
        Brush.Parse("#C97844"),
        Brush.Parse("#4EAF8A"),
        Brush.Parse("#C2A849")
    ];

    private readonly IMapCatalogService _mapCatalogService;
    private readonly Editor3DViewModel _viewport;
    private readonly Dictionary<int, WorldMapCatalogEntry> _mapEntries = [];
    public MinimapViewModel Minimap { get; }
    public MapSettingsViewModel MapSettings { get; }
    private readonly List<WorldMapListItem> _allMaps = [];
    private CancellationTokenSource? _loadCancellation;
    private bool _isActive;
    private bool _isContentReady;
    private bool _isMapsLoaded;
    private bool _isDisposed;
    private int _catalogGeneration;

    public ObservableCollection<WorldMapListItem> FilteredMaps { get; } = [];

    [ObservableProperty]
    private IReadOnlyList<MapFilterOption> _expansionFilters =
        [new(null, "All expansions")];

    [ObservableProperty]
    private IReadOnlyList<MapFilterOption> _mapTypeFilters =
        [new(null, "All map types")];

    [ObservableProperty]
    private MapFilterOption? _selectedExpansion;

    [ObservableProperty]
    private MapFilterOption? _selectedMapType;

    [ObservableProperty]
    private WorldMapListItem? _selectedMap;

    [ObservableProperty]
    private WorldMapCatalogEntry? _selectedMapEntry;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showMapsWithoutTerrain;

    [ObservableProperty]
    private string _statusMessage =
        "Map data will appear after the selected WoW client finishes loading.";

    [ObservableProperty]
    private bool _isLoading;

    public bool HasFilteredMaps => FilteredMaps.Count > 0;
    public bool HasNoFilteredMaps => _isMapsLoaded && !IsLoading && FilteredMaps.Count == 0;

    public WorldSelectionViewModel(
        IMapCatalogService mapCatalogService,
        Editor3DViewModel viewport,
        IMinimapService? minimapService = null)
    {
        Minimap = new MinimapViewModel(minimapService ?? new MinimapService(), NavigateFromMinimap);
        MapSettings = new MapSettingsViewModel();
        _mapCatalogService = mapCatalogService;
        _viewport = viewport;
        _isContentReady = viewport.RendererState == RendererLifecycleState.Ready;
        _viewport.PropertyChanged += OnViewportPropertyChanged;
        _viewport.ClientConfigurationChanged += OnClientConfigurationChanged;
        SelectedExpansion = ExpansionFilters[0];
        SelectedMapType = MapTypeFilters[0];
    }

    public Task ActivateAsync()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _isActive = true;
        _isContentReady |= _viewport.RendererState == RendererLifecycleState.Ready;

        if (_viewport.RendererState == RendererLifecycleState.Failed)
            StatusMessage = "Map data is unavailable because the WoW client could not be loaded.";
        else if (_viewport.RendererState == RendererLifecycleState.AwaitingContent)
            StatusMessage = "Map data is unavailable until this WoW client's content is loaded.";

        return LoadMapCatalogIfReadyAsync();
    }

    private void OnViewportPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(Editor3DViewModel.CameraPosition))
        {
            UpdateActivePosition();
            return;
        }

        if (eventArgs.PropertyName == nameof(Editor3DViewModel.CameraDirection))
        {
            UpdateActivePosition();
            return;
        }

        if (eventArgs.PropertyName == nameof(Editor3DViewModel.ActiveWdtFileDataId))
        {
            SynchronizeActiveWorldSelection();
            return;
        }

        if (eventArgs.PropertyName != nameof(Editor3DViewModel.RendererState))
            return;

        switch (_viewport.RendererState)
        {
            case RendererLifecycleState.Ready:
                _isContentReady = true;
                _ = LoadMapCatalogIfReadyAsync();
                break;
            case RendererLifecycleState.Failed when !_isMapsLoaded:
                StatusMessage = "Map data is unavailable because the WoW client could not be loaded.";
                break;
            case RendererLifecycleState.AwaitingContent:
                _isContentReady = false;
                if (!_isMapsLoaded)
                    StatusMessage = "Map data is unavailable until this WoW client's content is loaded.";
                break;
            case RendererLifecycleState.Initializing:
            case RendererLifecycleState.LoadingContent:
                if (!_isMapsLoaded)
                    StatusMessage = "Loading WoW content before reading Map DB2...";
                break;
        }
    }

    private void OnClientConfigurationChanged(object? sender, ClientConfiguration configuration)
    {
        _catalogGeneration++;
        _loadCancellation?.Cancel();
        _isContentReady = false;
        _isMapsLoaded = false;
        _allMaps.Clear();
        _mapEntries.Clear();
        Minimap.SelectMap(null);
        MapSettings.SetMap(null);
        FilteredMaps.Clear();
        SelectedMap = null;
        SelectedMapEntry = null;
        ExpansionFilters = [new(null, "All expansions")];
        MapTypeFilters = [new(null, "All map types")];
        SelectedExpansion = ExpansionFilters[0];
        SelectedMapType = MapTypeFilters[0];
        StatusMessage = "Map data will refresh after the new WoW client finishes loading.";
        NotifyMapListChanged();
    }

    private async Task LoadMapCatalogIfReadyAsync()
    {
        if (_isDisposed || !_isActive || !_isContentReady || _isMapsLoaded || IsLoading)
            return;

        var generation = _catalogGeneration;
        _loadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;

        IsLoading = true;
        StatusMessage = "Reading map database...";
        NotifyMapListChanged();

        try
        {
            var maps = await _mapCatalogService.LoadAsync(cancellation.Token);
            if (_isDisposed || cancellation.IsCancellationRequested || generation != _catalogGeneration)
                return;

            _allMaps.Clear();
            _mapEntries.Clear();
            foreach (var map in maps)
                _mapEntries[map.Map.Id] = map;
            _allMaps.AddRange(maps.Select(CreateMapListItem));
            UpdateFilterOptions();
            ApplyFilters();
            _isMapsLoaded = true;
            SynchronizeActiveWorldSelection();
            StatusMessage = $"{_allMaps.Count:N0} maps and WDT headers loaded.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A client change or disposal superseded this request.
        }
        catch (Exception exception)
        {
            LoadDiagnostics.Error("Loading the world-selection map catalog", exception);
            if (!_isDisposed && generation == _catalogGeneration)
                StatusMessage = $"Unable to read map database: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
                IsLoading = false;
                if (!_isDisposed)
                    NotifyMapListChanged();
            }

            cancellation.Dispose();

            if (!_isDisposed && generation != _catalogGeneration)
                _ = LoadMapCatalogIfReadyAsync();
        }
    }

    private void UpdateFilterOptions()
    {
        ExpansionFilters =
        [
            new(null, "All expansions"),
            .. _allMaps
                .GroupBy(map => map.ExpansionId)
                .OrderBy(group => group.Key)
                .Select(group => new MapFilterOption(group.Key, group.First().ExpansionName))
        ];

        MapTypeFilters =
        [
            new(null, "All map types"),
            .. _allMaps
                .GroupBy(map => map.InstanceType)
                .OrderBy(group => group.Key)
                .Select(group => new MapFilterOption(group.Key, group.First().MapType))
        ];

        SelectedExpansion = ExpansionFilters[0];
        SelectedMapType = MapTypeFilters[0];
    }

    partial void OnSelectedMapChanged(WorldMapListItem? value)
    {
        var entry = value != null && _mapEntries.TryGetValue(value.Id, out var selectedEntry)
            ? selectedEntry
            : null;
        SelectedMapEntry = entry;
        MapSettings.SetMap(entry);
        Minimap.ActivePosition = null;
        Minimap.SelectMap(entry);
        UpdateActivePosition();
    }

    private void NavigateFromMinimap(WTEditor.Application.Geometry.TilePoint position)
    {
        if (SelectedMap == null || !_mapEntries.TryGetValue(SelectedMap.Id, out var map))
            return;

        _viewport.RequestWorldNavigation(new WorldNavigationRequest(
            map.Map.Id,
            map.Wdt.FileDataId,
            position,
            !map.HasTerrain));
    }

    private void SynchronizeActiveWorldSelection()
    {
        if (!_isMapsLoaded || _viewport.ActiveWdtFileDataId == 0)
            return;

        var active = _allMaps.FirstOrDefault(map =>
            _mapEntries.TryGetValue(map.Id, out var entry) &&
            entry.Wdt.FileDataId == _viewport.ActiveWdtFileDataId);
        if (active == null)
            return;

        if (!FilteredMaps.Contains(active))
        {
            SearchText = string.Empty;
            SelectedExpansion = ExpansionFilters.FirstOrDefault();
            SelectedMapType = MapTypeFilters.FirstOrDefault();
            ShowMapsWithoutTerrain = !active.HasTerrain;
            ApplyFilters();
        }

        if (SelectedMap != active)
        {
            SelectedMap = active;
            Minimap.ResetViewCommand.Execute(null);
        }
        else
            UpdateActivePosition();
    }

    private void UpdateActivePosition()
    {
        if (_viewport.ActiveWdtFileDataId == 0)
        {
            Minimap.ActivePosition = null;
            Minimap.ActiveDirection = null;
            return;
        }

        Minimap.ActivePosition = WTEditor.Application.Geometry.MapCoordinates.TerrainToTile(
            _viewport.CameraPosition.X,
            _viewport.CameraPosition.Y);
        Minimap.ActiveDirection = new WTEditor.Application.Geometry.TilePoint(
            -_viewport.CameraDirection.Y,
            -_viewport.CameraDirection.X);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedExpansionChanged(MapFilterOption? value) => ApplyFilters();
    partial void OnSelectedMapTypeChanged(MapFilterOption? value) => ApplyFilters();
    partial void OnShowMapsWithoutTerrainChanged(bool value) => ApplyFilters();

    private void ApplyFilters()
    {
        var search = SearchText.Trim();
        var filtered = _allMaps.Where(map =>
                (SelectedExpansion?.Value is not int expansion || map.ExpansionId == expansion)
                && (SelectedMapType?.Value is not int mapType || map.InstanceType == mapType)
                && (ShowMapsWithoutTerrain ? !map.HasTerrain : map.HasTerrain)
                && (string.IsNullOrEmpty(search)
                    || map.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || map.Id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(map => map.Id)
            .ToArray();

        FilteredMaps.Clear();
        foreach (var map in filtered)
            FilteredMaps.Add(map);

        if (SelectedMap != null && !FilteredMaps.Contains(SelectedMap))
            SelectedMap = null;

        NotifyMapListChanged();
    }

    partial void OnIsLoadingChanged(bool value) => NotifyMapListChanged();

    private void NotifyMapListChanged()
    {
        OnPropertyChanged(nameof(HasFilteredMaps));
        OnPropertyChanged(nameof(HasNoFilteredMaps));
    }

    private static WorldMapListItem CreateMapListItem(WorldMapCatalogEntry entry)
    {
        var map = entry.Map;
        var iconIndex = Math.Abs(map.ExpansionId) % ExpansionIcons.Length;
        var brushIndex = Math.Abs(map.ExpansionId) % ExpansionBrushes.Length;

        return new WorldMapListItem(
            map.Id,
            map.Name,
            map.ExpansionId,
            GetExpansionName(map.ExpansionId),
            map.InstanceType,
            GetMapTypeName(map.InstanceType),
            entry.HasTerrain,
            ExpansionIcons[iconIndex],
            ExpansionBrushes[brushIndex]);
    }

    private static string GetExpansionName(int expansionId) => expansionId switch
    {
        0 => "Classic",
        1 => "The Burning Crusade",
        2 => "Wrath of the Lich King",
        3 => "Cataclysm",
        4 => "Mists of Pandaria",
        5 => "Warlords of Draenor",
        6 => "Legion",
        7 => "Battle for Azeroth",
        8 => "Shadowlands",
        9 => "Dragonflight",
        10 => "The War Within",
        11 => "Midnight",
        _ => $"Expansion {expansionId}"
    };

    private static string GetMapTypeName(int instanceType) => instanceType switch
    {
        0 => "Continent",
        1 => "Dungeon",
        2 => "Raid",
        3 => "Battleground",
        4 => "Arena",
        5 => "Scenario",
        _ => $"Map type {instanceType}"
    };

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _isActive = false;
        _catalogGeneration++;
        _viewport.PropertyChanged -= OnViewportPropertyChanged;
        _viewport.ClientConfigurationChanged -= OnClientConfigurationChanged;
        var cancellation = _loadCancellation;
        _loadCancellation = null;
        cancellation?.Cancel();
        Minimap.Dispose();
        GC.SuppressFinalize(this);
    }
}
