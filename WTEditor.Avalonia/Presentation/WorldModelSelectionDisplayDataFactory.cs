using System.Diagnostics;
using WTEditor.Application.Models;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.Renderer;

namespace WTEditor.Avalonia.Presentation;

/// <summary>Builds inspector display data for WMO selections.</summary>
internal static class WorldModelSelectionDisplayDataFactory
{
    public static WorldModelObjectData Create(WMOContainer wmo)
    {
        if (!wmo.IsLoaded)
            return CreateUnloaded(wmo);

        try
        {
            return CreateLoaded(
                wmo.GetWMO(),
                wmo.FileDataId,
                wmo.ParentFileDataId,
                wmo.UniqueID,
                wmo.PlacementFlags,
                wmo.PlacementDoodadSet,
                wmo.PlacementNameSet,
                wmo.ActiveDoodads.Count);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Unable to project loaded WMO {wmo.FileDataId} for display: {exception}");
            Console.Error.WriteLine(
                $"Unable to project loaded WMO {wmo.FileDataId} for display: {exception.Message}");
            return CreateUnloaded(wmo);
        }
    }

    internal static WorldModelObjectData CreateLoaded(
        WoWRenderLib.DX11.Structs.WorldModel model,
        uint fileDataId,
        uint parentFileDataId,
        uint uniqueId,
        ushort placementFlags,
        ushort placementDoodadSet,
        ushort placementNameSet,
        int activeDoodadCount)
    {
        var preppedMaterials = model.preppedMats ?? [];
        var materials = preppedMaterials.Select((material, index) => new ModelMaterialData(
            index,
            material.BlendMode,
            material.Flags,
            ModelSelectionDisplayAssets.GetEnumDisplayName<WorldModelShaderDisplayName>(
                (uint)material.Shader),
            material.VertexShader.ToString(),
            material.PixelShader.ToString(),
            ModelSelectionDisplayAssets.CreateReferences(() => GetTextureIds(material)),
            Color1: material.Color1,
            Color1B: material.Color1B,
            Color2: material.Color2,
            Color3: material.Color3,
            GroundType: material.GroundType,
            ExtendedFlags: material.Flags3,
            TextureSlots: CreateTextureSlots(material))).ToArray();
        var renderBatches = model.wmoRenderBatches ?? [];
        var detailedBatches = renderBatches.Select((batch, index) => new ModelBatchData(
            index,
            checked((int)batch.groupID),
            batch.materialIndex,
            batch.firstFace,
            batch.numFaces,
            batch.blendType,
            0,
            ModelSelectionDisplayAssets.GetEnumDisplayName<WorldModelShaderDisplayName>(batch.shader),
            batch.materialIndex >= 0 && batch.materialIndex < preppedMaterials.Length
                ? preppedMaterials[batch.materialIndex].VertexShader.ToString()
                : string.Empty,
            batch.materialIndex >= 0 && batch.materialIndex < preppedMaterials.Length
                ? preppedMaterials[batch.materialIndex].PixelShader.ToString()
                : string.Empty,
            ModelSelectionDisplayAssets.CreateReferences(() => batch.materialFDIDs))).ToArray();
        var groups = (model.groupBatches ?? []).Select((group, index) =>
        {
            var batches = renderBatches
                .Where(batch => batch.groupID == (uint)index)
                .ToArray();
            return new WorldModelGroupData(
                index,
                string.IsNullOrWhiteSpace(group.groupName) ? $"Group {index}" : group.groupName,
                group.mogiGroupName ?? string.Empty,
                group.groupID,
                batches.Length,
                checked((int)group.verticeCount),
                checked((int)(batches.Sum(batch => (long)batch.numFaces) / 3)),
                group.doodadReferences?.Length ?? 0,
                group.flags);
        }).ToArray();
        return new WorldModelObjectData(
            fileDataId,
            parentFileDataId,
            groups.Length,
            model.doodadSets?.Length ?? 0,
            activeDoodadCount,
            model.rootWMOFileDataID == fileDataId,
            WoWRenderLib.Listfile.GetDisplayName(fileDataId),
            ModelSelectionDisplayAssets.CreateReferences(
                () => preppedMaterials.SelectMany(GetTextureIds)),
            uniqueId,
            WoWRenderLib.Listfile.GetDisplayName(parentFileDataId),
            groups,
            new MapPlacementData(
                MapPlacementKind.Modf,
                uniqueId,
                placementFlags,
                placementDoodadSet,
                placementNameSet),
            new WorldModelRootData(model.ambientColor, model.flags),
            materials,
            detailedBatches,
            (model.doodadSets ?? []).Select((name, index) =>
                string.IsNullOrWhiteSpace(name) ? $"Set {index}" : name).ToArray());
    }

    private static WorldModelObjectData CreateUnloaded(WMOContainer wmo) => new(
        wmo.FileDataId,
        wmo.ParentFileDataId,
        0,
        0,
        wmo.ActiveDoodads.Count,
        false,
        WoWRenderLib.Listfile.GetDisplayName(wmo.FileDataId),
        [],
        wmo.UniqueID,
        WoWRenderLib.Listfile.GetDisplayName(wmo.ParentFileDataId),
        [],
        new MapPlacementData(
            MapPlacementKind.Modf,
            wmo.UniqueID,
            wmo.PlacementFlags,
            wmo.PlacementDoodadSet,
            wmo.PlacementNameSet));

    private static IEnumerable<uint> GetTextureIds(WoWRenderLib.Structs.PreppedWMOMaterial material)
    {
        yield return material.TexFileDataID0;
        yield return material.TexFileDataID1;
        yield return material.TexFileDataID2;
        yield return material.TexFileDataID3;
        yield return material.TexFileDataID4;
        yield return material.TexFileDataID5;
        yield return material.TexFileDataID6;
        yield return material.TexFileDataID7;
        yield return material.TexFileDataID8;
    }

    private static IReadOnlyList<ModelTextureData> CreateTextureSlots(
        WoWRenderLib.Structs.PreppedWMOMaterial material)
    {
        uint[] ids =
        [
            material.TexFileDataID0,
            material.TexFileDataID1,
            material.TexFileDataID2,
            material.TexFileDataID3,
            material.TexFileDataID4,
            material.TexFileDataID5,
            material.TexFileDataID6,
            material.TexFileDataID7,
            material.TexFileDataID8
        ];
        return ids.Select((fileDataId, index) => (fileDataId, index))
            .Where(item => item.fileDataId is not 0 and not uint.MaxValue)
            .Select(item => new ModelTextureData(
                item.index + 1,
                new AssetReference(
                    item.fileDataId,
                    WoWRenderLib.Listfile.GetDisplayName(item.fileDataId))))
            .ToArray();
    }
}
