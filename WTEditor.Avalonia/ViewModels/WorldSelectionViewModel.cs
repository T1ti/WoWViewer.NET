using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WTEditor.Application.Models;
using WTEditor.Avalonia.Models;
using WTEditor.Avalonia.Services;

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
        Geometry.Parse("M12,2 C6.5,2 2,6.5 2,12 C2,17.5 6.5,22 12,22 C17.5,22 22,17.5 22,12 C22,6.5 17.5,2 12,2 Z"),
        Geometry.Parse("M12,2 L22,12 L12,22 L2,12 Z"),
        Geometry.Parse("M12,2 L20.5,7 L20.5,17 L12,22 L3.5,17 L3.5,7 Z"),
        Geometry.Parse("M12,2 L21,6 L19,20 L12,23 L5,20 L3,6 Z"),
        Geometry.Parse("M12,2 L14.7,8.3 L21.5,8.9 L16.4,13.4 L18,20 L12,16.4 L6,20 L7.6,13.4 L2.5,8.9 L9.3,8.3 Z")
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
    private readonly List<WorldMapListItem> _allMaps = [];
    private CancellationTokenSource? _loadCancellation;
    private bool _isActive;
    private bool _isContentReady;
    private bool _isMapsLoaded;
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
        Minimap = new MinimapViewModel(minimapService ?? new MinimapService());
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
        _isActive = true;
        _isContentReady |= _viewport.RendererState == RendererLifecycleState.Ready;

        if (_viewport.RendererState == RendererLifecycleState.Failed)
            StatusMessage = "Map data is unavailable because the WoW client could not be loaded.";

        return LoadMapCatalogIfReadyAsync();
    }

    private void OnViewportPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
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
        FilteredMaps.Clear();
        SelectedMap = null;
        ExpansionFilters = [new(null, "All expansions")];
        MapTypeFilters = [new(null, "All map types")];
        SelectedExpansion = ExpansionFilters[0];
        SelectedMapType = MapTypeFilters[0];
        StatusMessage = "Map data will refresh after the new WoW client finishes loading.";
        NotifyMapListChanged();
    }

    private async Task LoadMapCatalogIfReadyAsync()
    {
        if (!_isActive || !_isContentReady || _isMapsLoaded || IsLoading)
            return;

        var generation = _catalogGeneration;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();

        IsLoading = true;
        StatusMessage = "Reading Map DB2...";
        NotifyMapListChanged();

        try
        {
            var maps = await _mapCatalogService.LoadAsync(_loadCancellation.Token);
            if (generation != _catalogGeneration)
                return;

            _allMaps.Clear();
            _mapEntries.Clear();
            foreach (var map in maps)
                _mapEntries[map.Map.Id] = map;
            _allMaps.AddRange(maps.Select(CreateMapListItem));
            UpdateFilterOptions();
            ApplyFilters();
            _isMapsLoaded = true;
            StatusMessage = $"{_allMaps.Count:N0} maps and WDT headers loaded.";
        }
        catch (OperationCanceledException) when (generation != _catalogGeneration)
        {
            // A client change superseded this request.
        }
        catch (Exception exception)
        {
            if (generation == _catalogGeneration)
                StatusMessage = $"Unable to read Map DB2: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
            NotifyMapListChanged();

            if (generation != _catalogGeneration)
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

    partial void OnSelectedMapChanged(WorldMapListItem? value) =>
        Minimap.SelectMap(value != null && _mapEntries.TryGetValue(value.Id, out var map) ? map : null);

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
        _viewport.PropertyChanged -= OnViewportPropertyChanged;
        _viewport.ClientConfigurationChanged -= OnClientConfigurationChanged;
        _loadCancellation?.Cancel();
        Minimap.Dispose();
    }
}
