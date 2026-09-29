using System.Buffers.Binary;

namespace WoWViewer.ClientShaderTools;

public sealed record BlsPermutation(int Ordinal, int RecordOffset, int PayloadOffset,
    uint Metadata0, uint Metadata1, ushort Metadata2, ushort Metadata3, byte[] Bytecode);

public static class BlsParser
{
    // Build 12340: CGxDevice__IShaderLoad (0x684970) checks these two DWORDs;
    // SFile__ReadLengthPrefixedBytesAligned (0x689A70) reads each record.
    public const uint Magic = 0x47585348; // On-disk ASCII: HSXG.
    public const uint FormatTag = 0x00010003;

    public static IReadOnlyList<BlsPermutation> Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return [];
        if (bytes.Length < 12)
            throw new InvalidDataException("Truncated BLS header.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic)
            throw new InvalidDataException("Invalid BLS magic; expected HSXG.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != FormatTag)
            throw new InvalidDataException("Unsupported BLS format tag; expected 0x00010003.");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        if (count > (uint)((bytes.Length - 12) / 16))
            throw new InvalidDataException("BLS record count exceeds the available record headers.");
        var records = new List<BlsPermutation>((int)count);
        var offset = 12;
        for (var ordinal = 0; ordinal < (int)count; ordinal++)
        {
            if (bytes.Length - offset < 16)
                throw new InvalidDataException($"Truncated BLS record {ordinal} header.");
            var header = bytes.Slice(offset, 16);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
            var paddedLength = ((ulong)length + 3) & ~3UL;
            if (paddedLength > (ulong)(bytes.Length - offset - 16))
                throw new InvalidDataException($"Truncated BLS record {ordinal} payload or alignment padding.");
            records.Add(new BlsPermutation(ordinal, offset, offset + 16,
                BinaryPrimitives.ReadUInt32LittleEndian(header),
                BinaryPrimitives.ReadUInt32LittleEndian(header[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(header[8..]),
                BinaryPrimitives.ReadUInt16LittleEndian(header[10..]),
                bytes.Slice(offset + 16, (int)length).ToArray()));
            offset += 16 + (int)paddedLength;
        }
        if (offset != bytes.Length)
            throw new InvalidDataException($"BLS has {bytes.Length - offset} unexpected trailing bytes.");
        return records;
    }
}
