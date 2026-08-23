using System.Numerics;
using System.Runtime.InteropServices;
using WoWLib;
using Formats = WoWLib.Formats;
using Fs = WoWLib.Filesystem;
using WoWRenderLib.Renderer;
using WoWRenderLib.Services;
using WoWRenderLib.Structs;
using static WoWRenderLib.Renderer.ShaderEnums;

namespace WoWRenderLib.Loaders;

public static class M2Loader
{
    private const uint DefaultTextureId = 186184;

    public static ParsedM2 ParseM2(uint fileDataId)
    {
        var fileSystem = WowlibFileSystem.Current;
        if (!fileSystem.Exists(new FileDataId(fileDataId)))
            throw new FileNotFoundException($"Model {fileDataId} does not exist!");

        using var model = Formats.M2.M2.ForVersion(fileSystem.Version);
        model.Read(fileSystem, new FileKey(new FileDataId(fileDataId)));
        var root = model.Root;
        var vertices = ReadVertices(root.Vertices);
        var (renderBoundingBox, renderBoundingRadius) = CalculateRenderBounds(vertices.Select(v => v.Position).ToArray());
        var counts = ReadCounts(root);

        var parsed = new ParsedM2
        {
            boundingBox = renderBoundingBox,
            boundingRadius = renderBoundingRadius,
            fileDataID = fileDataId,
            vertexCount = vertices.Length,
            animationCount = counts.AnimationCount,
            particleEmitterCount = counts.ParticleEmitterCount,
            boneCount = counts.BoneCount,
            attachmentCount = counts.AttachmentCount
        };

        var textures = root.Textures.AsSpan();
        var rootMaterials = root.Materials.AsSpan();
        var textureFileDataIds = ResolveTextureFileDataIds(fileSystem, model, textures);
        parsed.mats = new M2Material[textures.Length];
        for (var i = 0; i < parsed.mats.Length; i++)
        {
            var texture = textures[i];
            var material = i < rootMaterials.Length ? rootMaterials[i] : default;
            parsed.mats[i] = new M2Material
            {
                fileDataID = textureFileDataIds[i],
                flags = texture.Flags,
                blendMode = material?.BlendingMode ?? 0
            };
        }

        var profile = ReadProfile(model);
        if (profile == null || profile.Vertices.Length == 0)
            throw new InvalidDataException($"Model {fileDataId} does not contain a skin profile.");

        parsed.vertexCount = profile.Vertices.Length;
        parsed.indexCount = profile.Indices.Length;
        parsed.geosets = ReadGeosets(profile);
        parsed.submeshes = ReadSubmeshes(root, profile, parsed.mats);

        var renderVertices = new M2Vertex[profile.Vertices.Length];
        for (var i = 0; i < renderVertices.Length; i++)
        {
            var rootVertexIndex = profile.Vertices[i];
            renderVertices[i] = rootVertexIndex < vertices.Length ? vertices[rootVertexIndex] : default;
        }

        parsed.vertexBytes = MemoryMarshal.AsBytes(renderVertices.AsSpan()).ToArray();
        parsed.indiceBytes = MemoryMarshal.AsBytes(profile.Indices.AsSpan()).ToArray();
        return parsed;
    }

    private readonly record struct ModelCounts(
        int AnimationCount,
        int ParticleEmitterCount,
        int BoneCount,
        int AttachmentCount);

    private static ModelCounts ReadCounts(Formats.M2.Root.M2Root root)
    {
        return root switch
        {
            Formats.M2.Root.M2RootVanilla value => new(value.Sequences.Count, value.ParticleEmitters.Count, value.Bones.Count, value.Attachments.Count),
            Formats.M2.Root.M2RootTbc value => new(value.Sequences.Count, value.ParticleEmitters.Count, value.Bones.Count, value.Attachments.Count),
            Formats.M2.Root.M2RootWotlk value => new(value.Sequences.Count, value.ParticleEmitters.Count, value.Bones.Count, value.Attachments.Count),
            Formats.M2.Root.M2RootCataToMop value => new(value.Sequences.Count, value.ParticleEmitters.Count, value.Bones.Count, value.Attachments.Count),
            Formats.M2.Root.M2RootWod value => new(value.Sequences.Count, value.ParticleEmitters.Count, value.Bones.Count, value.Attachments.Count),
            Formats.M2.Root.M2RootLegionPlus value => new(value.Sequences.Count, value.ParticleEmitters.Count, value.Bones.Count, value.Attachments.Count),
            _ => default
        };
    }

    private sealed record ProfileData(
        ushort[] Vertices,
        ushort[] Indices,
        SectionData[] Sections,
        BatchData[] Batches);

    private readonly record struct SectionData(
        ushort Id,
        ushort Level,
        ushort FirstVertex,
        ushort VertexCount,
        ushort FirstIndex,
        ushort IndexCount);

    private readonly record struct BatchData(
        ushort ShaderId,
        ushort SectionIndex,
        ushort TextureCount,
        ushort TextureComboIndex,
        ushort MaterialIndex);

    private static ProfileData? ReadProfile(Formats.M2.M2 model)
    {
        return model switch
        {
            Formats.M2.M2Vanilla value when value.Root.SkinProfiles.Count > 0 => ToProfile(value.Root.SkinProfiles[0]),
            Formats.M2.M2Tbc value when value.Root.SkinProfiles.Count > 0 => ToProfile(value.Root.SkinProfiles[0]),
            Formats.M2.M2Wotlk value when value.Skins.Count > 0 => ToProfile(value.Skins[0].Profile),
            Formats.M2.M2CataToMop value when value.Skins.Count > 0 => ToProfile(value.Skins[0].Profile),
            Formats.M2.M2Wod value when value.Skins.Count > 0 => ToProfile(value.Skins[0].Profile),
            Formats.M2.M2Legion value when value.Skins.Count > 0 => ToProfile(value.Skins[0].Profile),
            Formats.M2.M2Bfa value when value.Skins.Count > 0 => ToProfile(value.Skins[0].Profile),
            Formats.M2.M2Shadowlands value when value.Skins.Count > 0 => ToProfile(value.Skins[0].Profile),
            Formats.M2.M2Dragonflight value when value.Skins.Count > 0 => ToProfile(value.Skins[0].Profile),
            Formats.M2.M2TheWarWithin value when value.Skins.Count > 0 => ToProfile(value.Skins[0].Profile),
            _ => null
        };
    }

    private static ProfileData ToProfile(Formats.M2.Skin.M2SkinProfileVanilla profile) => new(
        profile.Vertices.ToArray(),
        profile.Indices.ToArray(),
        profile.Submeshes.ToArray().Select(ToSection).ToArray(),
        profile.Batches.ToArray().Select(ToBatch).ToArray());

    private static ProfileData ToProfile(Formats.M2.Skin.M2SkinProfileTbcToWotlk profile) => new(
        profile.Vertices.ToArray(),
        profile.Indices.ToArray(),
        profile.Submeshes.ToArray().Select(ToSection).ToArray(),
        profile.Batches.ToArray().Select(ToBatch).ToArray());

    private static ProfileData ToProfile(Formats.M2.Skin.M2SkinProfileCataPlus profile) => new(
        profile.Vertices.ToArray(),
        profile.Indices.ToArray(),
        profile.Submeshes.ToArray().Select(ToSection).ToArray(),
        profile.Batches.ToArray().Select(ToBatch).ToArray());

    private static SectionData ToSection(Formats.M2.Skin.M2SkinSectionVanilla section) => new(
        section.SkinSectionId,
        section.Level,
        section.VertexStart,
        section.VertexCount,
        section.IndexStart,
        section.IndexCount);

    private static SectionData ToSection(Formats.M2.Skin.M2SkinSectionTbcPlus section) => new(
        section.SkinSectionId,
        section.Level,
        section.VertexStart,
        section.VertexCount,
        section.IndexStart,
        section.IndexCount);

    private static BatchData ToBatch(Formats.M2.Skin.M2Batch batch) => new(
        batch.ShaderId,
        batch.SkinSectionIndex,
        batch.TextureCount,
        batch.TextureComboIndex,
        batch.MaterialIndex);

    private static M2Vertex[] ReadVertices(WoWLib.Vector<Formats.M2.Root.Record.M2Vertex> vertices)
    {
        // Keep the native vector as a live typed span. M2Vertex is not a
        // blittable Data mirror in wowlib 0.0.8, so its nested position,
        // normal, and UV views still use the typed wrapper API.
        var sourceVertices = vertices.AsSpan();
        var result = new M2Vertex[sourceVertices.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var source = sourceVertices[i];
            result[i] = new M2Vertex
            {
                Position = ToVector3(source.Pos),
                Normal = ToVector3(source.Normal),
                TexCoord1 = ToVector2(source.TexCoords[0]),
                TexCoord2 = ToVector2(source.TexCoords[1])
            };
        }
        return result;
    }

    private static uint[] ResolveTextureFileDataIds(
        Fs.FileSystem fileSystem,
        Formats.M2.M2 model,
        ReadOnlySpan<Formats.M2.Root.Record.M2Texture> textures)
    {
        var chunkIds = GetChunkTextureIds(model);
        var result = new uint[textures.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var texture = textures[i];
            var id = texture.Type == 0 && i < chunkIds.Length ? chunkIds[i] : 0;
            if (id == 0)
                id = ResolvePath(fileSystem, texture.Filename);
            result[i] = id == 0 ? DefaultTextureId : id;
        }
        return result;
    }

    private static uint[] GetChunkTextureIds(Formats.M2.M2 model)
    {
        return model switch
        {
            Formats.M2.M2Legion value => value.Chunks.TextureFdids.ToArray(),
            Formats.M2.M2Bfa value => value.Chunks.TextureFdids.ToArray(),
            Formats.M2.M2Shadowlands value => value.Chunks.TextureFdids.ToArray(),
            Formats.M2.M2Dragonflight value => value.Chunks.TextureFdids.ToArray(),
            Formats.M2.M2TheWarWithin value => value.Chunks.TextureFdids.ToArray(),
            _ => []
        };
    }

    private static M2Geoset[] ReadGeosets(ProfileData profile)
    {
        return profile.Sections.Select(section => new M2Geoset
        {
            id = section.Id,
            level = section.Level,
            firstVertex = section.FirstVertex,
            vertexCount = section.VertexCount,
            firstIndex = section.FirstIndex,
            indexCount = section.IndexCount
        }).ToArray();
    }

    private static Submesh[] ReadSubmeshes(
        Formats.M2.Root.M2Root root,
        ProfileData profile,
        M2Material[] materials)
    {
        var textureLookupTable = root.TextureLookupTable.AsSpan();
        var result = new List<Submesh>(profile.Batches.Length);
        for (var i = 0; i < profile.Batches.Length; i++)
        {
            var batch = profile.Batches[i];
            if (batch.SectionIndex >= profile.Sections.Length)
                continue;

            var section = profile.Sections[batch.SectionIndex];
            var textureIndices = new int[batch.TextureCount];
            var materialIds = new uint[batch.TextureCount];
            for (var texture = 0; texture < batch.TextureCount; texture++)
            {
                var lookupIndex = batch.TextureComboIndex + texture;
                var textureIndex = lookupIndex < textureLookupTable.Length
                    ? textureLookupTable[lookupIndex]
                    : (ushort)0;
                textureIndices[texture] = textureIndex;
                materialIds[texture] = textureIndex < materials.Length
                    ? materials[textureIndex].fileDataID
                    : DefaultTextureId;
            }

            var blendType = batch.MaterialIndex < materials.Length ? materials[batch.MaterialIndex].blendMode : 0;
            result.Add(new Submesh
            {
                firstFace = section.FirstIndex,
                numFaces = section.IndexCount,
                material = materialIds,
                textureIndices = textureIndices,
                blendType = blendType,
                renderFlags = batch.MaterialIndex < materials.Length ? (ushort)materials[batch.MaterialIndex].flags : (ushort)0,
                geosetId = section.Id,
                index = i,
                vertexShaderID = (uint)GetVertexShaderID(batch.TextureCount, batch.ShaderId),
                pixelShaderID = (uint)GetPixelShaderID(batch.TextureCount, batch.ShaderId)
            });
        }

        return [.. result];
    }

    public static (BoundingBox BoundingBox, float Radius) CalculateRenderBounds(ReadOnlySpan<Vector3> vertices)
    {
        if (vertices.IsEmpty)
            return (new BoundingBox(Vector3.Zero, Vector3.Zero), 0f);

        var min = vertices[0];
        var max = vertices[0];
        for (var i = 1; i < vertices.Length; i++)
        {
            min = Vector3.Min(min, vertices[i]);
            max = Vector3.Max(max, vertices[i]);
        }

        var center = (min + max) * 0.5f;
        var maximumDistanceSquared = 0f;
        for (var i = 0; i < vertices.Length; i++)
            maximumDistanceSquared = MathF.Max(maximumDistanceSquared, Vector3.DistanceSquared(center, vertices[i]));

        return (new BoundingBox(min, max), MathF.Sqrt(maximumDistanceSquared));
    }

    private static Vector2 ToVector2(Formats.Common.C2Vector value) => new(value.X, value.Y);

    private static Vector3 ToVector3(Formats.Common.C3Vector value) => new(value.X, value.Y, value.Z);

    private static uint ResolvePath(Fs.FileSystem fileSystem, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;
        try { return fileSystem.Resolve(new FileKey(path)).Fdid?.Value ?? 0; }
        catch { return 0; }
    }

    private static int GetVertexShaderID(int textureCount, ushort shaderID)
    {
        if (textureCount == 1)
            return (shaderID & 0x80) == 0 ? ((shaderID & 0x4000) != 0 ? 10 : 0) : 1;
        if ((shaderID & 0x80) == 0)
        {
            var result = (shaderID & 8) != 0 ? 3 : 7;
            return (shaderID & 0x4000) != 0 ? 2 : result;
        }
        return (shaderID & 8) != 0 ? 5 : 4;
    }

    private static int GetPixelShaderID(int textureCount, ushort shaderID)
    {
        if ((shaderID & 0x8000) > 0)
        {
            var pixelShaderId = shaderID & 0x7FFF;
            if (pixelShaderId >= M2Shaders.Count)
                throw new InvalidDataException($"M2 pixel shader {pixelShaderId} is out of bounds.");
            return (int)M2Shaders[pixelShaderId].PixelShader;
        }

        if (textureCount == 1)
            return (shaderID & 0x70) != 0 ? (int)M2PixelShader.Combiners_Mod : (int)M2PixelShader.Combiners_Opaque;

        return (shaderID & 0x70) != 0
            ? (shaderID & 7) switch
            {
                0 => (int)M2PixelShader.Combiners_Mod_Opaque,
                1 or 2 or 5 => (int)M2PixelShader.Combiners_Mod_Mod,
                3 => (int)M2PixelShader.Combiners_Mod_Add,
                4 => (int)M2PixelShader.Combiners_Mod_Mod2x,
                6 => (int)M2PixelShader.Combiners_Mod_Mod2xNA,
                7 => (int)M2PixelShader.Combiners_Mod_AddNA,
                _ => (int)M2PixelShader.Combiners_Mod_Mod
            }
            : (shaderID & 7) switch
            {
                0 => (int)M2PixelShader.Combiners_Opaque_Opaque,
                1 or 2 or 5 => (int)M2PixelShader.Combiners_Opaque_Mod,
                3 or 7 => (int)M2PixelShader.Combiners_Opaque_AddAlpha,
                4 => (int)M2PixelShader.Combiners_Opaque_Mod2x,
                6 => (int)M2PixelShader.Combiners_Opaque_Mod2xNA,
                _ => (int)M2PixelShader.Combiners_Opaque_Mod
            };
    }
}
