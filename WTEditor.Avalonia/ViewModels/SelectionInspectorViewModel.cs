using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using WTEditor.Application.Models;

namespace WTEditor.Avalonia.ViewModels;

public sealed record InspectorPropertyViewModel(string Label, string Value);

public sealed record InspectorSectionViewModel(
    string Title,
    IReadOnlyList<InspectorPropertyViewModel> Properties);

public interface IObjectInspectorSectionProvider
{
    bool Supports(EditorObjectSnapshot selection);
    InspectorSectionViewModel CreateSection(EditorObjectSnapshot selection);
}

public partial class SelectionInspectorViewModel : ViewModelBase
{
    private readonly IReadOnlyList<IObjectInspectorSectionProvider> _providers;

    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string _selectionName = "Nothing selected";
    [ObservableProperty] private string _selectionKind = "Click an object in the viewport to inspect it.";
    [ObservableProperty] private IReadOnlyList<InspectorSectionViewModel> _sections = [];

    public SelectionInspectorViewModel(IEnumerable<IObjectInspectorSectionProvider> providers)
    {
        _providers = providers.ToArray();
    }

    public void Inspect(EditorObjectSnapshot? selection)
    {
        HasSelection = selection != null;
        SelectionName = selection?.Name ?? "Nothing selected";
        SelectionKind = selection?.Kind ?? "Click an object in the viewport to inspect it.";
        Sections = selection == null
            ? []
            : _providers.Where(provider => provider.Supports(selection))
                .Select(provider => provider.CreateSection(selection))
                .ToArray();
    }
}

public sealed class TransformInspectorSectionProvider : IObjectInspectorSectionProvider
{
    public bool Supports(EditorObjectSnapshot selection) => true;

    public InspectorSectionViewModel CreateSection(EditorObjectSnapshot selection)
    {
        var transform = selection.Transform;
        var rotation = ToEulerDegrees(transform.Rotation);
        return new InspectorSectionViewModel("Transform",
        [
            VectorRow("Position", transform.Position),
            VectorRow("Rotation", rotation, "°"),
            VectorRow("Scale", transform.Scale)
        ]);
    }

    private static InspectorPropertyViewModel VectorRow(string label, Vector3 value, string suffix = "") =>
        new(label, string.Create(CultureInfo.InvariantCulture,
            $"X {value.X:0.###}{suffix}   Y {value.Y:0.###}{suffix}   Z {value.Z:0.###}{suffix}"));

    private static Vector3 ToEulerDegrees(Quaternion quaternion)
    {
        quaternion = Quaternion.Normalize(quaternion);
        var sinPitch = 2f * (quaternion.W * quaternion.X - quaternion.Z * quaternion.Y);
        var pitch = MathF.Abs(sinPitch) >= 1f
            ? MathF.CopySign(MathF.PI / 2f, sinPitch)
            : MathF.Asin(sinPitch);
        var yaw = MathF.Atan2(
            2f * (quaternion.W * quaternion.Y + quaternion.X * quaternion.Z),
            1f - 2f * (quaternion.X * quaternion.X + quaternion.Y * quaternion.Y));
        var roll = MathF.Atan2(
            2f * (quaternion.W * quaternion.Z + quaternion.X * quaternion.Y),
            1f - 2f * (quaternion.X * quaternion.X + quaternion.Z * quaternion.Z));
        const float radiansToDegrees = 180f / MathF.PI;
        return new Vector3(pitch, yaw, roll) * radiansToDegrees;
    }
}

public sealed class M2InspectorSectionProvider : IObjectInspectorSectionProvider
{
    public bool Supports(EditorObjectSnapshot selection) => selection.Data is M2ObjectData;

    public InspectorSectionViewModel CreateSection(EditorObjectSnapshot selection)
    {
        var data = (M2ObjectData)selection.Data!;
        return new InspectorSectionViewModel("M2 model",
        [
            Row("File data ID", data.FileDataId),
            Row("Parent file data ID", data.ParentFileDataId),
            Row("Geosets", data.GeosetCount),
            new("Placement", data.IsWorldModelDoodad ? "WMO doodad" : "World object")
        ]);
    }

    private static InspectorPropertyViewModel Row(string label, object value) => new(label, value.ToString() ?? "—");
}

public sealed class WorldModelInspectorSectionProvider : IObjectInspectorSectionProvider
{
    public bool Supports(EditorObjectSnapshot selection) => selection.Data is WorldModelObjectData;

    public InspectorSectionViewModel CreateSection(EditorObjectSnapshot selection)
    {
        var data = (WorldModelObjectData)selection.Data!;
        return new InspectorSectionViewModel("World model",
        [
            Row("File data ID", data.FileDataId),
            Row("Parent file data ID", data.ParentFileDataId),
            Row("Groups", data.GroupCount),
            Row("Doodad sets", data.DoodadSetCount),
            Row("Active doodads", data.ActiveDoodadCount),
            new("Asset state", data.IsLoaded ? "Loaded" : "Loading")
        ]);
    }

    private static InspectorPropertyViewModel Row(string label, object value) => new(label, value.ToString() ?? "—");
}

public sealed class TerrainInspectorSectionProvider : IObjectInspectorSectionProvider
{
    public bool Supports(EditorObjectSnapshot selection) => selection.Data is TerrainObjectData;

    public InspectorSectionViewModel CreateSection(EditorObjectSnapshot selection)
    {
        var data = (TerrainObjectData)selection.Data!;
        return new InspectorSectionViewModel("Terrain tile",
        [
            new("File data ID", data.FileDataId.ToString()),
            new("Tile", $"{data.TileX}, {data.TileY}"),
            new("Asset state", data.IsLoaded ? "Loaded" : "Loading")
        ]);
    }
}
