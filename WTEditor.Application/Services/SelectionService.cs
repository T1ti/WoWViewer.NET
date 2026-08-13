using WTEditor.Application.Models;

namespace WTEditor.Application.Services;

public sealed record SelectionSnapshot(IReadOnlyList<EditorObjectId> ObjectIds, EditorObjectId? Primary)
{
    public static SelectionSnapshot Empty { get; } = new(Array.Empty<EditorObjectId>(), null);
}

public sealed class SelectionService
{
    public SelectionSnapshot Current { get; private set; } = SelectionSnapshot.Empty;
    public event EventHandler<SelectionSnapshot>? SelectionChanged;

    public void Select(EditorObjectId id) => Set([id], id);

    public void Set(IEnumerable<EditorObjectId> ids, EditorObjectId? primary = null)
    {
        var unique = ids.Distinct().ToArray();
        if (primary.HasValue && !unique.Contains(primary.Value))
            throw new ArgumentException("The primary object must be part of the selection.", nameof(primary));

        var nextPrimary = primary ?? (unique.Length > 0 ? unique[0] : null);
        var next = new SelectionSnapshot(Array.AsReadOnly(unique), nextPrimary);
        if (Current.ObjectIds.SequenceEqual(next.ObjectIds) && Current.Primary == next.Primary)
            return;

        Current = next;
        SelectionChanged?.Invoke(this, next);
    }

    public void Clear()
    {
        if (Current.ObjectIds.Count == 0)
            return;

        Current = SelectionSnapshot.Empty;
        SelectionChanged?.Invoke(this, Current);
    }
}
