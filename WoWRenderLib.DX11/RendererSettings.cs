namespace WoWRenderLib.DX11;

using System.Numerics;
using System.Text.Json.Serialization;

public sealed class RendererSettings
{
    // Reference-noon colors from the 3.3.5 client lighting profile. They are
    // stored as linear RGB-style factors and serialized with the other
    // renderer settings so a future settings UI can edit them without a
    // renderer API change.
    private Vector3 _ambientColor = new(104f / 255f, 130f / 255f, 154f / 255f);
    private Vector3 _diffuseColor = new(1f, 136f / 255f, 0f);

    [JsonIgnore]
    public Vector3 AmbientColor { get => _ambientColor; set => _ambientColor = value; }

    [JsonIgnore]
    public Vector3 DiffuseColor { get => _diffuseColor; set => _diffuseColor = value; }

    // Vector3 is field-based in System.Numerics and is intentionally not
    // globally enabled in the editor's JSON options. Keep the persisted form
    // explicit and portable instead of relying on serializer implementation
    // details.
    public float AmbientColorR { get => _ambientColor.X; set => _ambientColor.X = value; }
    public float AmbientColorG { get => _ambientColor.Y; set => _ambientColor.Y = value; }
    public float AmbientColorB { get => _ambientColor.Z; set => _ambientColor.Z = value; }
    public float DiffuseColorR { get => _diffuseColor.X; set => _diffuseColor.X = value; }
    public float DiffuseColorG { get => _diffuseColor.Y; set => _diffuseColor.Y = value; }
    public float DiffuseColorB { get => _diffuseColor.Z; set => _diffuseColor.Z = value; }

    public float TerrainRenderDistance { get; set; } = 20_000f;
    public float ModelRenderDistance { get; set; } = 20_000f;
    public int TileLoadingDistance { get; set; } = 4;
    public float MovementSpeed { get; set; } = 150f;
    public float MouseSensitivity { get; set; } = 0.1f;

    public bool RenderADT { get; set; } = true;
    public bool RenderWMO { get; set; } = true;
    public bool RenderM2 { get; set; } = true;
    public bool ShowBoundingBoxes { get; set; }
    public bool ShowBoundingSpheres { get; set; }

    public RendererSettings Clone() => new()
    {
        AmbientColor = AmbientColor,
        DiffuseColor = DiffuseColor,
        TerrainRenderDistance = TerrainRenderDistance,
        ModelRenderDistance = ModelRenderDistance,
        TileLoadingDistance = TileLoadingDistance,
        MovementSpeed = MovementSpeed,
        MouseSensitivity = MouseSensitivity,
        RenderADT = RenderADT,
        RenderWMO = RenderWMO,
        RenderM2 = RenderM2,
        ShowBoundingBoxes = ShowBoundingBoxes,
        ShowBoundingSpheres = ShowBoundingSpheres
    };
}
