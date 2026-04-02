using Silk.NET.OpenGL;
using System.Numerics;
using WoWFormatLib.FileProviders;
using WoWFormatLib.FileReaders;
using WoWFormatLib.Structs.ADT;
using WoWViewer.NET.Renderer;
using static WoWViewer.NET.Renderer.Structs;

namespace WoWViewer.NET.Loaders
{
    class ADTLoader
    {
        public static unsafe Terrain LoadADT(GL gl, Structs.MapTile mapTile, uint shaderProgram, bool loadModels = false)
        {
            ADT adt = new ADT();
            Terrain result = new Terrain();
            ADTReader adtReader = new ADTReader();
            var wdtReader = new WDTReader();
            wdtReader.LoadWDT(mapTile.wdtFileDataID);

            Listfile.FDIDToFilename.TryGetValue(mapTile.wdtFileDataID, out string wdtFilename);

            adtReader.LoadADT(wdtReader.wdtfile, mapTile.tileX, mapTile.tileY, true, wdtFilename);
            adt = adtReader.adtfile;

            var TileSize = 1600.0f / 3.0f; //533.333
            var ChunkSize = TileSize / 16.0f; //33.333
            var UnitSize = ChunkSize / 8.0f; //4.166666
            var MapMidPoint = 32.0f / ChunkSize;

            var verticelist = new List<Vertex>();
            var indicelist = new List<int>();
            result.vao = gl.GenVertexArray();
            gl.BindVertexArray(result.vao);

            result.vertexBuffer = gl.GenBuffer();
            result.indiceBuffer = gl.GenBuffer();

            var materials = new List<Material>();

            if (adt.textures.filenames == null)
            {
                for (var ti = 0; ti < adt.diffuseTextureFileDataIDs.Count(); ti++)
                {
                    var material = new Material();
                    material.filename = adt.diffuseTextureFileDataIDs[ti].ToString();
                    material.textureID = Cache.GetOrLoadBLP(gl, adt.diffuseTextureFileDataIDs[ti]);

                    if (adt.texParams != null && adt.texParams.Count() >= ti)
                    {
                        material.scale = (float)Math.Pow(2, (adt.texParams[ti].flags & 0xF0) >> 4);
                        if (adt.texParams[ti].height != 0.0 || adt.texParams[ti].offset != 1.0)
                        {
                            material.heightScale = adt.texParams[ti].height;
                            material.heightOffset = adt.texParams[ti].offset;

                            if (!FileProvider.FileExists(adt.heightTextureFileDataIDs[ti]))
                            {
                                Console.WriteLine("Height texture: " + adt.heightTextureFileDataIDs[ti] + " does not exist! Falling back to original texture (hack)..");
                                material.heightTexture = Cache.GetOrLoadBLP(gl, adt.diffuseTextureFileDataIDs[ti]);
                            }
                            else
                            {
                                material.heightTexture = Cache.GetOrLoadBLP(gl, adt.heightTextureFileDataIDs[ti]);
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
                    materials.Add(material);
                }
            }
            else
            {
                for (var ti = 0; ti < adt.textures.filenames.Count(); ti++)
                {
                    var material = new Material();
                    material.filename = adt.textures.filenames[ti];
                    material.textureID = BLPLoader.LoadTexture(gl, adt.textures.filenames[ti]);

                    if (adt.texParams != null && adt.texParams.Count() >= ti)
                    {
                        material.scale = (float)Math.Pow(2, (adt.texParams[ti].flags & 0xF0) >> 4);
                        if (adt.texParams[ti].height != 0.0 || adt.texParams[ti].offset != 1.0)
                        {
                            material.heightScale = adt.texParams[ti].height;
                            material.heightOffset = adt.texParams[ti].offset;

                            var heightName = adt.textures.filenames[ti].Replace(".blp", "_h.blp");
                            if (!FileProvider.FileExists(heightName))
                            {
                                Console.WriteLine("Height texture: " + heightName + " does not exist! Falling back to original texture (hack)..");
                                material.heightTexture = BLPLoader.LoadTexture(gl, adt.textures.filenames[ti]);
                            }
                            else
                            {
                                material.heightTexture = BLPLoader.LoadTexture(gl, heightName);
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
                    materials.Add(material);
                }
            }


            var initialChunkY = adt.chunks[0].header.position.Y;
            var initialChunkX = adt.chunks[0].header.position.X;

            var renderBatches = new List<RenderBatch>();

            for (uint c = 0; c < adt.chunks.Count(); c++)
            {
                var chunk = adt.chunks[c];

                var off = verticelist.Count();

                var batch = new RenderBatch();

                batch.groupID = c;

                for (int i = 0, idx = 0; i < 17; i++)
                {
                    for (var j = 0; j < (((i % 2) != 0) ? 8 : 9); j++)
                    {
                        var v = new Vertex();
                        v.Normal = new Vector3(chunk.normals.normal_0[idx], chunk.normals.normal_1[idx], chunk.normals.normal_2[idx]);
                        if (chunk.vertexShading.red != null)
                            v.Color = new Vector4(chunk.vertexShading.blue[idx] / 255.0f, chunk.vertexShading.green[idx] / 255.0f, chunk.vertexShading.red[idx] / 255.0f, chunk.vertexShading.alpha[idx] / 255.0f);
                        else
                            v.Color = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);

                        v.TexCoord = new Vector2((j + (((i % 2) != 0) ? 0.5f : 0f)) / 8f, (i * 0.5f) / 8f);
                        v.Position = new Vector3(chunk.header.position.X - (i * UnitSize * 0.5f), chunk.header.position.Y - (j * UnitSize), chunk.vertices.vertices[idx++] + chunk.header.position.Z);

                        if ((i % 2) != 0)
                            v.Position.Y -= 0.5f * UnitSize;

                        verticelist.Add(v);
                    }
                }

                result.startPos = verticelist[0];

                batch.firstFace = (uint)indicelist.Count();
                for (var j = 9; j < 145; j++)
                {
                    indicelist.AddRange(new int[] { off + j + 8, off + j - 9, off + j });
                    indicelist.AddRange(new int[] { off + j - 9, off + j - 8, off + j });
                    indicelist.AddRange(new int[] { off + j - 8, off + j + 9, off + j });
                    indicelist.AddRange(new int[] { off + j + 9, off + j + 8, off + j });
                    if ((j + 1) % (9 + 8) == 0) j += 9;
                }
                batch.numFaces = (uint)(indicelist.Count()) - batch.firstFace;

                var layerMaterials = new List<int>();
                var alphalayermats = new List<int>();
                var layerscales = new List<float>();
                var layerheights = new List<int>();

                batch.heightScales = new Vector4();
                batch.heightOffsets = new Vector4();
                for (var li = 0; li < adt.chunks[c].layers.Count(); li++)
                {
                    if (adt.chunks[c].alphaLayer != null)
                        alphalayermats.Add((int)BLPLoader.GenerateAlphaTexture(gl, adt.chunks[c].alphaLayer[li].layer));

                    Material curMat;

                    if (adt.diffuseTextureFileDataIDs == null)
                    {
                        if (adt.textures.filenames == null)
                            throw new Exception("ADT has no textures?");

                        throw new NotImplementedException("Old style filename based texture loading not implemented");

                        //var texFileDataID = WoWFormatLib.Utils.CASC.getFileDataIdByName(adt.textures.filenames[adt.chunks[c].layers[li].textureId]);
                        // layerMaterials.Add(Cache.GetOrLoadBLP(gl, texFileDataID));
                        //curMat = materials.Where(material => material.filename == adt.textures.filenames[adt.chunks[c].layers[li].textureId]).Single();
                    }
                    else
                    {
                        layerMaterials.Add((int)Cache.GetOrLoadBLP(gl, adt.diffuseTextureFileDataIDs[adt.chunks[c].layers[li].textureId]));
                        curMat = materials.Where(material => material.filename == adt.diffuseTextureFileDataIDs[adt.chunks[c].layers[li].textureId].ToString()).Single();
                    }

                    layerscales.Add(curMat.scale);
                    layerheights.Add((int)curMat.heightTexture);

                    batch.heightScales[li] = curMat.heightScale;
                    batch.heightOffsets[li] = curMat.heightOffset;
                }

                batch.materialID = layerMaterials.ToArray();
                batch.alphaMaterialID = alphalayermats.ToArray();
                batch.scales = layerscales.ToArray();
                batch.heightMaterialIDs = layerheights.ToArray();

                var indices = indicelist.ToArray();
                var vertices = verticelist.ToArray();

                gl.BindBuffer(BufferTargetARB.ArrayBuffer, result.vertexBuffer);
                fixed (Vertex* buf = vertices)
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)vertices.Length * 12 * sizeof(float), buf, GLEnum.StaticDraw);

                //var normalAttrib = GL.GetAttribLocation(shaderProgram, "normal");
                //GL.EnableVertexAttribArray(normalAttrib);
                //GL.VertexAttribPointer(normalAttrib, 3, VertexAttribPointerType.Float, false, sizeof(float) * 11, sizeof(float) * 0);

                var colorAttrib = gl.GetAttribLocation(shaderProgram, "color");
                gl.EnableVertexAttribArray((uint)colorAttrib);
                gl.VertexAttribPointer((uint)colorAttrib, 4, GLEnum.Float, false, sizeof(float) * 12, (void*)(sizeof(float) * 3));

                var texCoordAttrib = gl.GetAttribLocation(shaderProgram, "texCoord");
                gl.EnableVertexAttribArray((uint)texCoordAttrib);
                gl.VertexAttribPointer((uint)texCoordAttrib, 2, GLEnum.Float, false, sizeof(float) * 12, (void*)(sizeof(float) * 7));

                var posAttrib = gl.GetAttribLocation(shaderProgram, "position");
                gl.EnableVertexAttribArray((uint)posAttrib);
                gl.VertexAttribPointer((uint)posAttrib, 3, GLEnum.Float, false, sizeof(float) * 12, (void*)(sizeof(float) * 9));

                gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, result.indiceBuffer);
                fixed (int* buf = indices)
                    gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(int)), buf, GLEnum.StaticDraw);

                renderBatches.Add(batch);
            }

            var doodads = new List<Doodad>();
            var worldModelBatches = new List<WorldModelBatch>();

            if (loadModels)
            {
                for (var mi = 0; mi < adt.objects.models.entries.Count(); mi++)
                {
                    var modelentry = adt.objects.models.entries[mi];
                    //var mmid = adt.objects.m2NameOffsets.offsets[modelentry.mmidEntry];

                    //var modelFileName = "";
                    //for (var mmi = 0; mmi < adt.objects.m2Names.offsets.Count(); mmi++)
                    //{
                    //    if (adt.objects.m2Names.offsets[mmi] == mmid)
                    //    {
                    //        modelFileName = adt.objects.m2Names.filenames[mmi].ToLower();
                    //        break;
                    //    }
                    //}

                    doodads.Add(new Doodad
                    {
                        position = new Vector3(-(modelentry.position.X - 17066), modelentry.position.Y, -(modelentry.position.Z - 17066)),
                        rotation = new Vector3(modelentry.rotation.X, modelentry.rotation.Y, modelentry.rotation.Z),
                        scale = modelentry.scale / 1024.0f,
                        fileDataID = modelentry.mmidEntry
                    });
                }

                for (var wmi = 0; wmi < adt.objects.worldModels.entries.Count(); wmi++)
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
            }

            result.renderBatches = renderBatches.ToArray();
            result.doodads = doodads.ToArray();
            result.worldModelBatches = worldModelBatches.ToArray();

            return result;
        }
    }
}
