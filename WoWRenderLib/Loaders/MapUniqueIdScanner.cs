using System.Buffers.Binary;
using WoWLib;
using WoWRenderLib.Services;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Loaders;

/// <summary>
/// Finds the largest map-object uniqueId without parsing or retaining the rest of an ADT.
/// </summary>
public static class MapUniqueIdScanner
{
    private const int MddfRecordSize = 36;
    private const int ModfRecordSize = 64;
    private const int UniqueIdOffset = 4;

    private static readonly uint Mddf = FourCc("MDDF");
    private static readonly uint Modf = FourCc("MODF");

    public static uint ScanMap(WdtFile map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var maximum = 0u;
        var fileSystem = WowlibFileSystem.Current;
        var scannedFiles = new HashSet<uint>();

        foreach (var tile in map.Tiles)
        {
            if (!map.TryGetTile(tile.tileX, tile.tileY, out var files))
                continue;

            var objectFileDataId = map.HasSplitAdts ? files.Obj0Adt : files.RootAdt;
            if (objectFileDataId == 0 || !scannedFiles.Add(objectFileDataId))
                continue;

            if (!fileSystem.Exists(new FileKey(new FileDataId(objectFileDataId))))
                continue;

            maximum = Math.Max(maximum, ScanFile(objectFileDataId));
        }

        return maximum;
    }

    public static uint ScanFile(uint fileDataId)
    {
        using var stream = new MemoryStream(
            WowlibFileSystem.Current.ReadFile(new FileKey(new FileDataId(fileDataId))),
            writable: false);
        return ScanStream(stream);
    }

    /// <summary>
    /// Scans an ADT stream. This is public so callers with their own stream can reuse the
    /// allocation-light chunk scanner without constructing a full wowlib ADT result.
    /// </summary>
    public static uint ScanStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var maximum = 0u;
        var header = new byte[8];
        var discard = new byte[8192];

        while (TryReadChunkHeader(stream, header))
        {
            var chunkName = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));

            switch (chunkName)
            {
                case var value when value == Mddf:
                    maximum = Math.Max(maximum, ScanPlacementChunk(stream, chunkSize, MddfRecordSize, discard));
                    break;
                case var value when value == Modf:
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

    private static uint FourCc(string value) =>
        BinaryPrimitives.ReadUInt32LittleEndian(System.Text.Encoding.ASCII.GetBytes(value));

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

    private static void ReadExactly(Stream stream, Span<byte> buffer) => stream.ReadExactly(buffer);
}
