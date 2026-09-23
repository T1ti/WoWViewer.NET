using WTEditor.Application.Models;

namespace WTEditor.Avalonia.Services;

public sealed record ObjectClipboardEntry(
    string Name,
    EditorObjectSnapshot? WorldObject = null,
    string? AssetPath = null,
    uint? FileDataId = null);

/// <summary>Editor-owned object clipboard shared by viewport copy and browser selection.</summary>
public sealed class ObjectClipboardService
{
    public IReadOnlyList<ObjectClipboardEntry> Entries { get; private set; } = [];
    public event EventHandler? Changed;

    public void CopyWorldObjects(IReadOnlyList<EditorObjectSnapshot> objects)
    {
        if (objects.Count == 0)
            return;

        Entries = objects.Select(item => new ObjectClipboardEntry(item.Name, WorldObject: item)).ToArray();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SelectAsset(string path, uint? fileDataId)
    {
        var normalized = path.Replace('\\', '/');
        if (Entries.Count == 1 && Entries[0].AssetPath == normalized &&
            Entries[0].FileDataId == fileDataId)
            return;
        Entries = [new ObjectClipboardEntry(
            Path.GetFileName(normalized), AssetPath: normalized, FileDataId: fileDataId)];
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
