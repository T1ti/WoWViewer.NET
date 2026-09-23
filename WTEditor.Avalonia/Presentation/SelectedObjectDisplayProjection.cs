using System.Numerics;
using WTEditor.Application.Models;
using WoWRenderLib.DX11.Objects;

namespace WTEditor.Avalonia.Presentation;

/// <summary>
/// Projects renderer-owned selection state into an immutable snapshot intended
/// for the editor UI. Format-specific display data belongs in dedicated
/// factories rather than in the viewport control or this identity coordinator.
/// </summary>
internal sealed class SelectedObjectDisplayProjection
{
    private Container3D? _selectedObject;
    private EditorObjectId _selectedObjectId;
    private IEditorObjectData? _displayData;
    private bool _displayDataUsesListfile;

    public EditorObjectSnapshot? CreateDisplaySnapshot(Container3D? selectedObject)
    {
        if (selectedObject == null)
        {
            _selectedObject = null;
            _displayData = null;
            _displayDataUsesListfile = false;
            return null;
        }

        RefreshDisplayDataWhenNeeded(selectedObject);

        var rotation = selectedObject.Rotation * (MathF.PI / 180f);
        return new EditorObjectSnapshot(
            _selectedObjectId,
            GetDisplayName(selectedObject, _displayData),
            GetDisplayKind(selectedObject),
            new ObjectTransform(
                selectedObject.Position,
                Quaternion.CreateFromYawPitchRoll(rotation.Y, rotation.X, rotation.Z),
                new Vector3(selectedObject.Scale)),
            _displayData);
    }

    public void RefreshDisplayData(Container3D selectedObject)
    {
        if (!ReferenceEquals(_selectedObject, selectedObject))
            return;

        _displayData = CreateDisplayData(selectedObject);
        _displayDataUsesListfile = WoWRenderLib.Listfile.IsLoaded;
    }

    private void RefreshDisplayDataWhenNeeded(Container3D selectedObject)
    {
        if (!ReferenceEquals(_selectedObject, selectedObject))
        {
            _selectedObject = selectedObject;
            _selectedObjectId = EditorObjectId.New();
            RefreshDisplayData(selectedObject);
            return;
        }

        if (!_displayDataUsesListfile && WoWRenderLib.Listfile.IsLoaded)
        {
            RefreshDisplayData(selectedObject);
        }
        else if (selectedObject is WMOContainer selectedWmo &&
                 _displayData is WorldModelObjectData { IsLoaded: false } &&
                 selectedWmo.IsLoaded)
        {
            RefreshDisplayData(selectedObject);
        }
        else if (selectedObject is ADTContainer selectedAdt &&
                 _displayData is TerrainObjectData terrainData &&
                 terrainData.IsModified != selectedAdt.IsModified)
        {
            RefreshDisplayData(selectedObject);
        }
    }

    private static IEditorObjectData? CreateDisplayData(Container3D selectedObject) =>
        selectedObject switch
        {
            M2Container m2 => M2SelectionDisplayDataFactory.Create(m2),
            WMOContainer wmo => WorldModelSelectionDisplayDataFactory.Create(wmo),
            ADTContainer adt => new TerrainObjectData(
                adt.FileDataId,
                adt.mapTile.tileX,
                adt.mapTile.tileY,
                adt.IsLoaded,
                adt.IsModified),
            _ => null
        };

    private static string GetDisplayName(Container3D selectedObject, IEditorObjectData? data)
    {
        var fileName = data switch
        {
            M2ObjectData m2 => m2.FileName,
            WorldModelObjectData wmo => wmo.FileName,
            TerrainObjectData adt => WoWRenderLib.Services.WowlibFileSystem.GetAssetDisplayName(adt.FileDataId),
            _ => string.Empty
        };
        return string.IsNullOrWhiteSpace(fileName) ||
               fileName.StartsWith("FDID ", StringComparison.Ordinal)
            ? $"{selectedObject.GetType().Name.Replace("Container", string.Empty)} " +
              selectedObject.FileDataId
            : Path.GetFileName(fileName);
    }

    private static string GetDisplayKind(Container3D selectedObject) => selectedObject switch
    {
        M2Container => "M2 model",
        WMOContainer => "World model",
        ADTContainer => "Terrain tile",
        _ => selectedObject.GetType().Name
    };
}
