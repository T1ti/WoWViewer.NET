using Silk.NET.OpenGL;
using System.Numerics;
using WoWFormatLib.FileProviders;
using WoWFormatLib.FileReaders;
using WoWFormatLib.Structs.ADT;
using WoWViewer.NET.Renderer;
using WoWViewer.NET.Structs;

namespace WoWViewer.NET.Loaders
{
    class ADTLoader
    {
        public static unsafe Terrain LoadADT(GL gl, Structs.MapTile mapTile, uint shaderProgram)
        {
            ADT adt = new();
            Terrain result = new();
            ADTReader adtReader = new();

            var wdt = Cache.GetOrLoadWDT(mapTile.wdtFileDataID);

            var rootADTFileDataID = adtReader.LoadADT(wdt, mapTile.tileX, mapTile.tileY, true, "");
            adt = adtReader.adtfile;

            var TileSize = 1600.0f / 3.0f; //533.333
            var ChunkSize = TileSize / 16.0f; //33.333
            var UnitSize = ChunkSize / 8.0f; //4.166666
            var MapMidPoint = 32.0f / ChunkSize;

            HashSet<uint> usedBLPFileDataIDs = [];

            result.vao = gl.GenVertexArray();
            gl.BindVertexArray(result.vao);

            result.vertexBuffer = gl.GenBuffer();
            result.indiceBuffer = gl.GenBuffer();

            var materials = new Dictionary<uint, ADTMaterial>();

            if (adt.textures.filenames == null)
            {
                for (var ti = 0; ti < adt.diffuseTextureFileDataIDs.Length; ti++)
                {
                    var material = new ADTMaterial
                    {
                        textureID = Cache.GetOrLoadBLP(gl, adt.diffuseTextureFileDataIDs[ti], mapTile.wdtFileDataID)
                    };

                    usedBLPFileDataIDs.Add(material.textureID);

                    if (adt.texParams != null && adt.texParams.Length >= ti)
                    {
                        material.scale = (float)Math.Pow(2, (adt.texParams[ti].flags & 0xF0) >> 4);
                        if (adt.texParams[ti].height != 0.0 || adt.texParams[ti].offset != 1.0)
                        {
                            material.heightScale = adt.texParams[ti].height;
                            material.heightOffset = adt.texParams[ti].offset;

                            if (!FileProvider.FileExists(adt.heightTextureFileDataIDs[ti]))
                            {
                                material.heightTextureID = Cache.GetOrLoadBLP(gl, adt.diffuseTextureFileDataIDs[ti], rootADTFileDataID);
                                usedBLPFileDataIDs.Add(adt.diffuseTextureFileDataIDs[ti]);
                            }
                            else
                            {
                                material.heightTextureID = Cache.GetOrLoadBLP(gl, adt.heightTextureFileDataIDs[ti], rootADTFileDataID);
                                usedBLPFileDataIDs.Add(adt.heightTextureFileDataIDs[ti]);
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
                    materials.Add(adt.diffuseTextureFileDataIDs[ti], material);
                }
            }
            else
            {
                throw new Exception("Filename-based loading yeeted");
            }

            result.blpFileDataIDs = [.. usedBLPFileDataIDs];

            var initialChunkY = adt.chunks[0].header.position.Y;
            var initialChunkX = adt.chunks[0].header.position.X;

            var renderBatches = new List<ADTRenderBatch>(256);

            var normalAttrib = gl.GetAttribLocation(shaderProgram, "normal");
            var colorAttrib = gl.GetAttribLocation(shaderProgram, "color");
            var texCoordAttrib = gl.GetAttribLocation(shaderProgram, "texCoord");
            var posAttrib = gl.GetAttribLocation(shaderProgram, "position");

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

                int off = c * 145;
                for (var j = 9; j < 145; j++)
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

                    if ((j + 1) % (9 + 8) == 0) j += 9;
                }

                var layerMaterials = new int[8];
                Array.Fill(layerMaterials, -1);

                var layerHeights = new int[8];
                Array.Fill(layerHeights, -1);

                var layerScales = new float[8];
                Array.Fill(layerScales, 1.0f);
                var heightScales = new float[8];

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
                    usedBLPFileDataIDs.Add(diffuseTextureID);

                    layerMaterials[li] = (int)Cache.GetOrLoadBLP(gl, diffuseTextureID, rootADTFileDataID);
                    layerHeights[li] = (int)curMat.heightTextureID;
                    layerScales[li] = curMat.scale;
                    heightScales[li] = curMat.heightScale;
                    heightOffsets[li] = curMat.heightOffset;
                }

                var alphaLayerMats = new int[2];
                Array.Fill(alphaLayerMats, -1);

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

                    alphaLayerMats[li] = (int)BLPLoader.GenerateAlphaTexture(gl, alphaData);
                }

                batch.heightScales = heightScales;
                batch.heightOffsets = heightOffsets;
                batch.materialID = layerMaterials;
                batch.alphaMaterialID = alphaLayerMats;
                batch.scales = layerScales;
                batch.heightMaterialIDs = layerHeights;
                renderBatches.Add(batch);

                chunkBounds[c] = new BoundingBox
                {
                    min = chunkMinBounds,
                    max = chunkMaxBounds
                };
            }

            gl.BindBuffer(BufferTargetARB.ArrayBuffer, result.vertexBuffer);
            fixed (ADTVertex* buf = vertices)
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)vertices.Length * 12 * sizeof(float), buf, GLEnum.StaticDraw);

            gl.EnableVertexAttribArray((uint)normalAttrib);
            gl.VertexAttribPointer((uint)normalAttrib, 3, GLEnum.Float, false, sizeof(float) * 12, (void*)(sizeof(float) * 0));

            gl.EnableVertexAttribArray((uint)colorAttrib);
            gl.VertexAttribPointer((uint)colorAttrib, 4, GLEnum.Float, false, sizeof(float) * 12, (void*)(sizeof(float) * 3));

            gl.EnableVertexAttribArray((uint)texCoordAttrib);
            gl.VertexAttribPointer((uint)texCoordAttrib, 2, GLEnum.Float, false, sizeof(float) * 12, (void*)(sizeof(float) * 7));

            gl.EnableVertexAttribArray((uint)posAttrib);
            gl.VertexAttribPointer((uint)posAttrib, 3, GLEnum.Float, false, sizeof(float) * 12, (void*)(sizeof(float) * 9));

            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, result.indiceBuffer);
            fixed (int* buf = indices)
                gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(int)), buf, GLEnum.StaticDraw);

            var doodads = new List<Doodad>(adt.objects.models.entries.Length);
            var worldModelBatches = new List<WorldModelBatch>(adt.objects.worldModels.entries.Length);

            for (var mi = 0; mi < adt.objects.models.entries.Length; mi++)
            {
                var modelentry = adt.objects.models.entries[mi];

                doodads.Add(new Doodad
                {
                    position = new Vector3(-(modelentry.position.X - 17066), modelentry.position.Y, -(modelentry.position.Z - 17066)),
                    rotation = new Vector3(modelentry.rotation.X, modelentry.rotation.Y, modelentry.rotation.Z),
                    scale = modelentry.scale / 1024.0f,
                    fileDataID = modelentry.mmidEntry
                });
            }

            for (var wmi = 0; wmi < adt.objects.worldModels.entries.Length; wmi++)
            {
                var wmodelentry = adt.objects.worldModels.entries[wmi];
                var wmoFDID = wmodelentry.mwidEntry;

                worldModelBatches.Add(new WorldModelBatch
                {
                    position = new Vector3(-(wmodelentry.position.X - 17066.666f), wmodelentry.position.Y, -(wmodelentry.position.Z - 17066.666f)),
                    rotation = new Vector3(wmodelentry.rotation.X, wmodelentry.rotation.Y, wmodelentry.rotation.Z),
                    fileDataID = wmoFDID,
                    uniqueID = wmodelentry.uniqueId,
                    scale = wmodelentry.scale / 1024.0f
                });
            }

            result.renderBatches = [.. renderBatches];
            result.doodads = [.. doodads];
            result.worldModelBatches = [.. worldModelBatches];
            result.rootADTFileDataID = rootADTFileDataID;
            result.chunkBounds = chunkBounds;

            return result;
        }

        public static void UnloadTerrain(Terrain terrain, GL gl)
        {
            gl.DeleteVertexArray(terrain.vao);
            gl.DeleteBuffer(terrain.vertexBuffer);
            gl.DeleteBuffer(terrain.indiceBuffer);

            foreach (var usedWMO in terrain.worldModelBatches)
                Cache.ReleaseWMO(gl, usedWMO.fileDataID, terrain.rootADTFileDataID);

            foreach (var usedM2 in terrain.doodads)
                Cache.ReleaseM2(gl, usedM2.fileDataID, terrain.rootADTFileDataID);

            foreach (var usedBLP in terrain.blpFileDataIDs)
                Cache.ReleaseBLP(gl, usedBLP, terrain.rootADTFileDataID);

            foreach (var batch in terrain.renderBatches)
                foreach (var alphaMatID in batch.alphaMaterialID)
                    if (alphaMatID != -1)
                        gl.DeleteTexture((uint)alphaMatID);
        }
    }
}
