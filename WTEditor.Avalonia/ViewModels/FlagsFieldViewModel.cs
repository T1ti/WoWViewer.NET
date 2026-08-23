using System.Text.RegularExpressions;

namespace WTEditor.Avalonia.ViewModels;

public sealed record FlagsFieldViewModel(string Label, ulong Value, IReadOnlyList<string> ActiveFlags)
{
    public string DisplayText => ActiveFlags.Count == 0 ? "None" : string.Join(", ", ActiveFlags);
    public string ToolTip => $"Raw value: 0x{Value:X}";

    public static FlagsFieldViewModel FromEnum<TEnum>(string label, ulong value)
        where TEnum : struct, Enum
    {
        var active = new List<string>();
        ulong knownBits = 0;
        foreach (var enumValue in Enum.GetValues<TEnum>())
        {
            var mask = Convert.ToUInt64(enumValue);
            if (mask == 0 || (value & mask) != mask)
                continue;

            knownBits |= mask;
            active.Add(Humanize(Enum.GetName(enumValue) ?? enumValue.ToString()));
        }

        var unknownBits = value & ~knownBits;
        if (unknownBits != 0)
            active.Add($"Unknown (0x{unknownBits:X})");
        return new FlagsFieldViewModel(label, value, active);
    }

    private static string Humanize(string name)
    {
        var flagMask = Regex.Match(name, "^Flag_(?<mask>0x[0-9a-f]+)(?:_(?<label>.*))?$", RegexOptions.IgnoreCase);
        if (flagMask.Success)
            name = flagMask.Groups["label"].Success
                ? flagMask.Groups["label"].Value
                : $"Unknown {flagMask.Groups["mask"].Value}";

        var words = Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
        return string.Join(' ', words.Split(['_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }
}
