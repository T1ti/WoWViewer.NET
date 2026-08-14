using CommunityToolkit.Mvvm.ComponentModel;
using WoWFormatLib.Structs.ADT;
using WTEditor.Application.Models;

namespace WTEditor.Avalonia.ViewModels;

public partial class PlacementInformationViewModel : ViewModelBase
{
    private bool _synchronizing;
    private ushort _nameSet;

    public event EventHandler<WmoPlacementSelection>? WmoPlacementChanged;

    [ObservableProperty] private bool _hasPlacement;
    [ObservableProperty] private bool _hasWorldModelPlacement;
    [ObservableProperty] private string _placementType = string.Empty;
    [ObservableProperty] private FilePathViewModel? _sourceAdtFile;
    [ObservableProperty] private IReadOnlyList<InspectorPropertyViewModel> _properties = [];
    [ObservableProperty] private FlagsFieldViewModel? _flags;
    [ObservableProperty] private IReadOnlyList<PlacementOptionViewModel> _doodadSetOptions = [];
    [ObservableProperty] private PlacementOptionViewModel? _selectedDoodadSet;
    [ObservableProperty] private bool _canEditDoodadSet;

    public void SetPlacement(EditorObjectSnapshot? snapshot)
    {
        var (placement, parentName, parentId, doodadSets) = snapshot?.Data switch
        {
            M2ObjectData m2 => (m2.Placement, m2.ParentFileName, m2.ParentFileDataId, (IReadOnlyList<string>?)null),
            WorldModelObjectData wmo => (wmo.Placement, wmo.ParentFileName, wmo.ParentFileDataId, wmo.DoodadSets),
            _ => (null, string.Empty, 0u, null)
        };

        HasPlacement = placement != null;
        if (placement == null)
        {
            PlacementType = string.Empty;
            SourceAdtFile = null;
            Properties = [];
            Flags = null;
            HasWorldModelPlacement = false;
            DoodadSetOptions = [];
            return;
        }

        PlacementType = placement.Kind == MapPlacementKind.Mddf ? "M2 map placement (MDDF)" : "WMO map placement (MODF)";
        SourceAdtFile = new FilePathViewModel(parentName, parentId);
        Properties = placement.Kind == MapPlacementKind.Mddf
            ? [new("UID", placement.UniqueId.ToString())]
            :
            [
                new("UID", placement.UniqueId.ToString()),
                new("Name set", placement.NameSet?.ToString() ?? "—")
            ];
        Flags = placement.Kind == MapPlacementKind.Mddf
            ? FlagsFieldViewModel.FromEnum<MDDFFlags>("Flags", placement.Flags)
            : FlagsFieldViewModel.FromEnum<MODFFlags>("Flags", placement.Flags);

        _synchronizing = true;
        try
        {
            HasWorldModelPlacement = placement.Kind == MapPlacementKind.Modf;
            CanEditDoodadSet = (placement.Flags & (ushort)MODFFlags.modf_use_sets_from_mwds) == 0;
            DoodadSetOptions = doodadSets?.Select((name, index) =>
                new PlacementOptionViewModel(
                    (ushort)index,
                    $"({index}) {(string.IsNullOrWhiteSpace(name) ? $"Set {index}" : name)}"))
                .ToArray() ?? [];
            SelectedDoodadSet = DoodadSetOptions.FirstOrDefault(option => option.Index == placement.DoodadSet);
            _nameSet = placement.NameSet ?? 0;
        }
        finally
        {
            _synchronizing = false;
        }
    }

    partial void OnSelectedDoodadSetChanged(PlacementOptionViewModel? value)
    {
        if (_synchronizing || value == null)
            return;

        WmoPlacementChanged?.Invoke(this, new WmoPlacementSelection(value.Index, _nameSet));
    }
}

public sealed record PlacementOptionViewModel(ushort Index, string DisplayName);
public sealed record WmoPlacementSelection(ushort DoodadSet, ushort NameSet);
