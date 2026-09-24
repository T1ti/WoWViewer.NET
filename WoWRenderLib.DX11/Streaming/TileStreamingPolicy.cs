using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Streaming;

internal static class TileStreamingPolicy
{
    public static List<MapTile> BuildDesiredTiles(
        string wdtPath,
        byte centerX,
        byte centerY,
        int loadingDistance,
        IReadOnlySet<int> availableTiles)
        => BuildDesiredTiles(-1, wdtPath, 0, centerX, centerY, loadingDistance, availableTiles);

    public static List<MapTile> BuildDesiredTiles(
        int mapId,
        string? wdtPath,
        uint wdtFileDataId,
        byte centerX,
        byte centerY,
        int loadingDistance,
        IReadOnlySet<int> availableTiles)
    {
        var radius = Math.Clamp(loadingDistance, 0, 32);
        var candidates = new List<MapTile>((radius * 2 + 1) * (radius * 2 + 1));
        BuildDesiredTiles(
            mapId,
            wdtPath,
            wdtFileDataId,
            centerX,
            centerY,
            loadingDistance,
            availableTiles,
            candidates);
        return candidates;
    }

    public static void BuildDesiredTiles(
        int mapId,
        string? wdtPath,
        uint wdtFileDataId,
        byte centerX,
        byte centerY,
        int loadingDistance,
        IReadOnlySet<int> availableTiles,
        List<MapTile> destination)
    {
        ArgumentNullException.ThrowIfNull(availableTiles);
        ArgumentNullException.ThrowIfNull(destination);

        var radius = Math.Clamp(loadingDistance, 0, 32);
        destination.Clear();

        for (var xOffset = -radius; xOffset <= radius; xOffset++)
        {
            for (var yOffset = -radius; yOffset <= radius; yOffset++)
            {
                var tileX = centerX + xOffset;
                var tileY = centerY + yOffset;
                if (tileX is < 0 or > 63 || tileY is < 0 or > 63 ||
                    !availableTiles.Contains(MapTile.GetPositionIndex((byte)tileX, (byte)tileY)))
                {
                    continue;
                }

                destination.Add(new MapTile
                {
                    MapId = mapId,
                    WdtPath = wdtPath ?? string.Empty,
                    WdtFileDataId = wdtFileDataId,
                    TileX = (byte)tileX,
                    TileY = (byte)tileY
                });
            }
        }

        destination.Sort((left, right) =>
        {
            var leftDistance = (left.TileX - centerX) * (left.TileX - centerX) +
                               (left.TileY - centerY) * (left.TileY - centerY);
            var rightDistance = (right.TileX - centerX) * (right.TileX - centerX) +
                                (right.TileY - centerY) * (right.TileY - centerY);
            var byDistance = leftDistance.CompareTo(rightDistance);
            if (byDistance != 0)
                return byDistance;

            var byX = left.TileX.CompareTo(right.TileX);
            return byX != 0 ? byX : left.TileY.CompareTo(right.TileY);
        });
    }
}
