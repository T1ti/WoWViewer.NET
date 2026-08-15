using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private EditorObjectSnapshot? _selection;
    private ClientBuildProfile _buildProfile = new(string.Empty, false);
    private bool _synchronizingTransform;

    public event EventHandler<ObjectTransform>? TransformChanged;
    public event EventHandler<WmoPlacementSelection>? WmoPlacementChanged;

    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string _selectionName = "Nothing selected";
    [ObservableProperty] private string _selectionKind = "Click an object in the viewport to inspect it.";
    [ObservableProperty] private IReadOnlyList<InspectorSectionViewModel> _sections = [];
    [ObservableProperty] private double _positionX;
    [ObservableProperty] private double _positionY;
    [ObservableProperty] private double _positionZ;
    [ObservableProperty] private double _rotationX;
    [ObservableProperty] private double _rotationY;
    [ObservableProperty] private double _rotationZ;
    [ObservableProperty] private double _uniformScale = 1d;
    [ObservableProperty] private bool _isScaleEditable = true;
    [ObservableProperty] private string _scaleHelpText = string.Empty;
    [ObservableProperty] private bool _isPanelVisible = true;
    public ModelInformationViewModel ModelInformation { get; } = new();
    public PlacementInformationViewModel PlacementInformation { get; } = new();

    public SelectionInspectorViewModel(IEnumerable<IObjectInspectorSectionProvider> providers)
    {
        _providers = providers.ToArray();
        PlacementInformation.WmoPlacementChanged += (_, selection) =>
            WmoPlacementChanged?.Invoke(this, selection);
    }

    public void SetBuildProfile(ClientBuildProfile profile)
    {
        _buildProfile = profile;
        UpdateScaleBehavior();
    }

    public void Inspect(EditorObjectSnapshot? selection)
    {
        _selection = selection;
        HasSelection = selection != null;
        SelectionName = selection?.Name ?? "Nothing selected";
        SelectionKind = selection?.Kind ?? "Click an object in the viewport to inspect it.";
        ModelInformation.SetModel(selection);
        PlacementInformation.SetPlacement(selection);
        Sections = selection == null
            ? []
            : _providers.Where(provider => provider.Supports(selection))
                .Select(provider => provider.CreateSection(selection))
                .ToArray();

        _synchronizingTransform = true;
        try
        {
            var transform = selection?.Transform ?? ObjectTransform.Identity;
            var rotation = ToEulerDegrees(transform.Rotation);
            PositionX = transform.Position.X;
            PositionY = transform.Position.Y;
            PositionZ = transform.Position.Z;
            RotationX = rotation.X;
            RotationY = rotation.Y;
            RotationZ = rotation.Z;
            UniformScale = transform.Scale.X;
        }
        finally
        {
            _synchronizingTransform = false;
        }

        UpdateScaleBehavior();
    }

    [RelayCommand]
    private void TogglePanel() => IsPanelVisible = !IsPanelVisible;

    partial void OnPositionXChanged(double value) => PublishTransform();
    partial void OnPositionYChanged(double value) => PublishTransform();
    partial void OnPositionZChanged(double value) => PublishTransform();
    partial void OnRotationXChanged(double value) => PublishTransform();
    partial void OnRotationYChanged(double value) => PublishTransform();
    partial void OnRotationZChanged(double value) => PublishTransform();
    partial void OnUniformScaleChanged(double value) => PublishTransform();

    private void PublishTransform()
    {
        if (_synchronizingTransform || _selection == null)
            return;

        var scale = IsScaleEditable ? Math.Max(0.001d, UniformScale) : 1d;
        var rotationRadians = new Vector3(
            (float)RotationX,
            (float)RotationY,
            (float)RotationZ) * (MathF.PI / 180f);
        TransformChanged?.Invoke(this, new ObjectTransform(
            new Vector3((float)PositionX, (float)PositionY, (float)PositionZ),
            Quaternion.CreateFromYawPitchRoll(
                rotationRadians.Y,
                rotationRadians.X,
                rotationRadians.Z),
            new Vector3((float)scale)));
    }

    private void UpdateScaleBehavior()
    {
        IsScaleEditable = _selection == null || _buildProfile.CanEditScale(_selection);
        ScaleHelpText = IsScaleEditable
            ? string.Empty
            : "WMO scale is fixed at 1.0 for classic clients.";
        if (!IsScaleEditable && UniformScale != 1d)
        {
            _synchronizingTransform = true;
            UniformScale = 1d;
            _synchronizingTransform = false;
        }
    }

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
        return new Vector3(pitch, yaw, roll) * (180f / MathF.PI);
    }
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
            new("Asset state", data.IsLoaded ? "Loaded" : "Loading"),
            new("Save state", data.IsModified ? "Modified" : "Unchanged")
        ]);
    }
}
