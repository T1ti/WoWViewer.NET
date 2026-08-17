using System.Buffers.Binary;
using System.IO;
using WoWFormatLib.FileProviders;
using WoWFormatLib.Structs.ADT;
using WoWFormatLib.Structs.WDT;

namespace WoWRenderLib.Loaders;

/// <summary>
/// Finds the largest map-object uniqueId without parsing or retaining the rest of an ADT.
/// </summary>
public static class MapUniqueIdScanner
{
    private const int MddfRecordSize = 36;
    private const int ModfRecordSize = 64;
    private const int UniqueIdOffset = 4;

    public static uint ScanMap(WDT map)
    {
        var maximum = GetMaximumUniqueId(map.modf.entries);

        if (map.tiles == null || map.tileFiles == null)
            return maximum;

        var scannedFiles = new HashSet<uint>();
        var hasSplitAdts = map.mphd.flags.HasFlag(MPHDFlags.wdt_has_maid);

        foreach (var tile in map.tiles)
        {
            if (!map.tileFiles.TryGetValue(tile, out var files))
                continue;

            // Split ADTs keep MDDF/MODF in OBJ0. Older, unsplit ADTs keep them in the root ADT.
            var objectFileDataId = hasSplitAdts ? files.obj0ADT : files.rootADT;
            if (objectFileDataId == 0 || !scannedFiles.Add(objectFileDataId))
                continue;

            if (!FileProvider.FileExists(objectFileDataId))
                continue;

            maximum = Math.Max(maximum, ScanFile(objectFileDataId));
        }

        return maximum;
    }

    public static uint ScanFile(uint fileDataId)
    {
        using var stream = FileProvider.OpenFile(fileDataId);
        return ScanStream(stream);
    }

    /// <summary>
    /// Scans an ADT stream. This is public so callers with their own file provider can reuse the
    /// allocation-light chunk scanner without constructing a full ADTReader result.
    /// </summary>
    public static uint ScanStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var maximum = 0u;
        var header = new byte[8];
        var discard = new byte[8192];

        while (TryReadChunkHeader(stream, header))
        {
            var chunkName = (ADTChunks)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));

            switch (chunkName)
            {
                case ADTChunks.MDDF:
                    maximum = Math.Max(maximum, ScanPlacementChunk(stream, chunkSize, MddfRecordSize, discard));
                    break;
                case ADTChunks.MODF:
                    maximum = Math.Max(maximum, ScanPlacementChunk(stream, chunkSize, ModfRecordSize, discard));
                    break;
                default:
                    Skip(stream, chunkSize, discard);
                    break;
            }
        }

        return maximum;
    }

    private static uint ScanPlacementChunk(Stream stream, uint chunkSize, int recordSize, byte[] discard)
    {
        if (chunkSize % recordSize != 0)
            throw new InvalidDataException($"Placement chunk size {chunkSize} is not a multiple of {recordSize}.");

        var maximum = 0u;
        var recordCount = chunkSize / recordSize;
        var uniqueIdBytes = new byte[sizeof(uint)];

        for (var i = 0u; i < recordCount; i++)
        {
            Skip(stream, UniqueIdOffset, discard);
            ReadExactly(stream, uniqueIdBytes);
            maximum = Math.Max(maximum, BinaryPrimitives.ReadUInt32LittleEndian(uniqueIdBytes));
            Skip(stream, (uint)(recordSize - UniqueIdOffset - sizeof(uint)), discard);
        }

        return maximum;
    }

    private static uint GetMaximumUniqueId(MODFEntry[]? entries)
    {
        var maximum = 0u;
        if (entries == null)
            return maximum;

        foreach (var entry in entries)
            maximum = Math.Max(maximum, entry.uniqueId);

        return maximum;
    }

    private static bool TryReadChunkHeader(Stream stream, byte[] header)
    {
        var firstByte = stream.ReadByte();
        if (firstByte < 0)
            return false;

        header[0] = (byte)firstByte;
        ReadExactly(stream, header.AsSpan(1));
        return true;
    }

    private static void Skip(Stream stream, uint count, byte[] discard)
    {
        if (count == 0)
            return;

        if (stream.CanSeek)
        {
            var target = checked(stream.Position + count);
            if (target > stream.Length)
                throw new InvalidDataException("ADT chunk extends beyond the end of the stream.");

            stream.Position = target;
            return;
        }

        var remaining = count;
        while (remaining > 0)
        {
            var bytesToRead = (int)Math.Min((uint)discard.Length, remaining);
            ReadExactly(stream, discard.AsSpan(0, bytesToRead));
            remaining -= (uint)bytesToRead;
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        stream.ReadExactly(buffer);
    }
}
