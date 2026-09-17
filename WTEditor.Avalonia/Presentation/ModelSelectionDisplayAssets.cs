using WTEditor.Application.Models;

namespace WTEditor.Avalonia.Presentation;

internal static class ModelSelectionDisplayAssets
{
    public static IReadOnlyList<AssetReference> CreateReferences(
        Func<IEnumerable<uint>> getIds)
    {
        try
        {
            return getIds()
                .Where(fileDataId => fileDataId is not 0 and not uint.MaxValue)
                .Distinct()
                .Select(fileDataId => new AssetReference(
                    fileDataId,
                    WoWRenderLib.Listfile.GetDisplayName(fileDataId)))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public static string GetEnumDisplayName<TEnum>(uint value) where TEnum : struct, Enum
    {
        var enumValue = (TEnum)Enum.ToObject(typeof(TEnum), value);
        return Enum.IsDefined(enumValue) ? enumValue.ToString() : value.ToString();
    }
}
