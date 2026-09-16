using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace WoWRenderLib.Services;

public static class WdtChunkDiagnostics
{
    private static readonly HashSet<string> HandledRootChunks =
        ["MVER", "MPHD", "MAIN", "MWMO", "MODF", "MAID", "MANM"];

    public static IReadOnlyList<string> FindUnhandledChunks(ReadOnlySpan<byte> data)
    {
        var result = new List<string>();
        if (data.Length < 8)
            return result;

        var reverseFourCc = ReadFourCc(data, 0, reverse: true) == "MVER";
        var offset = 0;
        while (offset <= data.Length - 8)
        {
            var fourCc = ReadFourCc(data, offset, reverseFourCc);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
            var nextOffset = (long)offset + 8 + size;
            if (nextOffset > data.Length)
                break;

            if (!HandledRootChunks.Contains(fourCc))
                result.Add(fourCc);

            offset = (int)nextOffset;
        }

        return result;
    }

    public static void WarnAboutUnhandledChunks(uint wdtFileDataId, ReadOnlySpan<byte> data)
    {
        var chunks = FindUnhandledChunks(data);
        if (chunks.Count == 0)
            return;

        var summary = string.Join(", ", chunks
            .GroupBy(chunk => chunk)
            .Select(group => group.Count() == 1 ? group.Key : $"{group.Key} ({group.Count()} occurrences)"));
        var message = $"Warning: WDT {wdtFileDataId} contains unhandled chunk(s): {summary}.";
        Console.Error.WriteLine(message);
        Trace.TraceWarning(message);
    }

    private static string ReadFourCc(ReadOnlySpan<byte> data, int offset, bool reverse)
    {
        Span<byte> bytes = stackalloc byte[4];
        data.Slice(offset, 4).CopyTo(bytes);
        if (reverse)
            bytes.Reverse();
        return Encoding.ASCII.GetString(bytes);
    }
}
