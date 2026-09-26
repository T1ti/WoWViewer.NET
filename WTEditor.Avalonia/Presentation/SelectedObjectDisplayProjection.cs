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

    public EditorObjectSnapshot? CreateDisplaySnapshot(Container3D? selectedObject)
    {
        if (selectedObject == null)
        {
            _selectedObject = null;
            _displayData = null;
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

        if (selectedObject is WMOContainer selectedWmo &&
                 _displayData is WorldModelObjectData { IsLoaded: false } &&
                 selectedWmo.IsLoaded)
        {
            RefreshDisplayData(selectedObject);
        }
        else if (selectedObject is ADTContainer selectedAdt &&
                 _displayData is TerrainObjectData terrainData &&
                 (terrainData.IsLoaded != selectedAdt.IsLoaded ||
                  terrainData.IsModified != selectedAdt.IsModified))
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
                adt.IsLoaded ? adt.Terrain.rootADTFileDataID : 0,
                adt.mapTile.TileX,
                adt.mapTile.TileY,
                adt.IsLoaded,
                adt.IsModified,
                WoWRenderLib.Services.WowlibFileSystem.TryGetCurrent()?.Kind == WoWLib.StorageKind.Mpq
                    ? string.IsNullOrWhiteSpace(adt.mapTile.WdtPath)
                        ? "Missing MPQ WDT path"
                        : WoWRenderLib.Services.MapAssetPathResolver.GetLegacyAdtPath(
                            adt.mapTile.WdtPath, adt.mapTile.TileX, adt.mapTile.TileY)
                    : string.Empty),
            _ => null
        };

    private static string GetDisplayName(Container3D selectedObject, IEditorObjectData? data)
    {
        var fileName = data switch
        {
            M2ObjectData m2 => m2.FileName,
            WorldModelObjectData wmo => wmo.FileName,
            TerrainObjectData adt => WoWRenderLib.Services.WowlibFileSystem.TryGetCurrent()?.Kind == WoWLib.StorageKind.Mpq
                ? adt.FilePath
                : adt.FileDataId == 0 ? $"ADT {adt.TileX}_{adt.TileY}"
                : WoWRenderLib.Services.WowlibFileSystem.GetAssetDisplayName(adt.FileDataId),
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = WoWRenderLib.Services.WowlibFileSystem.GetAssetDisplayName(selectedObject.FileDataId);

        return fileName.StartsWith("FDID ", StringComparison.Ordinal) ||
               fileName.StartsWith("Unknown", StringComparison.Ordinal) ||
               fileName.StartsWith("Missing", StringComparison.Ordinal)
            ? $"{selectedObject.GetType().Name.Replace("Container", string.Empty)} {fileName}"
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
