using System.Numerics;
using WoWFormatLib.FileProviders;
using WoWFormatLib.FileReaders;
using WoWFormatLib.Structs.M2;
using WoWFormatLib.Structs.SKIN;
using WoWRenderLib.Structs;
using static WoWRenderLib.Renderer.ShaderEnums;

namespace WoWRenderLib.Loaders
{
    public class M2Loader
    {
        private static uint DEFAULT_TEXTURE_ID = 186184; // dungeons/textures/testing/color_01.blp

        public static ParsedM2 ParseM2(uint fileDataID)
        {
            M2Model model = new();

            if (FileProvider.FileExists(fileDataID))
            {
                var modelReader = new M2Reader();
                modelReader.LoadM2(fileDataID);
                model = modelReader.model;
            }
            else
            {
                throw new FileNotFoundException("Model " + fileDataID + " does not exist!");
            }

            // Header bounds are often collision-oriented and are not guaranteed to
            // contain foliage or every vertex uploaded by this renderer. Culling must
            // describe the complete render geometry rather than collision geometry.
            var (renderBoundingBox, renderBoundingRadius) = CalculateRenderBounds(model.vertices);

            var doodadBatch = new ParsedM2()
            {
                boundingBox = renderBoundingBox,
                boundingRadius = renderBoundingRadius,
                fileDataID = fileDataID,
                vertexCount = model.vertices?.Length ?? 0,
                animationCount = model.animations?.Length ?? 0,
                particleEmitterCount = model.particleemitters?.Length ?? 0,
                boneCount = model.bones?.Length ?? 0,
                attachmentCount = model.attachments?.Length ?? 0
            };

            if (model.textures == null)
                throw new Exception("Model does not contain textures: " + fileDataID);

            if (model.skins == null)
                throw new Exception("Model does not contain skins: " + fileDataID);

            doodadBatch.geosets = model.skins[0].submeshes.Select(section => new M2Geoset
            {
                id = section.submeshID,
                level = section.level,
                firstVertex = section.startVertex,
                vertexCount = section.nVertices,
                firstIndex = section.startTriangle,
                indexCount = section.nTriangles
            }).ToArray();

            // Textures
            doodadBatch.mats = new M2Material[model.textures.Length];
            for (var i = 0; i < model.textures.Length; i++)
            {
                uint textureFileDataID = DEFAULT_TEXTURE_ID;
                doodadBatch.mats[i].flags = model.textures[i].flags;

                // TODO: Classic Era still has some M2s that use filename-based texturing
                if (model.textureFileDataIDs != null)
                {
                    switch (model.textures[i].type)
                    {
                        case 0: // NONE
                            textureFileDataID = model.textureFileDataIDs[i];
                            break;
                        case 1: // TEX_COMPONENT_SKIN
                        case 2: // TEX_COMPONENT_OBJECT_SKIN
                        case 11: // TEX_COMPONENT_MONSTER_1
                            break;
                    }
                }

                // Not set in TXID
                if (textureFileDataID == 0)
                    textureFileDataID = DEFAULT_TEXTURE_ID;

                doodadBatch.mats[i].fileDataID = textureFileDataID;
            }

            // Submeshes
            var submeshes = new List<Structs.Submesh>();
            for (int i = 0; i < model.skins[0].textureunit.Length; i++)
            {
                var batch = model.skins[0].textureunit[i];
                var skinSection = model.skins[0].submeshes[batch.submeshIndex];

                // TODO: Support
                if (batch.flags.HasFlag(TextureUnitFlags.ProjectedTexture))
                    continue;

                var materials = new uint[batch.textureCount];
                var textureIndices = new int[batch.textureCount];
                var firstFace = skinSection.startTriangle;
                var numFaces = skinSection.nTriangles;
                var blendType = model.renderflags[batch.renderFlagsIndex].blendingMode;
                var vertexShaderID = (uint)GetVertexShaderID(batch.textureCount, batch.shaderID);
                var pixelShaderID = (uint)GetPixelShaderID(batch.textureCount, batch.shaderID);

                for (var tm = 0; tm < batch.textureCount; tm++)
                {
                    var textureID = model.texlookup[batch.texture + tm].textureID;
                    textureIndices[tm] = textureID;
                    materials[tm] = doodadBatch.mats[textureID].fileDataID;
                }

                submeshes.Add(new Structs.Submesh()
                {
                    firstFace = firstFace,
                    numFaces = numFaces,
                    material = materials,
                    textureIndices = textureIndices,
                    blendType = blendType,
                    renderFlags = (ushort)model.renderflags[batch.renderFlagsIndex].flags,
                    geosetId = skinSection.submeshID,
                    index = i,
                    vertexShaderID = vertexShaderID,
                    pixelShaderID = pixelShaderID
                });
            }

            doodadBatch.submeshes = [.. submeshes];

            var modelvertices = new M2Vertex[model.vertices.Length];

            for (var i = 0; i < model.vertices.Length; i++)
            {
                modelvertices[i].Position = new Vector3(model.vertices[i].position.X, model.vertices[i].position.Y, model.vertices[i].position.Z);
                modelvertices[i].Normal = new Vector3(model.vertices[i].normal.X, model.vertices[i].normal.Y, model.vertices[i].normal.Z);
                modelvertices[i].TexCoord1 = new Vector2(model.vertices[i].textureCoordX, model.vertices[i].textureCoordY);
                modelvertices[i].TexCoord2 = new Vector2(model.vertices[i].textureCoordX2, model.vertices[i].textureCoordY2);
            }

            unsafe
            {
                fixed (M2Vertex* ptr = modelvertices)
                {
                    doodadBatch.vertexBytes = new byte[modelvertices.Length * sizeof(M2Vertex)];
                    fixed (byte* dst = doodadBatch.vertexBytes)
                        Buffer.MemoryCopy(ptr, dst, doodadBatch.vertexBytes.Length, doodadBatch.vertexBytes.Length);
                }
            }

            var modelindices = new ushort[model.skins[0].triangles.Length * 3];
            doodadBatch.indexCount = modelindices.Length;

            for (var i = 0; i < model.skins[0].triangles.Length; i++)
            {
                modelindices[i * 3] = model.skins[0].triangles[i].pt1;
                modelindices[i * 3 + 1] = model.skins[0].triangles[i].pt2;
                modelindices[i * 3 + 2] = model.skins[0].triangles[i].pt3;
            }

            unsafe
            {
                fixed (ushort* ptr = modelindices)
                {
                    doodadBatch.indiceBytes = new byte[modelindices.Length * sizeof(ushort)];
                    fixed (byte* dst = doodadBatch.indiceBytes)
                        Buffer.MemoryCopy(ptr, dst, doodadBatch.indiceBytes.Length, doodadBatch.indiceBytes.Length);
                }
            }

            return doodadBatch;
        }

        public static (BoundingBox BoundingBox, float Radius) CalculateRenderBounds(
            ReadOnlySpan<Vertice> vertices)
        {
            if (vertices.IsEmpty)
                return (new BoundingBox(Vector3.Zero, Vector3.Zero), 0f);

            var min = vertices[0].position;
            var max = vertices[0].position;
            for (var index = 1; index < vertices.Length; index++)
            {
                min = Vector3.Min(min, vertices[index].position);
                max = Vector3.Max(max, vertices[index].position);
            }

            var center = (min + max) * 0.5f;
            var maximumDistanceSquared = 0f;
            for (var index = 0; index < vertices.Length; index++)
            {
                maximumDistanceSquared = MathF.Max(
                    maximumDistanceSquared,
                    Vector3.DistanceSquared(center, vertices[index].position));
            }

            return (new BoundingBox(min, max), MathF.Sqrt(maximumDistanceSquared));
        }

        // Based on previously reverse engineerd logic by Deamon: https://github.com/Deamon87/WebWowViewerCpp/blob/master/wowViewerLib/src/engine/objects/m2/m2Object.cpp#L146
        private static int GetVertexShaderID(int textureCount, ushort shaderID)
        {
            int result = 0;
            if (shaderID >= 0)
            {
                if (textureCount == 1)
                {
                    if ((shaderID & 0x80u) == 0)
                        return ((shaderID & 0x4000) != 0 ? 10 : 0);
                    else
                        result = 1;
                }
                else if ((shaderID & 0x80u) == 0)
                {
                    if ((shaderID & 8) != 0)
                        return 3;
                    else
                        result = 7;
                    if ((shaderID & 0x4000) != 0)
                        return 2;
                }
                else if ((shaderID & 8) != 0)
                    return 5;
                else
                    return 4;
            }
            else if (shaderID < 0)
            {
                int vertexShaderId = shaderID & 0x7FFF;
                if (vertexShaderId >= M2Shaders.Count)
                    throw new Exception("Shader ID " + vertexShaderId + " is out of bounds for M2 shader list (" + M2Shaders.Count + ")");

                result = (int)M2Shaders[vertexShaderId].VertexShader;
            }

            return result;
        }

        private static int GetPixelShaderID(int textureCount, ushort shaderID)
        {
            int result;
            if ((shaderID & 0x8000) > 0)
            {
                int pixelShaderId = shaderID & 0x7FFF;
                if (pixelShaderId >= M2Shaders.Count)
                    throw new Exception("Shader ID " + pixelShaderId + " is out of bounds for M2 shader list (" + M2Shaders.Count + ")");

                result = (int)M2Shaders[shaderID & 0x7FFF].PixelShader;
            }
            else if (textureCount == 1)
            {
                result = (shaderID & 0x70) != 0 ? (int)M2PixelShader.Combiners_Mod : (int)M2PixelShader.Combiners_Opaque;
            }
            else
            {
                if ((shaderID & 0x70) != 0)
                {
                    result = (shaderID & 7) switch
                    {
                        0 => (int)M2PixelShader.Combiners_Mod_Opaque,
                        1 or 2 or 5 => (int)M2PixelShader.Combiners_Mod_Mod,
                        3 => (int)M2PixelShader.Combiners_Mod_Add,
                        4 => (int)M2PixelShader.Combiners_Mod_Mod2x,
                        6 => (int)M2PixelShader.Combiners_Mod_Mod2xNA,
                        7 => (int)M2PixelShader.Combiners_Mod_AddNA,
                        _ => (int)M2PixelShader.Combiners_Mod_Mod,
                    };
                }
                else
                {
                    result = (shaderID & 7) switch
                    {
                        0 => (int)M2PixelShader.Combiners_Opaque_Opaque,
                        1 or 2 or 5 => (int)M2PixelShader.Combiners_Opaque_Mod,
                        3 or 7 => (int)M2PixelShader.Combiners_Opaque_AddAlpha,
                        4 => (int)M2PixelShader.Combiners_Opaque_Mod2x,
                        6 => (int)M2PixelShader.Combiners_Opaque_Mod2xNA,
                        _ => (int)M2PixelShader.Combiners_Opaque_Mod,
                    };
                }
            }
            return result;
        }
    }
}
