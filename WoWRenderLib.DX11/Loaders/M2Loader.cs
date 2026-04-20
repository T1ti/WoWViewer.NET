using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using System.Runtime.InteropServices;
using WoWFormatLib.FileProviders;
using WoWFormatLib.FileReaders;
using WoWFormatLib.Structs.M2;
using WoWFormatLib.Structs.SKIN;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Structs;
using static WoWRenderLib.DX11.Renderer.ShaderEnums;

namespace WoWRenderLib.DX11.Loaders
{
    class M2Loader
    {
        private static uint DEFAULT_TEXTURE_ID = 186184; // dungeons/textures/testing/color_01.blp

        public static unsafe DoodadBatch LoadM2(ComPtr<ID3D11Device> device, uint fileDataID, CompiledShader shaderProgram)
        {
            M2Model model = new M2Model();

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

            Vector3 bbMin, bbMax;
            float bbRadius;

            if (model.boundingbox == null || model.boundingbox.Length < 2 || (model.boundingbox[0].X == 0 && model.boundingbox[0].Y == 0 && model.boundingbox[0].Z == 0 && model.boundingbox[1].X == 0 && model.boundingbox[1].Y == 0 && model.boundingbox[1].Z == 0))
            {
                if (model.vertices != null && model.vertices.Length > 0)
                {
                    // TODO: figure out how to make our own bounding box
                    bbMin = Vector3.Zero;
                    bbMax = Vector3.Zero;
                    bbRadius = 0;
                }
                else
                {
                    // no vertices so this is probably only particle effects?
                    bbMin = Vector3.Zero;
                    bbMax = Vector3.Zero;
                    bbRadius = 0;
                }
            }
            else
            {
                bbMin = new Vector3(model.boundingbox[0].X, model.boundingbox[0].Y, model.boundingbox[0].Z);
                bbMax = new Vector3(model.boundingbox[1].X, model.boundingbox[1].Y, model.boundingbox[1].Z);
                bbRadius = model.boundingradius;
            }

            var doodadBatch = new DoodadBatch()
            {
                boundingBox = new BoundingBox()
                {
                    Min = bbMin,
                    Max = bbMax
                },
                boundingRadius = bbRadius,
                fileDataID = fileDataID
            };

            if (model.textures == null)
                throw new Exception("Model does not contain textures: " + fileDataID);

            if (model.skins == null)
                throw new Exception("Model does not contain skins: " + fileDataID);

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
                // doodadBatch.mats[i].textureID = BLPCache.GetOrLoad(device, textureFileDataID, fileDataID);
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

                var materials = new List<uint>();
                var firstFace = skinSection.startTriangle;
                var numFaces = skinSection.nTriangles;
                var blendType = model.renderflags[batch.renderFlagsIndex].blendingMode;
                var vertexShaderID = (uint)GetVertexShaderID(batch.textureCount, batch.shaderID);
                var pixelShaderID = (uint)GetPixelShaderID(batch.textureCount, batch.shaderID);

                for (var tm = 0; tm < batch.textureCount; tm++)
                {
                    var textureID = model.texlookup[batch.texture + tm].textureID;
                    materials.Add(doodadBatch.mats[textureID].fileDataID);

                    // TODO: do we want to do this in this loop? does this create too many users?
                    BLPCache.GetOrLoad(device, doodadBatch.mats[textureID].fileDataID, fileDataID);
                }

                submeshes.Add(new Structs.Submesh()
                {
                    firstFace = firstFace,
                    numFaces = numFaces,
                    material = [.. materials],
                    blendType = blendType,
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

            ComPtr<ID3D11Buffer> vertexBuffer = default;

            if (modelvertices.Length > 0)
            {
                var bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)Marshal.SizeOf<M2Vertex>() * (uint)modelvertices.Length,
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.VertexBuffer
                };

                fixed (M2Vertex* vertexData = modelvertices)
                {
                    var subresourceData = new SubresourceData
                    {
                        PSysMem = vertexData
                    };

                    SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, in subresourceData, ref vertexBuffer));
                }
            }

            doodadBatch.vertexBuffer = vertexBuffer;

            var modelindices = new ushort[model.skins[0].triangles.Length * 3];

            for (var i = 0; i < model.skins[0].triangles.Length; i++)
            {
                modelindices[i * 3] = model.skins[0].triangles[i].pt1;
                modelindices[i * 3 + 1] = model.skins[0].triangles[i].pt2;
                modelindices[i * 3 + 2] = model.skins[0].triangles[i].pt3;
            }

            ComPtr<ID3D11Buffer> indiceBuffer = default;

            if (modelindices.Length > 0)
            {
                var bufferDesc = new BufferDesc
                {
                    ByteWidth = (uint)(modelindices.Length * sizeof(ushort)),
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.IndexBuffer
                };

                fixed (ushort* indiceData = modelindices)
                {
                    var subresourceData = new SubresourceData
                    {
                        PSysMem = indiceData
                    };

                    SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, in subresourceData, ref indiceBuffer));
                }
            }

            doodadBatch.indiceBuffer = indiceBuffer;

            return doodadBatch;
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

        public static void UnloadM2(DoodadBatch model)
        {
            model.vertexBuffer.Dispose();
            model.indiceBuffer.Dispose();

            foreach (var submesh in model.submeshes)
            {
                foreach(var material in submesh.material)
                {
                    BLPCache.Release(material, model.fileDataID);
                }
            }

        }
    }
}
