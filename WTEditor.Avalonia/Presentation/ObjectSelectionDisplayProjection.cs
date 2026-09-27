using System.Runtime.CompilerServices;
using WTEditor.Application.Models;
using WoWRenderLib.DX11.Objects;

namespace WTEditor.Avalonia.Presentation;

/// <summary>Publishes immutable group snapshots, retaining stable identities without retaining unloaded objects.</summary>
internal sealed class ObjectSelectionDisplayProjection
{
    private readonly ConditionalWeakTable<Container3D, SelectedObjectDisplayProjection> _projections = new();
    private IReadOnlyList<EditorObjectSnapshot> _last = [];

    public IReadOnlyList<EditorObjectSnapshot> CreateDisplaySnapshots(IReadOnlyList<Container3D> selection)
    {
        EditorObjectSnapshot[]? changed = selection.Count == _last.Count ? null : new EditorObjectSnapshot[selection.Count];
        for (var index = 0; index < selection.Count; index++)
        {
            var snapshot = _projections.GetValue(selection[index], static _ => new SelectedObjectDisplayProjection()).CreateDisplaySnapshot(selection[index])!;
            if (changed == null && snapshot != _last[index])
            {
                changed = new EditorObjectSnapshot[selection.Count];
                for (var previous = 0; previous < index; previous++)
                    changed[previous] = _last[previous];
            }
            if (changed != null)
                changed[index] = snapshot;
        }
        if (changed != null)
            _last = Array.AsReadOnly(changed);
        return _last;
    }

    public void RefreshDisplayData(Container3D item)
    {
        if (_projections.TryGetValue(item, out var projection))
            projection.RefreshDisplayData(item);
    }
}
