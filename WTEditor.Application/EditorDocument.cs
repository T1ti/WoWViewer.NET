using WTEditor.Application.Models;

namespace WTEditor.Application;

public sealed class EditorDocument
{
    private readonly Dictionary<EditorObjectId, EditorObjectSnapshot> _objects = [];
    private readonly HashSet<EditorObjectId> _changedObjects = [];

    public Guid Id { get; }
    public string Name { get; private set; }
    public string? Source { get; }
    public EditorDocumentState State { get; private set; } = EditorDocumentState.New;
    public bool IsDirty { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<EditorObjectSnapshot> Objects => _objects.Values;
    public IReadOnlyCollection<EditorObjectId> ChangedObjects => _changedObjects;

    public event EventHandler<DocumentChange>? Changed;

    public EditorDocument(string name, string? source = null, Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id ?? Guid.NewGuid();
        Name = name;
        Source = source;
    }

    public void SetState(EditorDocumentState state)
    {
        if (State == state)
            return;

        State = state;
        Notify(new DocumentChange(DocumentChangeKind.State), marksDirty: false);
    }

    public bool TryGetObject(EditorObjectId id, out EditorObjectSnapshot snapshot) =>
        _objects.TryGetValue(id, out snapshot!);

    public void AddObject(EditorObjectSnapshot snapshot)
    {
        if (!_objects.TryAdd(snapshot.Id, snapshot))
            throw new InvalidOperationException($"Object {snapshot.Id} already exists in the document.");

        _changedObjects.Add(snapshot.Id);
        Notify(new DocumentChange(DocumentChangeKind.ObjectAdded, snapshot.Id));
    }

    public void UpdateObjectTransform(EditorObjectId id, ObjectTransform transform)
    {
        if (!_objects.TryGetValue(id, out var current))
            throw new KeyNotFoundException($"Object {id} does not exist in the document.");

        if (current.Transform == transform)
            return;

        _objects[id] = current with { Transform = transform };
        _changedObjects.Add(id);
        Notify(new DocumentChange(DocumentChangeKind.ObjectChanged, id));
    }

    public void RemoveObject(EditorObjectId id)
    {
        if (!_objects.Remove(id))
            return;

        _changedObjects.Add(id);
        Notify(new DocumentChange(DocumentChangeKind.ObjectRemoved, id));
    }

    public void MarkSaved()
    {
        IsDirty = false;
        _changedObjects.Clear();
        Changed?.Invoke(this, new DocumentChange(DocumentChangeKind.Saved));
    }

    private void Notify(DocumentChange change, bool marksDirty = true)
    {
        Version++;
        if (marksDirty)
            IsDirty = true;
        Changed?.Invoke(this, change);
    }
}
