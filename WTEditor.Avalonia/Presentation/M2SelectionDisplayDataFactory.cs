using WTEditor.Application.Models;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.Renderer;

namespace WTEditor.Avalonia.Presentation;

/// <summary>Builds inspector display data for M2 selections.</summary>
internal static class M2SelectionDisplayDataFactory
{
    public static M2ObjectData Create(M2Container m2)
    {
        try
        {
            var model = m2.GetM2();
            var textureDetails = model.mats.Select((texture, index) => new ModelTextureData(
                index,
                new AssetReference(
                    texture.fileDataID,
                    WoWRenderLib.Services.WowlibFileSystem.GetAssetDisplayName(texture.fileDataID)),
                (uint)texture.flags)).ToArray();
            var materials = model.submeshes.Select((batch, index) => new ModelMaterialData(
                index,
                batch.blendType,
                batch.renderFlags,
                string.Empty,
                ModelSelectionDisplayAssets.GetEnumDisplayName<ShaderEnums.M2VertexShader>(
                    batch.vertexShaderID),
                ModelSelectionDisplayAssets.GetEnumDisplayName<ShaderEnums.M2PixelShader>(
                    batch.pixelShaderID),
                ModelSelectionDisplayAssets.CreateReferences(() => batch.material),
                TextureSlots: CreateTextureSlots(() => batch.textureIndices, textureDetails)))
                .ToArray();
            var batches = model.submeshes.Select((batch, index) => new ModelBatchData(
                index,
                null,
                index,
                batch.firstFace,
                batch.numFaces,
                batch.blendType,
                batch.renderFlags,
                string.Empty,
                ModelSelectionDisplayAssets.GetEnumDisplayName<ShaderEnums.M2VertexShader>(
                    batch.vertexShaderID),
                ModelSelectionDisplayAssets.GetEnumDisplayName<ShaderEnums.M2PixelShader>(
                    batch.pixelShaderID),
                ModelSelectionDisplayAssets.CreateReferences(() => batch.material))).ToArray();
            var enabledGeosets = m2.EnabledGeosets;
            var geosets = model.geosets.Select((geoset, index) => new ModelGeosetData(
                index,
                geoset.id,
                GetGeosetDisplayType(geoset.id),
                geoset.level,
                geoset.firstVertex,
                geoset.vertexCount,
                geoset.firstIndex,
                geoset.indexCount,
                index < enabledGeosets.Length && enabledGeosets[index])).ToArray();
            return new M2ObjectData(
                m2.FileDataId,
                m2.ParentFileDataId,
                m2.EnabledGeosets.Length,
                m2.ParentWMO != null,
                WoWRenderLib.Services.WowlibFileSystem.GetAssetDisplayName(m2.FileDataId),
                ModelSelectionDisplayAssets.CreateReferences(
                    () => model.mats.Select(material => material.fileDataID)),
                m2.ParentWMO == null && m2.UniqueID != 0 ? m2.UniqueID : null,
                WoWRenderLib.Services.WowlibFileSystem.GetAssetDisplayName(m2.ParentFileDataId),
                new ModelAdvancedData(
                    model.submeshes?.Length ?? 0,
                    model.vertexCount,
                    model.indexCount / 3,
                    model.animationCount,
                    model.particleEmitterCount,
                    model.boneCount,
                    model.attachmentCount),
                m2.ParentWMO == null
                    ? new MapPlacementData(MapPlacementKind.Mddf, m2.UniqueID, m2.PlacementFlags)
                    : null,
                materials,
                batches,
                geosets,
                textureDetails);
        }
        catch
        {
            return new M2ObjectData(
                m2.FileDataId,
                m2.ParentFileDataId,
                0,
                m2.ParentWMO != null,
                WoWRenderLib.Services.WowlibFileSystem.GetAssetDisplayName(m2.FileDataId),
                [],
                m2.ParentWMO == null && m2.UniqueID != 0 ? m2.UniqueID : null,
                WoWRenderLib.Services.WowlibFileSystem.GetAssetDisplayName(m2.ParentFileDataId),
                null,
                m2.ParentWMO == null
                    ? new MapPlacementData(MapPlacementKind.Mddf, m2.UniqueID, m2.PlacementFlags)
                    : null);
        }
    }

    private static IReadOnlyList<ModelTextureData> CreateTextureSlots(
        Func<IEnumerable<int>> getIndices,
        IReadOnlyList<ModelTextureData> textures)
    {
        try
        {
            return getIndices()
                .Where(index => index >= 0 && index < textures.Count)
                .Select((index, slot) => new ModelTextureData(
                    slot + 1,
                    textures[index].Asset,
                    textures[index].Flags))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static string GetGeosetDisplayType(ushort id) => (id / 100) switch
    {
        0 => "Base skin",
        1 => "Hair",
        2 => "Facial hair 1",
        3 => "Facial hair 2",
        4 => "Facial hair 3",
        5 => "Gloves",
        6 => "Boots",
        7 => "Ears",
        8 => "Wristbands",
        9 => "Kneepads",
        10 => "Chest",
        11 => "Pants",
        12 => "Tabard",
        13 => "Trousers",
        14 => "Cloak",
        16 => "Eye effects",
        17 => "Belt",
        18 => "Bones",
        19 => "Feet",
        20 => "Head",
        21 => "Torso",
        22 => "Hand attachment",
        23 => "Head attachment",
        var category => $"Category {category}"
    };
}
