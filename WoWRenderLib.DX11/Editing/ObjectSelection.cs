using WoWRenderLib.DX11.Objects;

namespace WoWRenderLib.DX11.Editing;

/// <summary>Ordered selection; the last selected placement supplies the local axes.</summary>
public sealed class ObjectSelection
{
    private readonly List<Container3D> _objects = [];
    public IReadOnlyList<Container3D> Objects { get; }
    public Container3D? Primary => _objects.Count == 0 ? null : _objects[^1];
    public int Revision { get; private set; }

    public ObjectSelection() => Objects = _objects.AsReadOnly();

    public void Select(Container3D? item, bool additive = false, bool toggle = false)
    {
        if (!additive && !toggle)
            Clear();
        if (item == null)
            return;
        if (toggle && _objects.Contains(item))
        {
            Remove(item);
            return;
        }
        _objects.Remove(item);
        _objects.Add(item);
        item.IsSelected = true;
        Revision++;
    }

    public void Remove(Container3D item)
    {
        if (!_objects.Remove(item))
            return;
        item.IsSelected = false;
        Revision++;
    }

    public void Clear()
    {
        if (_objects.Count == 0)
            return;
        foreach (var item in _objects)
            item.IsSelected = false;
        _objects.Clear();
        Revision++;
    }
}
