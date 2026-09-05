using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WoWLib;
using WoWRenderLib.Services;
using WTEditor.Avalonia.Models;
using WTEditor.Application.Geometry;

namespace WTEditor.Avalonia.Services;

public sealed record MinimapTile(WorldMapTile Position, Bitmap Image);
public sealed record WmoMinimapImage(int GroupIndex, TilePoint TopLeft, TilePoint TopRight,
    TilePoint BottomLeft, Bitmap Image, int OffsetX = 0, int OffsetY = 0);

public sealed class MinimapDocument(IReadOnlyList<MinimapTile> tiles, int missingTiles) : IDisposable
{
    public const int OverviewTileSize = 32;
    public IReadOnlyList<MinimapTile> Tiles { get; } = tiles;
    public int MissingTiles { get; } = missingTiles;
    public Bitmap? Overview { get; init; }
    public IReadOnlyList<WmoMinimapImage> WmoImages { get; init; } = [];
    private readonly Dictionary<(int, int), Bitmap> _tileImages =
        tiles.ToDictionary(tile => (tile.Position.X, tile.Position.Y), tile => tile.Image);
    public Bitmap? GetTile(int x, int y) => _tileImages.GetValueOrDefault((x, y));
    public void Dispose()
    {
        Overview?.Dispose();
        foreach (var image in WmoImages) image.Image.Dispose();
        foreach (var tile in Tiles)
            tile.Image.Dispose();
    }
}

public interface IMinimapService
{
    Task<MinimapDocument> LoadAsync(WorldMapCatalogEntry map, CancellationToken cancellationToken);
}

public sealed class MinimapService : IMinimapService
{
    private readonly IWmoMinimapLoader _wmoLoader;
    public MinimapService(IWmoMinimapLoader? wmoLoader = null) => _wmoLoader = wmoLoader ?? new WmoMinimapLoader();
    public static string GetTilePath(string directory, WorldMapTile tile) =>
        $"world/minimaps/{directory}/map{tile.X:D2}_{tile.Y:D2}.blp";

    public Task<MinimapDocument> LoadAsync(WorldMapCatalogEntry map, CancellationToken cancellationToken) =>
        Task.Run(() => Load(map, cancellationToken), cancellationToken);

    private MinimapDocument Load(WorldMapCatalogEntry map, CancellationToken token)
    {
        if (!map.HasTerrain) return LoadWmo(map.Wdt.FileDataId, token);
        var tiles = new List<MinimapTile>();
        var missing = 0;
        const int overviewSize = 64 * MinimapDocument.OverviewTileSize;
        var overviewPixels = new byte[overviewSize * overviewSize * 4];
        try
        {
            foreach (var position in map.Wdt.ActiveTiles)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var blp = new WoWLib.Formats.BLP.BLP();
                    blp.Read(WowlibFileSystem.Current, new FileKey(GetTilePath(map.Map.Directory, position)));
                    // Bound memory for large continents, retaining a useful detail level when zoomed.
                    uint mip = 0;
                    while (mip + 1 < blp.MipCount && blp.MipWidth(mip) > 128)
                        mip++;
                    using var image = blp.Decode(mip);
                    var pixels = image.Pixels.AsSpan().ToArray();
                    CopyOverviewTile(overviewPixels, pixels, (int)image.Width, (int)image.Height, position);
                    var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                    try
                    {
                        var bitmap = new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Unpremul,
                            handle.AddrOfPinnedObject(), new PixelSize((int)image.Width, (int)image.Height),
                            new Vector(96, 96), checked((int)image.Width * 4));
                        tiles.Add(new MinimapTile(position, bitmap));
                    }
                    finally { handle.Free(); }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    missing++;
                    System.Diagnostics.Debug.WriteLine($"Minimap {position}: {exception.Message}");
                }
            }
            token.ThrowIfCancellationRequested();
            var overviewHandle = GCHandle.Alloc(overviewPixels, GCHandleType.Pinned);
            try
            {
                return new MinimapDocument(tiles, missing)
                {
                    Overview = new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Unpremul,
                        overviewHandle.AddrOfPinnedObject(), new PixelSize(overviewSize, overviewSize),
                        new Vector(96, 96), overviewSize * 4)
                };
            }
            finally { overviewHandle.Free(); }
        }
        catch
        {
            foreach (var tile in tiles)
                tile.Image.Dispose();
            throw;
        }
    }

    private MinimapDocument LoadWmo(uint wdtFileDataId, CancellationToken token)
    {
        var data = _wmoLoader.Load(wdtFileDataId, token);
        var images = new List<WmoMinimapImage>();
        try
        {
            // Draw lower groups first, with stable ordering for all tiles in each group.
            foreach (var group in data.Groups.OrderBy(group => group.Maximum.Z).ThenBy(group => group.GroupIndex).ThenBy(group => group.OffsetX).ThenBy(group => group.OffsetY))
            {
                token.ThrowIfCancellationRequested();
                var span = (float)MapCoordinates.WmoMinimapSpan(group.Maximum.X - group.Minimum.X,
                    group.Maximum.Y - group.Minimum.Y);
                var topLeft = new System.Numerics.Vector3(group.Minimum.X + group.OffsetX * span, group.Minimum.Y + (group.OffsetY + 1) * span, group.Maximum.Z);
                var topRight = topLeft + new System.Numerics.Vector3(span, 0, 0);
                var bottomLeft = topLeft - new System.Numerics.Vector3(0, span, 0);
                var handle = GCHandle.Alloc(group.Pixels, GCHandleType.Pinned);
                try
                {
                    var bitmap = new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Unpremul,
                        handle.AddrOfPinnedObject(), new PixelSize(group.Width, group.Height),
                        new Vector(96, 96), checked(group.Width * 4));
                    images.Add(new(group.GroupIndex,
                        MapCoordinates.ModelToTile(topLeft, data.Position, data.Rotation, data.Scale),
                        MapCoordinates.ModelToTile(topRight, data.Position, data.Rotation, data.Scale),
                        MapCoordinates.ModelToTile(bottomLeft, data.Position, data.Rotation, data.Scale), bitmap, group.OffsetX, group.OffsetY));
                }
                finally { handle.Free(); }
            }
            token.ThrowIfCancellationRequested();
            return new MinimapDocument([], data.MissingTextures) { WmoImages = images };
        }
        catch
        {
            foreach (var image in images) image.Image.Dispose();
            throw;
        }
    }

    internal static void CopyOverviewTile(byte[] target, byte[] source, int width, int height, WorldMapTile tile)
    {
        const int size = MinimapDocument.OverviewTileSize;
        const int stride = 64 * size * 4;
        // Box filter once on the loader thread; pan/zoom never resamples on the UI thread.
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var startX = x * width / size;
            var startY = y * height / size;
            var endX = Math.Max(startX + 1, (x + 1) * width / size);
            var endY = Math.Max(startY + 1, (y + 1) * height / size);
            var count = (endX - startX) * (endY - startY);
            var destination = (tile.Y * size + y) * stride + (tile.X * size + x) * 4;
            for (var channel = 0; channel < 4; channel++)
            {
                var sum = 0;
                for (var sy = startY; sy < endY; sy++)
                for (var sx = startX; sx < endX; sx++)
                    sum += source[(sy * width + sx) * 4 + channel];
                target[destination + channel] = (byte)(sum / count);
            }
        }
    }
}
