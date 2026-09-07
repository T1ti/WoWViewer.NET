using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Streaming;

internal static class TileStreamingPolicy
{
    public static List<MapTile> BuildDesiredTiles(
        uint wdtFileDataId,
        byte centerX,
        byte centerY,
        int loadingDistance,
        IReadOnlySet<(byte X, byte Y)> availableTiles)
    {
        var radius = Math.Clamp(loadingDistance, 0, 32);
        var candidates = new List<(MapTile Tile, int DistanceSquared)>((radius * 2 + 1) * (radius * 2 + 1));

        for (var xOffset = -radius; xOffset <= radius; xOffset++)
        {
            for (var yOffset = -radius; yOffset <= radius; yOffset++)
            {
                var tileX = centerX + xOffset;
                var tileY = centerY + yOffset;
                if (tileX is < 0 or > 63 || tileY is < 0 or > 63 ||
                    !availableTiles.Contains(((byte)tileX, (byte)tileY)))
                {
                    continue;
                }

                candidates.Add((new MapTile
                {
                    wdtFileDataID = wdtFileDataId,
                    tileX = (byte)tileX,
                    tileY = (byte)tileY
                }, xOffset * xOffset + yOffset * yOffset));
            }
        }

        candidates.Sort(static (left, right) =>
        {
            var byDistance = left.DistanceSquared.CompareTo(right.DistanceSquared);
            if (byDistance != 0)
                return byDistance;

            var byX = left.Tile.tileX.CompareTo(right.Tile.tileX);
            return byX != 0 ? byX : left.Tile.tileY.CompareTo(right.Tile.tileY);
        });

        return candidates.Select(static candidate => candidate.Tile).ToList();
    }
}
