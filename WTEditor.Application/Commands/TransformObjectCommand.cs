using WTEditor.Application.Models;
using WTEditor.Application.Services;

namespace WTEditor.Application.Commands;

public interface IEditorSceneSink
{
    void UpdateObjectTransform(Guid documentId, EditorObjectId objectId, ObjectTransform transform);
}

public sealed class TransformObjectCommand : IEditorCommand
{
    private readonly EditorDocument _document;
    private readonly EditorObjectId _objectId;
    private readonly ObjectTransform _before;
    private readonly ObjectTransform _after;
    private readonly IEditorSceneSink? _sceneSink;

    public string Description { get; }

    public TransformObjectCommand(
        EditorDocument document,
        EditorObjectId objectId,
        ObjectTransform before,
        ObjectTransform after,
        IEditorSceneSink? sceneSink = null,
        string description = "Transform object")
    {
        _document = document;
        _objectId = objectId;
        _before = before;
        _after = after;
        _sceneSink = sceneSink;
        Description = description;
    }

    public void Execute() => Apply(_after);
    public void Undo() => Apply(_before);

    private void Apply(ObjectTransform transform)
    {
        _document.UpdateObjectTransform(_objectId, transform);
        _sceneSink?.UpdateObjectTransform(_document.Id, _objectId, transform);
    }
}
