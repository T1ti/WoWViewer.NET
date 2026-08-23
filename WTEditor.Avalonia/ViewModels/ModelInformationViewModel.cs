using CommunityToolkit.Mvvm.ComponentModel;
using WTEditor.Application.Models;
using M2MaterialFlags = WoWLib.Formats.M2.Root.Record.MaterialFlags;
using WmoGroupFlags = WoWLib.Formats.WMO.Group.Chunks.GroupFlags;
using WmoHeaderFlags = WoWLib.Formats.WMO.Root.Chunks.HeaderFlags;
using WmoMaterialFlags = WoWLib.Formats.WMO.Root.Chunks.MaterialFlags;

namespace WTEditor.Avalonia.ViewModels;

public sealed record FilePathViewModel(string Path, uint FileDataId)
{
    public string ToolTip => $"File data ID: {FileDataId}";
}

public sealed record ModelDetailItemViewModel(
    string Name,
    IReadOnlyList<InspectorPropertyViewModel> Properties,
    FlagsFieldViewModel? Flags,
    IReadOnlyList<FilePathViewModel> Textures,
    IReadOnlyList<ModelDetailItemViewModel>? Children = null,
    MaterialDetailsViewModel? Material = null,
    int? SourceIndex = null)
{
    public bool HasMaterial => Material != null;
}

public sealed record ModelTextureSlotViewModel(
    string Label,
    FilePathViewModel File,
    FlagsFieldViewModel? Flags = null)
{
    public bool HasFlags => Flags != null;
}
public sealed record InspectorColorViewModel(string Label, uint Value);
public sealed record ModelTextureItemViewModel(FilePathViewModel File, FlagsFieldViewModel? Flags)
{
    public bool HasFlags => Flags != null;
}

public partial class ModelInformationViewModel : ViewModelBase
{
    [ObservableProperty] private bool _hasModel;
    [ObservableProperty] private bool _hasTextures;
    [ObservableProperty] private string _texturesHeader = "Textures (0)";
    [ObservableProperty] private bool _hasAdvancedInformation;
    [ObservableProperty] private bool _hasWorldModelGroups;
    [ObservableProperty] private string _worldModelGroupsHeader = "WMO groups (0)";
    [ObservableProperty] private bool _hasRootInformation;
    [ObservableProperty] private FilePathViewModel? _modelFile;
    [ObservableProperty] private IReadOnlyList<InspectorPropertyViewModel> _properties = [];
    [ObservableProperty] private FlagsFieldViewModel? _rootFlags;
    [ObservableProperty] private uint _rootAmbientColor;
    [ObservableProperty] private IReadOnlyList<ModelTextureItemViewModel> _textures = [];
    [ObservableProperty] private IReadOnlyList<InspectorPropertyViewModel> _advancedProperties = [];
    [ObservableProperty] private IReadOnlyList<WorldModelGroupData> _groups = [];
    [ObservableProperty] private WorldModelGroupData? _selectedGroup;
    [ObservableProperty] private IReadOnlyList<InspectorPropertyViewModel> _selectedGroupProperties = [];
    [ObservableProperty] private FlagsFieldViewModel? _selectedGroupFlags;
    [ObservableProperty] private bool _hasMaterials;
    [ObservableProperty] private string _materialsHeader = "Materials (0)";
    [ObservableProperty] private bool _hasBatches;
    [ObservableProperty] private string _batchesHeader = "Render batches (0)";
    [ObservableProperty] private bool _hasGeosets;
    [ObservableProperty] private string _geosetsHeader = "Geosets (0)";
    [ObservableProperty] private IReadOnlyList<MaterialDetailsViewModel> _materials = [];
    [ObservableProperty] private MaterialDetailsViewModel? _selectedMaterial;
    [ObservableProperty] private IReadOnlyList<ModelDetailItemViewModel> _batches = [];
    [ObservableProperty] private ModelDetailItemViewModel? _selectedBatch;
    [ObservableProperty] private IReadOnlyList<ModelDetailItemViewModel> _geosets = [];
    [ObservableProperty] private ModelDetailItemViewModel? _selectedGeoset;

    public void SetModel(EditorObjectSnapshot? snapshot)
    {
        HasModel = snapshot?.Data is M2ObjectData or WorldModelObjectData;
        if (!HasModel)
        {
            ModelFile = null;
            Properties = [];
            RootFlags = null;
            Textures = [];
            AdvancedProperties = [];
            Groups = [];
            SelectedGroup = null;
            HasTextures = false;
            HasAdvancedInformation = false;
            HasWorldModelGroups = false;
            TexturesHeader = "Textures (0)";
            WorldModelGroupsHeader = "WMO groups (0)";
            HasRootInformation = false;
            SetDetailedData([], [], []);
            return;
        }

        switch (snapshot!.Data)
        {
            case M2ObjectData m2:
                var m2Materials = CreateMaterials(m2.Materials, false);
                ModelFile = new FilePathViewModel(m2.FileName, m2.FileDataId);
                Properties = [];
                RootFlags = null;
                HasRootInformation = false;
                Textures = CreateTextureItems(m2.Textures, m2.TextureDetails);
                AdvancedProperties = m2.Advanced == null ? [] : CreateM2Advanced(m2.Advanced);
                Groups = [];
                SetDetailedData(
                    m2Materials,
                    CreateBatches(m2.Batches, false, m2Materials),
                    CreateGeosets(m2.Geosets));
                break;
            case WorldModelObjectData wmo:
                var wmoMaterials = CreateMaterials(wmo.Materials, true);
                ModelFile = new FilePathViewModel(wmo.FileName, wmo.FileDataId);
                Properties =
                [
                    new("Doodad sets", wmo.DoodadSetCount.ToString()),
                    new("Active doodads", wmo.ActiveDoodadCount.ToString())
                ];
                RootAmbientColor = wmo.Root?.AmbientColor ?? 0;
                RootFlags = wmo.Root == null
                    ? null
                    : FlagsFieldViewModel.FromEnum<WmoHeaderFlags>("Root flags", wmo.Root.Flags);
                HasRootInformation = wmo.Root != null;
                Textures = CreateTextureItems(wmo.Textures, null);
                Groups = wmo.Groups ?? [];
                AdvancedProperties = CreateWmoAdvanced(Groups);
                SetDetailedData(
                    wmoMaterials,
                    CreateBatches(wmo.Batches, true, wmoMaterials),
                    []);
                break;
        }

        HasTextures = Textures.Count > 0;
        TexturesHeader = $"Textures ({Textures.Count})";
        HasAdvancedInformation = AdvancedProperties.Count > 0;
        HasWorldModelGroups = Groups.Count > 0;
        WorldModelGroupsHeader = $"WMO groups ({Groups.Count})";
        SelectedGroup = Groups.FirstOrDefault();
    }

    partial void OnSelectedGroupChanged(WorldModelGroupData? value)
    {
        SelectedGroupProperties = value == null
            ? []
            :
            [
                new("MOGI name", string.IsNullOrWhiteSpace(value.Description) ? "—" : value.Description),
                new("Group ID", value.GroupId.ToString()),
                new("Batches", value.BatchCount.ToString()),
                new("Vertices", value.VertexCount.ToString("N0")),
                new("Triangles", value.TriangleCount.ToString("N0")),
                new("Doodad references", value.DoodadReferenceCount.ToString()),
            ];
        SelectedGroupFlags = value == null
            ? null
            : FlagsFieldViewModel.FromEnum<WmoGroupFlags>("Flags", value.Flags);
    }

    private static IReadOnlyList<FilePathViewModel> ToFiles(IReadOnlyList<AssetReference>? assets) =>
        assets?.Select(asset => new FilePathViewModel(asset.Name, asset.FileDataId)).ToArray() ?? [];

    private static IReadOnlyList<ModelTextureItemViewModel> CreateTextureItems(
        IReadOnlyList<AssetReference>? assets,
        IReadOnlyList<ModelTextureData>? details)
    {
        if (details is { Count: > 0 })
            return details.Select(texture => new ModelTextureItemViewModel(
                new FilePathViewModel(texture.Asset.Name, texture.Asset.FileDataId),
                texture.Flags.HasValue
                    ? FlagsFieldViewModel.FromEnum<TextureFlags>("Texture flags", texture.Flags.Value)
                    : null)).ToArray();
        return ToFiles(assets).Select(file => new ModelTextureItemViewModel(file, null)).ToArray();
    }

    private static IReadOnlyList<InspectorPropertyViewModel> CreateM2Advanced(ModelAdvancedData data) =>
    [
        new("Render batches", data.BatchCount.ToString()),
        new("Vertices", data.VertexCount.ToString("N0")),
        new("Triangles", data.TriangleCount.ToString("N0")),
        new("Animations", data.AnimationCount.ToString()),
        new("Particle emitters", data.ParticleEmitterCount.ToString()),
        new("Bones", data.BoneCount.ToString()),
        new("Attachments", data.AttachmentCount.ToString())
    ];

    private static IReadOnlyList<InspectorPropertyViewModel> CreateWmoAdvanced(
        IReadOnlyList<WorldModelGroupData> groups) =>
    [
        new("Groups", groups.Count.ToString()),
        new("Render batches", groups.Sum(group => group.BatchCount).ToString("N0")),
        new("Vertices", groups.Sum(group => (long)group.VertexCount).ToString("N0")),
        new("Triangles", groups.Sum(group => (long)group.TriangleCount).ToString("N0"))
    ];

    private void SetDetailedData(
        IReadOnlyList<MaterialDetailsViewModel> materials,
        IReadOnlyList<ModelDetailItemViewModel> batches,
        IReadOnlyList<ModelDetailItemViewModel> geosets)
    {
        Materials = materials;
        Batches = batches;
        Geosets = geosets;
        HasMaterials = materials.Count > 0;
        HasBatches = batches.Count > 0;
        HasGeosets = geosets.Count > 0;
        MaterialsHeader = $"Materials ({materials.Count})";
        BatchesHeader = $"Render batches ({batches.Count})";
        GeosetsHeader = $"Geosets ({geosets.Count})";
        SelectedMaterial = materials.FirstOrDefault();
        SelectedBatch = batches.FirstOrDefault();
        SelectedGeoset = geosets.FirstOrDefault();
    }

    private static IReadOnlyList<MaterialDetailsViewModel> CreateMaterials(
        IReadOnlyList<ModelMaterialData>? materials,
        bool isWmo) => materials?.Select(material => new MaterialDetailsViewModel(
            isWmo ? ModelMaterialType.Wmo : ModelMaterialType.M2,
            material.Index,
            isWmo
                ? $"Material {material.Index}"
                : material.Textures.FirstOrDefault()?.Name ?? $"Material {material.Index}",
            CreateMaterialProperties(material),
            isWmo
                ? FlagsFieldViewModel.FromEnum<WmoMaterialFlags>("Flags", material.Flags)
                : FlagsFieldViewModel.FromEnum<M2MaterialFlags>("Render flags", material.Flags),
            ToFiles(material.Textures),
            CreateTextureSlots(material),
            CreateMaterialColors(material))).ToArray() ?? [];

    private static IReadOnlyList<InspectorPropertyViewModel> CreateMaterialProperties(ModelMaterialData material)
    {
        var properties = new List<InspectorPropertyViewModel>();
        if (material.BlendMode != uint.MaxValue)
            properties.Add(new("Blend mode", FormatBlendMode(material.BlendMode)));
        if (!string.IsNullOrWhiteSpace(material.Shader))
            properties.Add(new("Shader", material.Shader));
        if (!string.IsNullOrWhiteSpace(material.VertexShader))
            properties.Add(new("Vertex shader", material.VertexShader));
        if (!string.IsNullOrWhiteSpace(material.PixelShader))
            properties.Add(new("Pixel shader", material.PixelShader));
        if (material.GroundType.HasValue)
            properties.Add(new("Terrain type", material.GroundType.Value.ToString()));
        if (material.ExtendedFlags.HasValue)
            properties.Add(new("Extended flags", $"0x{material.ExtendedFlags.Value:X}"));
        return properties;
    }

    private static IReadOnlyList<ModelTextureSlotViewModel> CreateTextureSlots(ModelMaterialData material) =>
        material.TextureSlots?.Select(texture => new ModelTextureSlotViewModel(
            $"Texture {texture.Slot}",
            new FilePathViewModel(texture.Asset.Name, texture.Asset.FileDataId),
            texture.Flags.HasValue
                ? FlagsFieldViewModel.FromEnum<TextureFlags>("Texture flags", texture.Flags.Value)
                : null)).ToArray()
        ?? material.Textures.Select((texture, index) => new ModelTextureSlotViewModel(
            $"Texture {index + 1}",
            new FilePathViewModel(texture.Name, texture.FileDataId))).ToArray();

    private static IReadOnlyList<InspectorColorViewModel> CreateMaterialColors(ModelMaterialData material)
    {
        var colors = new List<InspectorColorViewModel>();
        if (material.Color1.HasValue) colors.Add(new("Color 1", material.Color1.Value));
        if (material.Color1B.HasValue) colors.Add(new("Color 1b", material.Color1B.Value));
        if (material.Color2.HasValue) colors.Add(new("Color 2", material.Color2.Value));
        if (material.Color3.HasValue) colors.Add(new("Color 3", material.Color3.Value));
        return colors;
    }

    private static IReadOnlyList<ModelDetailItemViewModel> CreateBatches(
        IReadOnlyList<ModelBatchData>? batches,
        bool isWmo,
        IReadOnlyList<MaterialDetailsViewModel> materials) => batches?.Select(batch => new ModelDetailItemViewModel(
            isWmo ? $"Batch {batch.Index} · Group {batch.GroupIndex}" : $"Batch {batch.Index}",
            CreateBatchProperties(batch, batch.MaterialIndex.HasValue),
            batch.MaterialIndex.HasValue ? null : FlagsFieldViewModel.FromEnum<M2MaterialFlags>("Render flags", batch.Flags),
            batch.MaterialIndex.HasValue ? [] : ToFiles(batch.Textures),
            Material: batch.MaterialIndex.HasValue
                ? materials.FirstOrDefault(material => material.SourceIndex == batch.MaterialIndex.Value)
                : null,
            SourceIndex: batch.Index)).ToArray() ?? [];

    private static IReadOnlyList<InspectorPropertyViewModel> CreateBatchProperties(ModelBatchData batch, bool hasLinkedMaterial)
    {
        var properties = new List<InspectorPropertyViewModel>();
        if (batch.GroupIndex.HasValue)
            properties.Add(new("Group", batch.GroupIndex.Value.ToString()));
        if (batch.MaterialIndex.HasValue)
            properties.Add(new("Material", batch.MaterialIndex.Value.ToString()));
        properties.Add(new("First index", batch.FirstIndex.ToString("N0")));
        properties.Add(new("Index count", batch.IndexCount.ToString("N0")));
        properties.Add(new("Triangles", (batch.IndexCount / 3).ToString("N0")));
        if (hasLinkedMaterial)
            return properties;
        properties.Add(new("Blend mode", FormatBlendMode(batch.BlendMode)));
        if (!string.IsNullOrWhiteSpace(batch.Shader))
            properties.Add(new("Shader", batch.Shader));
        if (!string.IsNullOrWhiteSpace(batch.VertexShader))
            properties.Add(new("Vertex shader", batch.VertexShader));
        if (!string.IsNullOrWhiteSpace(batch.PixelShader))
            properties.Add(new("Pixel shader", batch.PixelShader));
        return properties;
    }

    private static IReadOnlyList<ModelDetailItemViewModel> CreateGeosets(
        IReadOnlyList<ModelGeosetData>? geosets) => geosets?.Select(geoset => new ModelDetailItemViewModel(
            $"Geoset {geoset.Id} · {geoset.Type}",
            [
                new("Index", geoset.Index.ToString()),
                new("ID", geoset.Id.ToString()),
                new("Type", geoset.Type),
                new("Level", geoset.Level.ToString()),
                new("Enabled", geoset.IsEnabled ? "Yes" : "No"),
                new("First vertex", geoset.FirstVertex.ToString("N0")),
                new("Vertices", geoset.VertexCount.ToString("N0")),
                new("First index", geoset.FirstIndex.ToString("N0")),
                new("Indices", geoset.IndexCount.ToString("N0")),
                new("Triangles", (geoset.IndexCount / 3).ToString("N0"))
            ],
            null,
            [])).ToArray() ?? [];

    private static string FormatBlendMode(uint blendMode) => blendMode switch
    {
        0 => "Opaque (0)",
        1 => "Alpha key (1)",
        2 => "Alpha blend (2)",
        3 => "Additive without alpha (3)",
        4 => "Additive (4)",
        5 => "Modulate (5)",
        6 => "Modulate 2x (6)",
        7 => "Blend additive (7)",
        _ => $"Unknown ({blendMode})"
    };
}
