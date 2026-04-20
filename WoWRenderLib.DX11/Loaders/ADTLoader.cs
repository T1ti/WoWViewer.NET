using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using WoWFormatLib.FileProviders;
using WoWFormatLib.FileReaders;
using WoWFormatLib.Structs.ADT;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Loaders
{
    class ADTLoader
    {
        public static unsafe Terrain LoadADT(ComPtr<ID3D11Device> device, Structs.MapTile mapTile, CompiledShader shaderProgram)
        {
            ADT adt = new();
            Terrain result = new();
            ADTReader adtReader = new();

            var wdt = WDTCache.GetOrLoad(mapTile.wdtFileDataID);

            var rootADTFileDataID = adtReader.LoadADT(wdt, mapTile.tileX, mapTile.tileY, true, "");
            adt = adtReader.adtfile;

            var TileSize = 1600.0f / 3.0f; //533.333
            var ChunkSize = TileSize / 16.0f; //33.333
            var UnitSize = ChunkSize / 8.0f; //4.166666
            var MapMidPoint = 32.0f / ChunkSize;

            List<uint> usedBLPFileDataIDs = [];

            var materials = new Dictionary<uint, ADTMaterial>();

            if (adt.textures.filenames == null)
            {
                for (var ti = 0; ti < adt.diffuseTextureFileDataIDs.Length; ti++)
                {
                    var diffuseTextureFDID = adt.diffuseTextureFileDataIDs[ti];
                    BLPCache.GetOrLoad(device, diffuseTextureFDID, rootADTFileDataID);

                    var material = new ADTMaterial
                    {
                        texture = (int)diffuseTextureFDID
                    };

                    usedBLPFileDataIDs.Add(diffuseTextureFDID);

                    if (adt.texParams != null && adt.texParams.Length >= ti)
                    {
                        material.scale = (float)Math.Pow(2, (adt.texParams[ti].flags & 0xF0) >> 4);
                        if (adt.texParams[ti].height != 0.0 || adt.texParams[ti].offset != 1.0)
                        {
                            material.heightScale = adt.texParams[ti].height;
                            material.heightOffset = adt.texParams[ti].offset;

                            if (!FileProvider.FileExists(adt.heightTextureFileDataIDs[ti]))
                            {
                                material.heightTexture = (int)diffuseTextureFDID;
                                usedBLPFileDataIDs.Add(diffuseTextureFDID);
                                BLPCache.GetOrLoad(device, diffuseTextureFDID, rootADTFileDataID);
                            }
                            else
                            {
                                var heightTextureFDID = adt.heightTextureFileDataIDs[ti];
                                material.heightTexture = (int)heightTextureFDID;
                                usedBLPFileDataIDs.Add(heightTextureFDID);
                                BLPCache.GetOrLoad(device, heightTextureFDID, rootADTFileDataID);
                            }
                        }
                        else
                        {
                            material.heightScale = 0.0f;
                            material.heightOffset = 1.0f;
                        }
                    }
                    else
                    {
                        material.heightScale = 0.0f;
                        material.heightOffset = 1.0f;
                        material.scale = 1.0f;
                    }
                    materials.Add(diffuseTextureFDID, material);
                }
            }
            else
            {
                throw new Exception("Filename-based loading yeeted");
            }

            var initialChunkY = adt.chunks[0].header.position.Y;
            var initialChunkX = adt.chunks[0].header.position.X;

            var renderBatches = new List<ADTRenderBatch>(256);

            var vertices = new ADTVertex[256 * 145];
            var indices = new int[256 * 768];
            var verticesOffset = 0;
            var indicesOffset = 0;

            var chunkBounds = new BoundingBox[256];

            for (int c = 0; c < adt.chunks.Length; c++)
            {
                var batch = new ADTRenderBatch();

                var chunk = adt.chunks[c];

                var chunkMinBounds = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var chunkMaxBounds = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                for (int i = 0, idx = 0; i < 17; i++)
                {
                    var isInnerVertice = (i % 2) != 0;
                    var halfHeight = i * 0.5f;
                    for (var j = 0; j < (isInnerVertice ? 8 : 9); j++)
                    {
                        var v = new ADTVertex
                        {
                            Normal = new Vector3(chunk.normals.normal_0[idx], chunk.normals.normal_1[idx], chunk.normals.normal_2[idx]),
                            Color = chunk.header.flags.HasFlag(MCNKFlags.mcnk_has_mccv) ? new Vector4(chunk.vertexShading.blue[idx] / 255.0f, chunk.vertexShading.green[idx] / 255.0f, chunk.vertexShading.red[idx] / 255.0f, chunk.vertexShading.alpha[idx] / 255.0f) : new Vector4(0.5f, 0.5f, 0.5f, 1.0f),
                            TexCoord = new Vector2((j + (isInnerVertice ? 0.5f : 0f)) / 8f, (halfHeight) / 8f),
                            Position = new Vector3(chunk.header.position.X - (halfHeight * UnitSize), chunk.header.position.Y - (j * UnitSize), chunk.vertices.vertices[idx++] + chunk.header.position.Z)
                        };

                        if (isInnerVertice)
                            v.Position.Y -= 0.5f * UnitSize;

                        chunkMinBounds = Vector3.Min(chunkMinBounds, v.Position);
                        chunkMaxBounds = Vector3.Max(chunkMaxBounds, v.Position);

                        vertices[verticesOffset++] = v;
                    }
                }

                result.startPos = vertices[0].Position;

                var holesHighRes = new byte[8];
                holesHighRes[0] = chunk.header.holesHighRes_0;
                holesHighRes[1] = chunk.header.holesHighRes_1;
                holesHighRes[2] = chunk.header.holesHighRes_2;
                holesHighRes[3] = chunk.header.holesHighRes_3;
                holesHighRes[4] = chunk.header.holesHighRes_4;
                holesHighRes[5] = chunk.header.holesHighRes_5;
                holesHighRes[6] = chunk.header.holesHighRes_6;
                holesHighRes[7] = chunk.header.holesHighRes_7;

                int off = c * 145;
                for (int j = 9, xx = 0, yy = 0; j < 145; j++, xx++)
                {
                    if (xx >= 8) { xx = 0; ++yy; }
                    var isHole = true;

                    // Check if chunk is using low-res holes
                    if (!chunk.header.flags.HasFlag(MCNKFlags.mcnk_high_res_holes))
                    {
                        // Calculate current hole number
                        var currentHole = (int)Math.Pow(2,
                                Math.Floor(xx / 2f) * 1f +
                                Math.Floor(yy / 2f) * 4f);

                        // Check if current hole number should be a hole
                        if ((chunk.header.holesLowRes & currentHole) == 0)
                        {
                            isHole = false;
                        }
                    }
                    else
                    {
                        // Check if current section is a hole
                        if (((holesHighRes[yy] >> xx) & 1) == 0)
                        {
                            isHole = false;
                        }
                    }

                    if (isHole)
                    {
                        indices[indicesOffset++] = 0;
                        indices[indicesOffset++] = 0;
                        indices[indicesOffset++] = 0;

                        indices[indicesOffset++] = 0;
                        indices[indicesOffset++] = 0;
                        indices[indicesOffset++] = 0;

                        indices[indicesOffset++] = 0;
                        indices[indicesOffset++] = 0;
                        indices[indicesOffset++] = 0;

                        indices[indicesOffset++] = 0;
                        indices[indicesOffset++] = 0;
                        indices[indicesOffset++] = 0;
                    }
                    else
                    {
                        indices[indicesOffset++] = off + j + 8;
                        indices[indicesOffset++] = off + j - 9;
                        indices[indicesOffset++] = off + j;

                        indices[indicesOffset++] = off + j - 9;
                        indices[indicesOffset++] = off + j - 8;
                        indices[indicesOffset++] = off + j;

                        indices[indicesOffset++] = off + j - 8;
                        indices[indicesOffset++] = off + j + 9;
                        indices[indicesOffset++] = off + j;

                        indices[indicesOffset++] = off + j + 9;
                        indices[indicesOffset++] = off + j + 8;
                        indices[indicesOffset++] = off + j;
                    }

                    if ((j + 1) % (9 + 8) == 0) j += 9;
                }

                var layerMaterials = new int[8];
                Array.Fill(layerMaterials, -1);

                var layerHeights = new int[8];
                Array.Fill(layerHeights, -1);

                var layerScales = new float[8];
                Array.Fill(layerScales, 1.0f);

                var heightScales = new float[8];
                Array.Fill(heightScales, 1.0f);

                var heightOffsets = new float[8];
                Array.Fill(heightOffsets, 1.0f);

                var alphaLayers = new Dictionary<int, byte[]>(chunk.layers?.Length ?? 4);

                for (byte li = 0; li < chunk.layers!.Length; li++)
                {
                    if (adt.diffuseTextureFileDataIDs == null)
                        continue;

                    var diffuseTextureID = adt.diffuseTextureFileDataIDs[chunk.layers[li].textureId];

                    if (chunk.alphaLayer != null)
                        alphaLayers.Add(li, chunk.alphaLayer[li].layer);

                    ADTMaterial curMat = materials[diffuseTextureID];
                    layerMaterials[li] = (int)diffuseTextureID;
                    usedBLPFileDataIDs.Add(diffuseTextureID);

                    layerHeights[li] = curMat.heightTexture;
                    layerScales[li] = curMat.scale;
                    heightScales[li] = curMat.heightScale;
                    heightOffsets[li] = curMat.heightOffset;
                }

                var alphaLayerMats = new ComPtr<ID3D11ShaderResourceView>[2];
                Array.Fill(alphaLayerMats, default);

                for (int li = 0; li < 2; li++)
                {
                    var hasAlphas = false;

                    if (!alphaLayers.TryGetValue(0 + (li * 4), out var alphaLayer0))
                        alphaLayer0 = new byte[4096];
                    else
                        hasAlphas = true;

                    if (!alphaLayers.TryGetValue(1 + (li * 4), out var alphaLayer1))
                        alphaLayer1 = new byte[4096];
                    else
                        hasAlphas = true;

                    if (!alphaLayers.TryGetValue(2 + (li * 4), out var alphaLayer2))
                        alphaLayer2 = new byte[4096];
                    else
                        hasAlphas = true;

                    if (!alphaLayers.TryGetValue(3 + (li * 4), out var alphaLayer3))
                        alphaLayer3 = new byte[4096];
                    else
                        hasAlphas = true;

                    if (!hasAlphas)
                        continue;

                    var alphaData = new byte[64 * 64 * 4];
                    for (int x = 0; x < 64; x++)
                    {
                        for (int y = 0; y < 64; y++)
                        {
                            var idx = (y * 64 + x) * 4;
                            alphaData[idx] = alphaLayer0[y * 64 + x];
                            alphaData[idx + 1] = alphaLayer1[y * 64 + x];
                            alphaData[idx + 2] = alphaLayer2[y * 64 + x];
                            alphaData[idx + 3] = alphaLayer3[y * 64 + x];
                        }
                    }

                    alphaLayerMats[li] = BLPLoader.GenerateAlphaTexture(device, alphaData);
                }

                batch.heightScales = heightScales;
                batch.heightOffsets = heightOffsets;
                batch.materialFDIDs = layerMaterials;
                batch.heightMaterialFDIDs = layerHeights;
                batch.alphaMaterialID = alphaLayerMats;
                batch.scales = layerScales;
                renderBatches.Add(batch);

                chunkBounds[c] = new BoundingBox
                {
                    Min = chunkMinBounds,
                    Max = chunkMaxBounds
                };
            }

            var bufferDesc = new BufferDesc
            {
                ByteWidth = (uint)(vertices.Length * sizeof(ADTVertex)),
                Usage = Usage.Default,
                BindFlags = (uint)BindFlag.VertexBuffer
            };

            fixed (ADTVertex* vertexData = vertices)
            {
                var subresourceData = new SubresourceData
                {
                    PSysMem = vertexData
                };

                SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, in subresourceData, ref result.vertexBuffer));
            }

            bufferDesc = new BufferDesc
            {
                ByteWidth = (uint)(indices.Length * sizeof(int)),
                Usage = Usage.Default,
                BindFlags = (uint)BindFlag.IndexBuffer
            };

            fixed (int* indexData = indices)
            {
                var subresourceData = new SubresourceData
                {
                    PSysMem = indexData
                };

                SilkMarshal.ThrowHResult(device.CreateBuffer(in bufferDesc, in subresourceData, ref result.indiceBuffer));
            }

            var doodads = new List<Doodad>(adt.objects.models.entries.Length);
            var worldModelBatches = new List<WorldModelBatch>(adt.objects.worldModels.entries.Length);

            for (var mi = 0; mi < adt.objects.models.entries.Length; mi++)
            {
                var modelentry = adt.objects.models.entries[mi];

                doodads.Add(new Doodad
                {
                    position = new Vector3(-(modelentry.position.X - 17066), modelentry.position.Y, (modelentry.position.Z - 17066)),
                    rotation = new Vector3(modelentry.rotation.X, modelentry.rotation.Y, modelentry.rotation.Z),
                    scale = modelentry.scale / 1024.0f,
                    fileDataID = modelentry.mmidEntry
                });
            }

            for (var wmi = 0; wmi < adt.objects.worldModels.entries.Length; wmi++)
            {
                var wmodelentry = adt.objects.worldModels.entries[wmi];
                var wmoFDID = wmodelentry.mwidEntry;

                var doodadSets = new List<uint>();

                if (!wmodelentry.flags.HasFlag(MODFFlags.modf_use_sets_from_mwds))
                {
                    doodadSets.Add(wmodelentry.doodadSet);
                }
                else
                {
                    var mwdrEntry = adt.objects.worldModelDoodadRefs[wmodelentry.doodadSet];
                    for (var i = 0; i < mwdrEntry.begin; i++)
                    {
                        if (mwdrEntry.end <= i)
                            break;

                        doodadSets.Add(adt.objects.worldModelDoodadSets[i]);
                    }
                }

                worldModelBatches.Add(new WorldModelBatch
                {
                    position = new Vector3(-(wmodelentry.position.X - 17066.666f), wmodelentry.position.Y, (wmodelentry.position.Z - 17066.666f)),
                    rotation = new Vector3(wmodelentry.rotation.X, wmodelentry.rotation.Y, wmodelentry.rotation.Z),
                    fileDataID = wmoFDID,
                    uniqueID = wmodelentry.uniqueId,
                    scale = wmodelentry.scale / 1024.0f,
                    doodadSetIDs = [.. doodadSets]
                });
            }

            result.renderBatches = [.. renderBatches];
            result.doodads = [.. doodads];
            result.worldModelBatches = [.. worldModelBatches];
            result.rootADTFileDataID = rootADTFileDataID;
            result.chunkBounds = chunkBounds;
            result.blpFileDataIDs = [.. usedBLPFileDataIDs];

            return result;
        }

        public static void UnloadTerrain(Terrain terrain)
        {
            terrain.vertexBuffer.Dispose();
            terrain.indiceBuffer.Dispose();

            foreach (var usedWMO in terrain.worldModelBatches)
                WMOCache.Release(usedWMO.fileDataID, terrain.rootADTFileDataID);

            foreach (var usedM2 in terrain.doodads)
                M2Cache.Release(usedM2.fileDataID, terrain.rootADTFileDataID);

            foreach (var usedBLP in terrain.blpFileDataIDs)
                BLPCache.Release(usedBLP, terrain.rootADTFileDataID);

            foreach (var batch in terrain.renderBatches)
            {
                // cant dispose material/heightmaterials here, they have to be released by blpcache above when therse no more users

                foreach (var alphaMatID in batch.alphaMaterialID)
                    alphaMatID.Dispose();
            }
        }
    }
}
