using System.Numerics;

namespace WTEditor.Application.Models;

public readonly record struct EditorObjectId(Guid Value)
{
    public static EditorObjectId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

public sealed record ObjectTransform(
    Vector3 Position,
    Quaternion Rotation,
    Vector3 Scale)
{
    public static ObjectTransform Identity { get; } =
        new(Vector3.Zero, Quaternion.Identity, Vector3.One);
}

public sealed record EditorObjectSnapshot(
    EditorObjectId Id,
    string Name,
    string Kind,
    ObjectTransform Transform);

public enum EditorDocumentState
{
    New,
    Loading,
    Ready,
    Failed,
    Closed
}

public enum DocumentChangeKind
{
    State,
    ObjectAdded,
    ObjectChanged,
    ObjectRemoved,
    Saved
}

public sealed record DocumentChange(DocumentChangeKind Kind, EditorObjectId? ObjectId = null);
