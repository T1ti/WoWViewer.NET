namespace WTEditor.Avalonia.ViewModels;

public enum ModelMaterialType
{
    M2,
    Wmo
}

public sealed record MaterialDetailsViewModel(
    ModelMaterialType MaterialType,
    int SourceIndex,
    string Name,
    IReadOnlyList<InspectorPropertyViewModel> Properties,
    FlagsFieldViewModel? Flags,
    IReadOnlyList<FilePathViewModel> Textures,
    IReadOnlyList<ModelTextureSlotViewModel> TextureSlots,
    IReadOnlyList<InspectorColorViewModel> Colors);
