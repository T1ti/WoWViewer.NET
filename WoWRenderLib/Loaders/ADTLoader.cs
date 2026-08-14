using System.Numerics;
using System.Runtime.InteropServices;
using WoWFormatLib.FileProviders;
using WoWFormatLib.FileReaders;
using WoWFormatLib.Structs.ADT;
using WoWRenderLib.Cache;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Loaders
{
    public class ADTLoader
    {
        public static ParsedADT ParseADT(MapTile mapTile)
        {
            ParsedADT parsedADT = new();
            ADTReader adtReader = new();

            var wdt = WDTCache.GetOrLoad(mapTile.wdtFileDataID);

            var rootADTFileDataID = adtReader.LoadADT(wdt, mapTile.tileX, mapTile.tileY, true, "");
            var adt = adtReader.adtfile;

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

                    var material = new ADTMaterial
                    {
                        texture = (int)diffuseTextureFDID
                    };

                    usedBLPFileDataIDs.Add(diffuseTextureFDID);

                    if (adt.texParams != null && adt.texParams.Length > ti)
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
                            }
                            else
                            {
                                var heightTextureFDID = adt.heightTextureFileDataIDs[ti];
                                material.heightTexture = (int)heightTextureFDID;
                                usedBLPFileDataIDs.Add(heightTextureFDID);
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

            var renderBatches = new ParsedADTRenderBatch[256];

            var vertices = new ADTVertex[256 * 145];
            var indices = new int[256 * 768];
            var farLodIndices = new int[256 * 384];
            var verticesOffset = 0;
            var indicesOffset = 0;
            var farLodIndicesOffset = 0;

            var chunkBounds = new BoundingBox[256];
            var holesHighRes = new byte[8];
            var defaultVertexColor = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);
            for (int c = 0; c < adt.chunks.Length; c++)
            {
                var batch = new ParsedADTRenderBatch();

                var chunk = adt.chunks[c];

                var chunkMinBounds = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var chunkMaxBounds = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                bool hasMCCV = chunk.header.flags.HasFlag(MCNKFlags.mcnk_has_mccv);
                for (int i = 0, idx = 0; i < 17; i++)
                {
                    var isInnerVertice = (i % 2) != 0;
                    var halfHeight = i * 0.5f;
                    for (var j = 0; j < (isInnerVertice ? 8 : 9); j++)
                    {
                        var v = new ADTVertex
                        {
                            Normal = new Vector3(chunk.normals.normal_0[idx], chunk.normals.normal_1[idx], chunk.normals.normal_2[idx]),
                            Color = hasMCCV ? new Vector4(chunk.vertexShading.blue[idx] / 255.0f, chunk.vertexShading.green[idx] / 255.0f, chunk.vertexShading.red[idx] / 255.0f, chunk.vertexShading.alpha[idx] / 255.0f) : defaultVertexColor,
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

                if (c == 0)
                    parsedADT.startPos = vertices[0].Position;

                holesHighRes[0] = chunk.header.holesHighRes_0;
                holesHighRes[1] = chunk.header.holesHighRes_1;
                holesHighRes[2] = chunk.header.holesHighRes_2;
                holesHighRes[3] = chunk.header.holesHighRes_3;
                holesHighRes[4] = chunk.header.holesHighRes_4;
                holesHighRes[5] = chunk.header.holesHighRes_5;
                holesHighRes[6] = chunk.header.holesHighRes_6;
                holesHighRes[7] = chunk.header.holesHighRes_7;

                bool isHighResHoles = chunk.header.flags.HasFlag(MCNKFlags.mcnk_high_res_holes);

                int off = c * 145;
                for (int j = 9, xx = 0, yy = 0; j < 145; j++, xx++)
                {
                    if (xx >= 8) { xx = 0; ++yy; }
                    var isHole = true;

                    // Check if chunk is using low-res holes
                    if (!isHighResHoles)
                    {
                        // Calculate current hole number
                        var currentHole = 1 << ((xx / 2) + (yy / 2) * 4);

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

                        for (var triangleIndex = 0; triangleIndex < 6; triangleIndex++)
                            farLodIndices[farLodIndicesOffset++] = 0;
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

                        var topLeft = off + yy * 17 + xx;
                        var topRight = topLeft + 1;
                        var bottomLeft = off + (yy + 1) * 17 + xx;
                        var bottomRight = bottomLeft + 1;
                        farLodIndices[farLodIndicesOffset++] = bottomLeft;
                        farLodIndices[farLodIndicesOffset++] = topLeft;
                        farLodIndices[farLodIndicesOffset++] = topRight;
                        farLodIndices[farLodIndicesOffset++] = topRight;
                        farLodIndices[farLodIndicesOffset++] = bottomRight;
                        farLodIndices[farLodIndicesOffset++] = bottomLeft;
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

                if (adt.diffuseTextureFileDataIDs == null)
                    continue;

                for (byte li = 0; li < chunk.layers!.Length; li++)
                {
                    var diffuseTextureID = adt.diffuseTextureFileDataIDs[chunk.layers[li].textureId];

                    if (chunk.alphaLayer != null)
                        alphaLayers.Add(li, chunk.alphaLayer[li]);

                    ADTMaterial curMat = materials[diffuseTextureID];
                    layerMaterials[li] = (int)diffuseTextureID;
                    usedBLPFileDataIDs.Add(diffuseTextureID);

                    layerHeights[li] = curMat.heightTexture;
                    layerScales[li] = curMat.scale;
                    heightScales[li] = curMat.heightScale;
                    heightOffsets[li] = curMat.heightOffset;
                }

                var alphaLayerMats = new byte[2][];

                for (int li = 0; li < 2; li++)
                {
                    int baseLayer = li * 4;
                    alphaLayers.TryGetValue(baseLayer, out var l0);
                    alphaLayers.TryGetValue(baseLayer + 1, out var l1);
                    alphaLayers.TryGetValue(baseLayer + 2, out var l2);
                    alphaLayers.TryGetValue(baseLayer + 3, out var l3);

                    if (l0 == null && l1 == null && l2 == null && l3 == null) continue;

                    var alphaData = new byte[64 * 64 * 4];
                    for (int y = 0; y < 64; y++)
                    {
                        for (int x = 0; x < 64; x++)
                        {
                            var idx = (y * 64 + x) * 4;
                            alphaData[idx] = l0 != null ? l0[y * 64 + x] : (byte)0;
                            alphaData[idx + 1] = l1 != null ? l1[y * 64 + x] : (byte)0;
                            alphaData[idx + 2] = l2 != null ? l2[y * 64 + x] : (byte)0;
                            alphaData[idx + 3] = l3 != null ? l3[y * 64 + x] : (byte)0;
                        }
                    }

                    alphaLayerMats[li] = alphaData;
                }

                batch.heightScales = heightScales;
                batch.heightOffsets = heightOffsets;
                batch.materialFDIDs = layerMaterials;
                batch.heightMaterialFDIDs = layerHeights;
                batch.alphaMaterials = alphaLayerMats;
                batch.scales = layerScales;
                renderBatches[c] = batch;

                chunkBounds[c] = new BoundingBox
                {
                    Min = chunkMinBounds,
                    Max = chunkMaxBounds
                };
            }

            parsedADT.vertexBuffer = MemoryMarshal.AsBytes(vertices.AsSpan()).ToArray();
            parsedADT.indiceBuffer = MemoryMarshal.AsBytes(indices.AsSpan()).ToArray();
            parsedADT.farLodIndiceBuffer = MemoryMarshal.AsBytes(farLodIndices.AsSpan()).ToArray();

            var doodads = new Doodad[adt.objects.models.entries.Length];
            for (var mi = 0; mi < adt.objects.models.entries.Length; mi++)
            {
                var modelentry = adt.objects.models.entries[mi];

                doodads[mi] = new Doodad
                {
                    position = new Vector3(-(modelentry.position.X - 17066.666f), modelentry.position.Y, (modelentry.position.Z - 17066.666f)),
                    rotation = new Vector3(modelentry.rotation.X, modelentry.rotation.Y, modelentry.rotation.Z),
                    scale = modelentry.scale / 1024.0f,
                    fileDataID = modelentry.mmidEntry,
                    uniqueID = modelentry.uniqueId,
                    flags = (ushort)modelentry.flags
                };
            }

            var worldModelBatches = new WorldModelBatch[adt.objects.worldModels.entries.Length];
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
                    if (wmodelentry.doodadSet < adt.objects.worldModelDoodadRefs.Length)
                    {
                        var mwdrEntry = adt.objects.worldModelDoodadRefs[wmodelentry.doodadSet];
                        for (var i = mwdrEntry.begin; i < mwdrEntry.end; i++)
                        {
                            if (i >= adt.objects.worldModelDoodadSets.Length)
                                break;

                            doodadSets.Add(adt.objects.worldModelDoodadSets[i]);
                        }
                    }
                }

                worldModelBatches[wmi] = new WorldModelBatch
                {
                    position = new Vector3(-(wmodelentry.position.X - 17066.666f), wmodelentry.position.Y, (wmodelentry.position.Z - 17066.666f)),
                    rotation = new Vector3(wmodelentry.rotation.X, wmodelentry.rotation.Y, wmodelentry.rotation.Z),
                    fileDataID = wmoFDID,
                    uniqueID = wmodelentry.uniqueId,
                    flags = (ushort)wmodelentry.flags,
                    doodadSet = wmodelentry.doodadSet,
                    nameSet = wmodelentry.nameSet,
                    scale = wmodelentry.scale / 1024.0f,
                    doodadSetIDs = [.. doodadSets]
                };
            }

            parsedADT.renderBatches = renderBatches;
            parsedADT.doodads = doodads;
            parsedADT.worldModelBatches = worldModelBatches;
            parsedADT.rootADTFileDataID = rootADTFileDataID;
            parsedADT.chunkBounds = chunkBounds;
            parsedADT.blpFileDataIDs = [.. usedBLPFileDataIDs];

            return parsedADT;
        }
    }
}
