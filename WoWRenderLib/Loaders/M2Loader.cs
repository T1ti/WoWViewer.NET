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
    // Blizzard's built-in missing-texture FileDataID; wowlib does not define
    // renderer fallback assets.
    private const uint FallbackTextureFileDataId = 186184;
    // Legacy M2 shader IDs are compact bitfields rather than a wowlib enum.
    private const ushort ShaderBlendModeBit = 0x0008;
    private const ushort ShaderCombinerMask = 0x0070;
    private const ushort ShaderCombinerIdMask = 0x0007;
    private const ushort ShaderUsesEnvironmentBit = 0x4000;
    private const ushort ShaderUsesVertexShaderBit = 0x0080;
    private const ushort ShaderUsesPixelShaderTableBit = 0x8000;
    private const ushort ShaderPixelShaderIndexMask = 0x7FFF;

    public static ParsedM2 ParseM2(uint fileDataId)
    {
        var fileSystem = WowlibFileSystem.Current;
        if (!WowlibFileSystem.AssetExists(fileSystem, fileDataId))
            throw new FileNotFoundException($"Model {fileDataId} does not exist!");

        using var model = Formats.M2.M2.ForVersion(fileSystem.Version);
        using var modelKey = WowlibFileSystem.AssetKey(fileSystem, fileDataId);
        model.Read(fileSystem, modelKey);
        var root = model.Root;
        var vertices = ReadVertices(root.Vertices);
        var (renderBoundingBox, renderBoundingRadius) = CalculateRenderBounds(vertices.Select(v => v.Position).ToArray());
        if (fileSystem.Kind == StorageKind.Mpq && root is Formats.M2.Root.M2RootWotlk)
        {
            var authoredMin = ToVector3(root.BoundingBox.Min);
            var authoredMax = ToVector3(root.BoundingBox.Max);
            if (IsFinite(authoredMin) && IsFinite(authoredMax) &&
                authoredMin.X <= authoredMax.X && authoredMin.Y <= authoredMax.Y && authoredMin.Z <= authoredMax.Z)
            {
                var min = Vector3.Min(renderBoundingBox.Min, authoredMin);
                var max = Vector3.Max(renderBoundingBox.Max, authoredMax);
                renderBoundingBox = new BoundingBox(min, max);
                renderBoundingRadius = Vector3.Distance(min, max) * 0.5f;
            }
        }
        var counts = ReadCounts(root);

        var parsed = new ParsedM2
        {
            usesLegacyDepthFlags = fileSystem.Kind == StorageKind.Mpq,
            boundingBox = renderBoundingBox,
            boundingRadius = renderBoundingRadius,
            fileDataID = fileDataId,
            vertexCount = vertices.Length,
            animationCount = counts.AnimationCount,
            particleEmitterCount = counts.ParticleEmitterCount,
            boneCount = counts.BoneCount,
            attachmentCount = counts.AttachmentCount
        };
        if (fileSystem.Kind == StorageKind.Mpq && root is Formats.M2.Root.M2RootWotlk wotlkRoot)
            parsed.animation = ReadAnimation(wotlkRoot);

        // M2Texture and M2Vertex are reference records in wowlib 0.0.9 and
        // therefore do not expose blittable Data mirrors. Keep those vectors
        // on the typed wrapper path; M2Material has a Data mirror and can use
        // a live data span.
        var textures = root.Textures;
        var rootMaterials = root.Materials.AsDataSpan();
        var renderMaterials = new M2RenderMaterial[rootMaterials.Length];
        for (var i = 0; i < renderMaterials.Length; i++)
            renderMaterials[i] = new(rootMaterials[i].Flags, rootMaterials[i].BlendingMode);
        var textureFileDataIds = ResolveTextureFileDataIds(fileSystem, model, textures);
        parsed.mats = new M2Material[textures.Count];
        for (var i = 0; i < parsed.mats.Length; i++)
        {
            var texture = textures[i];
            var material = i < rootMaterials.Length ? rootMaterials[i] : default;
            parsed.mats[i] = new M2Material
            {
                fileDataID = textureFileDataIds[i],
                flags = texture.Flags,
                blendMode = material.BlendingMode
            };
        }

        var profile = ReadProfile(model);
        if (profile == null || profile.Vertices.Length == 0)
            throw new InvalidDataException($"Model {fileDataId} does not contain a skin profile.");

        parsed.vertexCount = profile.Vertices.Length;
        parsed.indexCount = profile.Indices.Length;
        parsed.geosets = ReadGeosets(profile);
        parsed.submeshes = ReadSubmeshes(
            root, profile, parsed.mats, renderMaterials, fileSystem.Kind == StorageKind.Mpq);

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
        // The 0.0.9 M2Root is the common record-family base. These
        // collections are live views for every era, including Classic clients.
        return new(
            root.Sequences.Count,
            root.ParticleEmitters.Count,
            root.Bones.Count,
            root.Attachments.Count);
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
        ushort TextureCoordComboIndex,
        ushort MaterialIndex,
        ushort ColorIndex,
        ushort TextureWeightComboIndex,
        ushort TextureTransformComboIndex);

    internal readonly record struct M2RenderMaterial(ushort Flags, ushort BlendMode);

    private static ProfileData? ReadProfile(Formats.M2.M2 model)
    {
        Formats.M2.Skin.M2SkinProfile? profile = model switch
        {
            Formats.M2.M2Vanilla value when value.Root.SkinProfiles.Count > 0 => value.Root.SkinProfiles[0],
            Formats.M2.M2Tbc value when value.Root.SkinProfiles.Count > 0 => value.Root.SkinProfiles[0],
            Formats.M2.M2Wotlk value when value.Skins.Count > 0 => value.Skins[0].Profile,
            Formats.M2.M2CataToMop value when value.Skins.Count > 0 => value.Skins[0].Profile,
            Formats.M2.M2Wod value when value.Skins.Count > 0 => value.Skins[0].Profile,
            Formats.M2.M2Legion value when value.Skins.Count > 0 => value.Skins[0].Profile,
            Formats.M2.M2Bfa value when value.Skins.Count > 0 => value.Skins[0].Profile,
            Formats.M2.M2Shadowlands value when value.Skins.Count > 0 => value.Skins[0].Profile,
            Formats.M2.M2Dragonflight value when value.Skins.Count > 0 => value.Skins[0].Profile,
            Formats.M2.M2TheWarWithin value when value.Skins.Count > 0 => value.Skins[0].Profile,
            _ => null
        };

        return profile is null ? null : ToProfile(profile);
    }

    private static ProfileData ToProfile(Formats.M2.Skin.M2SkinProfile profile)
    {
        var vertices = profile.Vertices.AsSpan().ToArray();
        var indices = profile.Indices.AsSpan().ToArray();
        var sections = ReadSections(profile.Submeshes);
        var sourceBatches = profile.Batches.AsDataSpan();
        var batches = new BatchData[sourceBatches.Length];
        for (var i = 0; i < batches.Length; i++)
            batches[i] = ToBatch(sourceBatches[i]);

        return new ProfileData(vertices, indices, sections, batches);
    }

    private static SectionData[] ReadSections(
        WoWLib.FamilyVector<Formats.M2.Skin.M2SkinSection> sections)
    {
        var result = new SectionData[sections.Count];
        for (var i = 0; i < result.Length; i++)
            result[i] = ToSection(sections[i]);
        return result;
    }

    private static SectionData ToSection(Formats.M2.Skin.M2SkinSection section) => new(
        section.SkinSectionId,
        section.Level,
        section.VertexStart,
        section.VertexCount,
        section.IndexStart,
        section.IndexCount);

    private static BatchData ToBatch(Formats.M2.Skin.M2Batch.Data batch) => new(
        batch.ShaderId,
        batch.SkinSectionIndex,
        batch.TextureCount,
        batch.TextureComboIndex,
        batch.TextureCoordComboIndex,
        batch.MaterialIndex,
        batch.ColorIndex,
        batch.TextureWeightComboIndex,
        batch.TextureTransformComboIndex);

    private static M2Vertex[] ReadVertices(WoWLib.Vector<Formats.M2.Root.Record.M2Vertex> vertices)
    {
        // M2Vertex remains a reference record in wowlib 0.0.9. Indexing the
        // typed vector keeps the nested position, normal, and UV views valid;
        // the unmanaged AsSpan extension intentionally cannot be used here.
        var result = new M2Vertex[vertices.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var source = vertices[i];
            result[i] = new M2Vertex
            {
                Position = ToVector3(source.Pos),
                Normal = ToVector3(source.Normal),
                TexCoord1 = ToVector2(source.TexCoords[0]),
                TexCoord2 = ToVector2(source.TexCoords[1]),
                BoneWeights = PackBytes(source.BoneWeights[0], source.BoneWeights[1],
                    source.BoneWeights[2], source.BoneWeights[3]),
                BoneIndices = PackBytes(source.BoneIndices[0], source.BoneIndices[1],
                    source.BoneIndices[2], source.BoneIndices[3])
            };
        }
        return result;
    }

    private static uint PackBytes(byte x, byte y, byte z, byte w) =>
        (uint)x | ((uint)y << 8) | ((uint)z << 16) | ((uint)w << 24);

    private static M2Animation ReadAnimation(Formats.M2.Root.M2RootWotlk root)
    {
        if (root.Bones.Count > M2Animation.MaxGpuBones)
            throw new InvalidDataException($"M2 contains {root.Bones.Count} bones, exceeding the renderer's {M2Animation.MaxGpuBones}-bone palette.");

        var sequences = new M2Sequence[root.Sequences.Count];
        for (var i = 0; i < sequences.Length; i++)
            sequences[i] = new M2Sequence(root.Sequences[i].Duration,
                root.Sequences[i].Flags, root.Sequences[i].AliasNext,
                root.Sequences[i].Id);

        var loops = new uint[root.GlobalLoops.Count];
        for (var i = 0; i < loops.Length; i++)
            loops[i] = root.GlobalLoops[i].Timestamp;

        var bones = new M2Bone[root.Bones.Count];
        for (var i = 0; i < bones.Length; i++)
        {
            var bone = root.Bones[i];
            bones[i] = new M2Bone(
                bone.ParentBone,
                bone.Flags,
                ToVector3(bone.Pivot),
                ReadVectorTrack(bone.Translation),
                ReadQuaternionTrack(bone.Rotation),
                ReadVectorTrack(bone.Scale));
        }
        var colors = new M2ColorAnimation[root.Colors.Count];
        for (var i = 0; i < colors.Length; i++)
        {
            var color = root.Colors[i];
            colors[i] = new M2ColorAnimation(
                ReadVectorTrack(color.Color), ReadFixedTrack(color.Alpha));
        }

        var weights = new M2Track<float>[root.TextureWeights.Count];
        for (var i = 0; i < weights.Length; i++)
            weights[i] = ReadFixedTrack(root.TextureWeights[i].Weight);

        var transforms = new M2TextureAnimation[root.TextureTransforms.Count];
        for (var i = 0; i < transforms.Length; i++)
        {
            var transform = root.TextureTransforms[i];
            transforms[i] = new M2TextureAnimation(
                ReadVectorTrack(transform.Translation),
                ReadFloatQuaternionTrack(transform.Rotation),
                ReadVectorTrack(transform.Scaling));
        }
        return new M2Animation
        {
            Bones = bones,
            Sequences = sequences,
            GlobalLoops = loops,
            HasAnimatedBones = bones.Any(bone => (bone.Flags & 0x280) != 0),
            Colors = colors,
            TextureWeights = weights,
            TextureTransforms = transforms
        };
    }

    private static M2Track<Vector3> ReadVectorTrack(Formats.M2.Root.Record.M2TrackC3Vector track)
    {
        var timelines = new M2Timeline<Vector3>[checked((int)track.TimelineCount())];
        for (ulong i = 0; i < (ulong)timelines.Length; i++)
        {
            var times = track.TimelineTimestamps(i);
            var values = track.TimelineValues(i);
            var count = Math.Min(times.Length, values.Count);
            var snapshots = new Vector3[count];
            for (var j = 0; j < count; j++)
                snapshots[j] = ToVector3(values[j]);
            timelines[i] = new M2Timeline<Vector3>(times[..count], snapshots);
        }
        return new M2Track<Vector3>
        {
            Interpolation = track.InterpolationType,
            GlobalSequence = unchecked((short)track.GlobalSequence),
            Timelines = timelines
        };
    }

    private static M2Track<Quaternion> ReadQuaternionTrack(Formats.M2.Root.Record.M2TrackCompQuat track)
    {
        var timelines = new M2Timeline<Quaternion>[checked((int)track.TimelineCount())];
        for (ulong i = 0; i < (ulong)timelines.Length; i++)
        {
            var times = track.TimelineTimestamps(i);
            var values = track.TimelineValues(i);
            var count = Math.Min(times.Length, values.Count);
            var snapshots = new Quaternion[count];
            for (var j = 0; j < count; j++)
            {
                var value = values[j];
                var q = new Quaternion(
                    DecodeCompressedComponent(value.X), DecodeCompressedComponent(value.Y),
                    DecodeCompressedComponent(value.Z), DecodeCompressedComponent(value.W));
                snapshots[j] = q.LengthSquared() > 1e-8f ? Quaternion.Normalize(q) : Quaternion.Identity;
            }
            timelines[i] = new M2Timeline<Quaternion>(times[..count], snapshots);
        }
        return new M2Track<Quaternion>
        {
            Interpolation = track.InterpolationType,
            GlobalSequence = unchecked((short)track.GlobalSequence),
            Timelines = timelines
        };
    }

    private static M2Track<float> ReadFixedTrack(Formats.M2.Root.Record.M2TrackFixed16 track)
    {
        var timelines = new M2Timeline<float>[checked((int)track.TimelineCount())];
        for (ulong i = 0; i < (ulong)timelines.Length; i++)
        {
            var times = track.TimelineTimestamps(i);
            var values = track.TimelineValues(i);
            var count = Math.Min(times.Length, values.Count);
            var snapshots = new float[count];
            for (var j = 0; j < count; j++)
                snapshots[j] = values[j].AsFloat;
            timelines[i] = new M2Timeline<float>(times[..count], snapshots);
        }
        return new M2Track<float>
        {
            Interpolation = track.InterpolationType,
            GlobalSequence = unchecked((short)track.GlobalSequence),
            Timelines = timelines
        };
    }

    private static M2Track<Quaternion> ReadFloatQuaternionTrack(
        Formats.M2.Root.Record.M2TrackC4Quaternion track)
    {
        var timelines = new M2Timeline<Quaternion>[checked((int)track.TimelineCount())];
        for (ulong i = 0; i < (ulong)timelines.Length; i++)
        {
            var times = track.TimelineTimestamps(i);
            var values = track.TimelineValues(i);
            var count = Math.Min(times.Length, values.Count);
            var snapshots = new Quaternion[count];
            for (var j = 0; j < count; j++)
            {
                var value = values[j];
                var q = new Quaternion(value.X, value.Y, value.Z, value.W);
                snapshots[j] = q.LengthSquared() > 1e-8f ? Quaternion.Normalize(q) : Quaternion.Identity;
            }
            timelines[i] = new M2Timeline<Quaternion>(times[..count], snapshots);
        }
        return new M2Track<Quaternion>
        {
            Interpolation = track.InterpolationType,
            GlobalSequence = unchecked((short)track.GlobalSequence),
            Timelines = timelines
        };
    }

    private static float DecodeCompressedComponent(short value) =>
        unchecked((ushort)value) * (2f / 65535f) - 1f;

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static uint[] ResolveTextureFileDataIds(
        Fs.FileSystem fileSystem,
        Formats.M2.M2 model,
        WoWLib.Vector<Formats.M2.Root.Record.M2Texture> textures)
    {
        var chunkIds = GetChunkTextureIds(model);
        var result = new uint[textures.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var texture = textures[i];
            // TXID replaces the legacy filename array one-for-one. It remains the
            // authoritative asset identity even when Type describes a component
            // texture slot; ignoring it leaves otherwise valid materials pink.
            var chunkId = i < chunkIds.Length ? chunkIds[i] : 0;
            var pathId = chunkId == 0 && texture.Type == 0
                ? ResolvePath(fileSystem, texture.Filename)
                : 0;
            var selected = SelectTextureFileDataId(chunkId, texture.Type, pathId);
            // Dynamic MPQ texture slots have no file path and cannot use the
            // modern client's fallback FileDataID. The renderer uses 0 as its
            // built-in placeholder texture.
            result[i] = fileSystem.Kind == StorageKind.Mpq && selected == FallbackTextureFileDataId
                ? 0
                : selected;
        }
        return result;
    }

    internal static uint SelectTextureFileDataId(uint chunkId, uint textureType, uint resolvedPathId)
    {
        if (chunkId != 0)
            return chunkId;
        if (textureType == 0 && resolvedPathId != 0)
            return resolvedPathId;
        return FallbackTextureFileDataId;
    }

    private static uint[] GetChunkTextureIds(Formats.M2.M2 model)
    {
        // All chunked M2 eras now share M2ChunkedFile. Only selecting the
        // version-specific owner remains era-dependent; the data access is
        // common and uses the zero-copy scalar span API.
        Formats.M2.Chunked.M2ChunkedFile? chunks = model switch
        {
            Formats.M2.M2Legion value => value.Chunks,
            Formats.M2.M2Bfa value => value.Chunks,
            Formats.M2.M2Shadowlands value => value.Chunks,
            Formats.M2.M2Dragonflight value => value.Chunks,
            Formats.M2.M2TheWarWithin value => value.Chunks,
            _ => null
        };

        return chunks?.TextureFdids.AsSpan().ToArray() ?? [];
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
        M2Material[] textures,
        ReadOnlySpan<M2RenderMaterial> materials,
        bool isMpq)
    {
        var textureLookupTable = root.TextureLookupTable.AsSpan();
        var weightLookupTable = root.TransparencyLookupTable.AsSpan();
        var transformLookupTable = root.TextureTransformsLookupTable.AsSpan();
        var result = new List<Submesh>(profile.Batches.Length);
        for (var i = 0; i < profile.Batches.Length; i++)
        {
            var batch = profile.Batches[i];
            if (batch.SectionIndex >= profile.Sections.Length)
                continue;

            var section = profile.Sections[batch.SectionIndex];
            var textureIndices = new int[batch.TextureCount];
            var materialIds = new uint[batch.TextureCount];
            var textureFlags = new uint[batch.TextureCount];
            for (var texture = 0; texture < batch.TextureCount; texture++)
            {
                var lookupIndex = batch.TextureComboIndex + texture;
                var textureIndex = lookupIndex < textureLookupTable.Length
                    ? textureLookupTable[lookupIndex]
                    : (ushort)0;
                textureIndices[texture] = textureIndex;
                materialIds[texture] = textureIndex < textures.Length
                    ? textures[textureIndex].fileDataID
                    : isMpq ? 0 : FallbackTextureFileDataId;
                textureFlags[texture] = textureIndex < textures.Length
                    ? textures[textureIndex].flags
                    : 0;
            }

            var material = ResolveRenderMaterial(batch.MaterialIndex, materials);
            var shaderId = isMpq && root is Formats.M2.Root.M2RootWotlk wotlkRoot
                ? ResolveWotlkShaderId(wotlkRoot, batch, material)
                : batch.ShaderId;
            result.Add(new Submesh
            {
                firstFace = section.FirstIndex,
                numFaces = section.IndexCount,
                material = materialIds,
                textureIndices = textureIndices,
                textureFlags = textureFlags,
                blendType = material.BlendMode,
                renderFlags = material.Flags,
                geosetId = section.Id,
                index = i,
                vertexShaderID = (uint)GetVertexShaderID(batch.TextureCount, shaderId),
                pixelShaderID = (uint)GetPixelShaderID(batch.TextureCount, shaderId),
                colorIndex = batch.ColorIndex < root.Colors.Count ? batch.ColorIndex : -1,
                textureWeightIndex = batch.TextureWeightComboIndex < weightLookupTable.Length &&
                    weightLookupTable[batch.TextureWeightComboIndex] < root.TextureWeights.Count
                    ? weightLookupTable[batch.TextureWeightComboIndex] : -1,
                textureTransformIndex1 = GetTextureTransformIndex(
                    transformLookupTable, batch.TextureTransformComboIndex, 0,
                    root.TextureTransforms.Count),
                textureTransformIndex2 = GetTextureTransformIndex(
                    transformLookupTable, batch.TextureTransformComboIndex, 1,
                    root.TextureTransforms.Count)
            });
        }

        return [.. result];
    }

    private static int GetTextureTransformIndex(
        ReadOnlySpan<short> lookup, int comboIndex, int stage, int transformCount)
    {
        var index = comboIndex + stage;
        if ((uint)index >= lookup.Length)
            return -1;
        var transform = lookup[index];
        return (uint)transform < transformCount ? transform : -1;
    }

    internal static M2RenderMaterial ResolveRenderMaterial(
        ushort materialIndex,
        ReadOnlySpan<M2RenderMaterial> materials) =>
        materialIndex < materials.Length ? materials[materialIndex] : default;

    private static ushort ResolveWotlkShaderId(
        Formats.M2.Root.M2RootWotlk root,
        BatchData batch,
        M2RenderMaterial material)
    {
        if ((batch.ShaderId & ShaderUsesPixelShaderTableBit) != 0 || batch.TextureCount is < 1 or > 2)
            return batch.ShaderId;

        var coordinates = root.TextureMappingLookupTable.AsSpan();
        var coordinateIndex = batch.TextureCoordComboIndex;
        if (coordinateIndex + batch.TextureCount > coordinates.Length)
            return batch.ShaderId;

        ushort shaderId = 0;
        if ((root.GlobalFlags & (uint)Formats.M2.Root.GlobalFlags.UseTextureCombinerCombos) == 0)
        {
            var operation = material.BlendMode == 0 ? 0 : 1;
            if (unchecked((ushort)coordinates[coordinateIndex]) > 2)
                operation |= 8;
            shaderId = (ushort)(operation << 4);
            if (coordinates[coordinateIndex] == 1)
                shaderId |= ShaderUsesEnvironmentBit;
            return shaderId;
        }

        var combiners = root.TextureCombinerCombos.AsSpan();
        if ((int)batch.ShaderId + batch.TextureCount > combiners.Length)
            return batch.ShaderId;

        for (var stage = 0; stage < batch.TextureCount; stage++)
        {
            var operation = stage == 0 && material.BlendMode == 0
                ? 0
                : combiners[batch.ShaderId + stage];
            var coordinate = unchecked((ushort)coordinates[coordinateIndex + stage]);
            if (coordinate > 2)
                operation |= 8;
            if (coordinate == 1 && stage + 1 == batch.TextureCount)
                shaderId |= ShaderUsesEnvironmentBit;
            shaderId |= (ushort)(operation << (stage == 0 ? 4 : 0));
        }
        return shaderId;
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
        try { return WowlibFileSystem.ResolveAssetId(fileSystem, path); }
        catch { return 0; }
    }

    private static int GetVertexShaderID(int textureCount, ushort shaderID)
    {
        if (textureCount == 1)
            return (shaderID & ShaderUsesVertexShaderBit) == 0
                ? (shaderID & ShaderUsesEnvironmentBit) != 0
                    ? (int)M2VertexShader.Diffuse_T2
                    : (int)M2VertexShader.Diffuse_T1
                : (int)M2VertexShader.Diffuse_Env;
        if ((shaderID & ShaderUsesVertexShaderBit) == 0)
        {
            var result = (shaderID & ShaderBlendModeBit) != 0
                ? M2VertexShader.Diffuse_T1_Env
                : M2VertexShader.Diffuse_T1_T1;
            return (shaderID & ShaderUsesEnvironmentBit) != 0
                ? (int)M2VertexShader.Diffuse_T1_T2
                : (int)result;
        }
        return (int)((shaderID & ShaderBlendModeBit) != 0
            ? M2VertexShader.Diffuse_Env_Env
            : M2VertexShader.Diffuse_Env_T1);
    }

    private static int GetPixelShaderID(int textureCount, ushort shaderID)
    {
        if ((shaderID & ShaderUsesPixelShaderTableBit) != 0)
        {
            var pixelShaderId = shaderID & ShaderPixelShaderIndexMask;
            if (pixelShaderId >= M2Shaders.Count)
                throw new InvalidDataException($"M2 pixel shader {pixelShaderId} is out of bounds.");
            return (int)M2Shaders[pixelShaderId].PixelShader;
        }

        if (textureCount == 1)
            return (shaderID & ShaderCombinerMask) != 0
                ? (int)M2PixelShader.Combiners_Mod
                : (int)M2PixelShader.Combiners_Opaque;

        return (shaderID & ShaderCombinerMask) != 0
            ? (shaderID & ShaderCombinerIdMask) switch
            {
                0 => (int)M2PixelShader.Combiners_Mod_Opaque,
                1 or 2 or 5 => (int)M2PixelShader.Combiners_Mod_Mod,
                3 => (int)M2PixelShader.Combiners_Mod_Add,
                4 => (int)M2PixelShader.Combiners_Mod_Mod2x,
                6 => (int)M2PixelShader.Combiners_Mod_Mod2xNA,
                7 => (int)M2PixelShader.Combiners_Mod_AddNA,
                _ => (int)M2PixelShader.Combiners_Mod_Mod
            }
            : (shaderID & ShaderCombinerIdMask) switch
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
