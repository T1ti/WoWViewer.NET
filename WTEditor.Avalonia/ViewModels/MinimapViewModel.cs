using WTEditor.Application.Geometry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTEditor.Avalonia.Models;
using WTEditor.Avalonia.Services;

namespace WTEditor.Avalonia.ViewModels;

public partial class MinimapViewModel(IMinimapService service) : ViewModelBase, IDisposable
{
    public const double MinimumZoom = 64d / 65;
    public const double MaximumZoom = 64;

    private CancellationTokenSource? _cancellation;
    private IReadOnlyList<WorldMapTile> _activeTiles = [];
    [ObservableProperty] private MinimapDocument? _document;
    [ObservableProperty] private string _status = "Select a map to view its minimap.";
    [ObservableProperty] private double _zoom = MinimumZoom;
    [ObservableProperty] private double _offsetX = (1 - MinimumZoom) / 2;
    [ObservableProperty] private double _offsetY = (1 - MinimumZoom) / 2;
    [ObservableProperty] private TileBounds? _globalWmoBounds;

    public void SelectMap(WorldMapCatalogEntry? map) => _ = LoadAsync(map);

    internal async Task LoadAsync(WorldMapCatalogEntry? map)
    {
        _cancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var previous = Document;
        Document = null;
        previous?.Dispose();
        _activeTiles = map?.Wdt.ActiveTiles ?? [];
        GlobalWmoBounds = map is { HasTerrain: false } ? map.Wdt.GlobalWmoBounds : null;
        ResetView();
        Status = map == null ? "Select a map to view its minimap." : $"Loading {map.Map.Name} minimap…";
        try
        {
            if (map == null) return;
            if (map.HasTerrain && map.Wdt.ActiveTiles.Count == 0)
            {
                Status = "This map has no active terrain tiles or its WDT is unavailable.";
                return;
            }
            var document = await service.LoadAsync(map, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                document.Dispose();
                return;
            }
            Document = document;
            Status = map.HasTerrain
                ? $"{map.Map.Name} · {document.Tiles.Count} tiles loaded · {document.MissingTiles} unavailable"
                : $"{map.Map.Name} · {document.WmoImages.Count} WMO minimap tiles · {document.MissingTiles} unavailable";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!cancellation.IsCancellationRequested)
                Status = $"Unable to load minimap: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            cancellation.Dispose();
        }
    }

    [RelayCommand]
    private void ResetView()
    {
        if (_activeTiles.Count == 0 && GlobalWmoBounds == null)
        {
            Zoom = MinimumZoom;
            OffsetX = OffsetY = (1 - Zoom) / 2;
            return;
        }

        var minX = GlobalWmoBounds?.MinX ?? _activeTiles.Min(tile => tile.X);
        var minY = GlobalWmoBounds?.MinY ?? _activeTiles.Min(tile => tile.Y);
        // Include the complete last tile, rather than fitting only its origin.
        var maxX = GlobalWmoBounds?.MaxX ?? _activeTiles.Max(tile => tile.X) + 1;
        var maxY = GlobalWmoBounds?.MaxY ?? _activeTiles.Max(tile => tile.Y) + 1;
        Zoom = Math.Clamp(64d / Math.Max(1, Math.Max(maxX - minX, maxY - minY)), MinimumZoom, MaximumZoom);
        OffsetX = 0.5 - (minX + maxX) * Zoom / 128;
        OffsetY = 0.5 - (minY + maxY) * Zoom / 128;
        ClampOffsets();
    }

    public void ClampOffsets()
    {
        // Half a tile beyond each of the four world edges, at the current zoom.
        Zoom = Math.Clamp(Zoom, MinimumZoom, MaximumZoom);
        if (Zoom <= MinimumZoom)
        {
            OffsetX = OffsetY = (1 - Zoom) / 2;
            return;
        }
        var padding = Zoom / 128;
        OffsetX = Math.Clamp(OffsetX, 1 - Zoom - padding, padding);
        OffsetY = Math.Clamp(OffsetY, 1 - Zoom - padding, padding);
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        var document = Document;
        Document = null;
        document?.Dispose();
    }
}
