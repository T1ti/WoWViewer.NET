using Silk.NET.OpenGL;
using System.Numerics;
using WoWFormatLib.FileReaders;
using WoWFormatLib.Structs.WMO;
using WoWViewer.NET.Renderer;
using WoWViewer.NET.Services;
using WoWViewer.NET.Structs;

namespace WoWViewer.NET.Loaders
{
    public class WMOLoader
    {
        //public static unsafe WorldModel LoadWMO(GL gl, string fileName, uint shaderProgram)
        //{
        //    if (!Listfile.TryGetFileDataID(fileName, out uint fileDataID))
        //        Console.WriteLine("Could not get filedataid for " + fileName);

        //    if (!FileProvider.FileExists(fileDataID))
        //        throw new Exception("WMO " + fileName + " does not exist!");

        //    return LoadWMO(gl, fileDataID, shaderProgram, fileName);
        //}

        public static unsafe PreppedWMO ParseWMO(uint fileDataID, string fileName = "")
        {
            WMO wmo = new WMOReader().LoadWMO(fileDataID, 0, fileName);

            var groupBatches = new List<PreppedWMOGroup>();
            for (var g = 0; g < wmo.group.Length; g++)
            {
                var group = wmo.group[g];

                string groupName = "";
                for (var i = 0; i < wmo.groupNames.Length; i++)
                    if (group.mogp.nameOffset == wmo.groupNames[i].offset)
                        groupName = wmo.groupNames[i].name.Replace(" ", "_");

                if (groupName == "antiportal")
                    continue;

                if (group.mogp.vertices == null)
                    continue;

                var wmovertices = new WMOVertex[group.mogp.vertices.Length];

                for (var i = 0; i < group.mogp.vertices.Length; i++)
                {
                    wmovertices[i].Position = new Vector3(group.mogp.vertices[i].vector.X, group.mogp.vertices[i].vector.Y, group.mogp.vertices[i].vector.Z);
                    wmovertices[i].Normal = new Vector3(group.mogp.normals[i].normal.X, group.mogp.normals[i].normal.Y, group.mogp.normals[i].normal.Z);
                    if (group.mogp.textureCoords[0] == null)
                        wmovertices[i].TexCoord = new Vector2(0.0f, 0.0f);
                    else
                        wmovertices[i].TexCoord = new Vector2(group.mogp.textureCoords[0][i].X, group.mogp.textureCoords[0][i].Y);

                    if (group.mogp.textureCoords[1] == null)
                        wmovertices[i].TexCoord2 = new Vector2(0.0f, 0.0f);
                    else
                        wmovertices[i].TexCoord2 = new Vector2(group.mogp.textureCoords[1][i].X, group.mogp.textureCoords[1][i].Y);

                    if (group.mogp.textureCoords[2] == null)
                        wmovertices[i].TexCoord3 = new Vector2(0.0f, 0.0f);
                    else
                        wmovertices[i].TexCoord3 = new Vector2(group.mogp.textureCoords[2][i].X, group.mogp.textureCoords[2][i].Y);

                    if (group.mogp.textureCoords[3] == null)
                        wmovertices[i].TexCoord4 = new Vector2(0.0f, 0.0f);
                    else
                        wmovertices[i].TexCoord4 = new Vector2(group.mogp.textureCoords[3][i].X, group.mogp.textureCoords[3][i].Y);

                    if (group.mogp.colors != null)
                        wmovertices[i].Color = new Vector4(group.mogp.colors[i].X, group.mogp.colors[i].Y, group.mogp.colors[i].Z, group.mogp.colors[i].W);
                    else
                        wmovertices[i].Color = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);

                    if (group.mogp.colors2 != null)
                        wmovertices[i].Color2 = new Vector4(group.mogp.colors2[i].X, group.mogp.colors2[i].Y, group.mogp.colors2[i].Z, group.mogp.colors2[i].W);
                    else
                        wmovertices[i].Color2 = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);

                    if (group.mogp.colors3 != null)
                        wmovertices[i].Color3 = new Vector4(group.mogp.colors3[i].X, group.mogp.colors3[i].Y, group.mogp.colors3[i].Z, group.mogp.colors3[i].W);
                    else
                        wmovertices[i].Color3 = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
                }

                var vertexBytes = new byte[wmovertices.Length * sizeof(WMOVertex)];
                fixed (WMOVertex* src = wmovertices)
                fixed (byte* dst = vertexBytes)
                    System.Buffer.MemoryCopy(src, dst, vertexBytes.Length, vertexBytes.Length);

                var indiceBytes = new byte[group.mogp.indices.Length * sizeof(ushort)];
                fixed (ushort* src = group.mogp.indices)
                fixed (byte* dst = indiceBytes)
                    System.Buffer.MemoryCopy(src, dst, indiceBytes.Length, indiceBytes.Length);

                var renderBatches = new List<PreppedWMOGroupBatch>();

                if (group.mogp.renderBatches != null)
                {
                    for (var i = 0; i < group.mogp.renderBatches.Length; i++)
                    {
                        int matID = 0;

                        if ((group.mogp.renderBatches[i].flags & 2) == 2)
                            matID = group.mogp.renderBatches[i].possibleBox2_3;
                        else
                            matID = group.mogp.renderBatches[i].materialID;

                        var renderBatch = new PreppedWMOGroupBatch()
                        {
                            FirstFace = group.mogp.renderBatches[i].firstFace,
                            NumFaces = group.mogp.renderBatches[i].numFaces,
                            MaterialID = matID
                        };

                        renderBatches.Add(renderBatch);
                    }
                }

                groupBatches.Add(new PreppedWMOGroup()
                {
                    groupName = groupName,
                    boundingBox = new BoundingBox()
                    {
                        min = new Vector3(wmo.group[g].mogp.boundingBox1.X, wmo.group[g].mogp.boundingBox1.Y, wmo.group[g].mogp.boundingBox1.Z),
                        max = new Vector3(wmo.group[g].mogp.boundingBox2.X, wmo.group[g].mogp.boundingBox2.Y, wmo.group[g].mogp.boundingBox2.Z)
                    },
                    vertexBuffer = vertexBytes,
                    indiceBuffer = indiceBytes,
                    groupBatches = [.. renderBatches]
                });
            }

            var mats = new PreppedWMOMaterial[wmo.materials.Length];
            for (var i = 0; i < wmo.materials.Length; i++)
            {
                var texFileDataID0 = wmo.materials[i].texture1 == 0 ? -1 : (int)wmo.materials[i].texture1;
                var texFileDataID1 = wmo.materials[i].texture2 == 0 ? -1 : (int)wmo.materials[i].texture2;
                var texFileDataID2 = wmo.materials[i].texture3 == 0 ? -1 : (int)wmo.materials[i].texture3;
                var texFileDataID3 = -1;
                var texFileDataID4 = -1;
                var texFileDataID5 = -1;
                var texFileDataID6 = -1;
                var texFileDataID7 = -1;
                var texFileDataID8 = -1;

                var (vertexShader, pixelShader) = ShaderEnums.WMOShaders[(int)wmo.materials[i].shader];

                if (pixelShader == ShaderEnums.WMOPixelShader.MapObjLod)
                    continue;

                if (pixelShader == ShaderEnums.WMOPixelShader.MapObjParallax)
                {
                    if ((int)wmo.materials[i].color3 != 0)
                        texFileDataID3 = (int)wmo.materials[i].color3;

                    if ((int)wmo.materials[i].flags3 != 0)
                        texFileDataID4 = (int)wmo.materials[i].flags3;

                    if ((int)wmo.materials[i].runtimeData0 != 0)
                        texFileDataID5 = (int)wmo.materials[i].runtimeData0;
                }
                else if (pixelShader == ShaderEnums.WMOPixelShader.MapObjUnkShader)
                {
                    if ((int)wmo.materials[i].color3 != 0)
                        texFileDataID3 = (int)wmo.materials[i].color3;

                    if ((int)wmo.materials[i].flags3 != 0)
                        texFileDataID4 = (int)wmo.materials[i].flags3;

                    if ((int)wmo.materials[i].runtimeData0 != 0)
                        texFileDataID5 = (int)wmo.materials[i].runtimeData0;

                    if ((int)wmo.materials[i].runtimeData1 != 0)
                        texFileDataID6 = (int)wmo.materials[i].runtimeData1;

                    if ((int)wmo.materials[i].runtimeData2 != 0)
                        texFileDataID7 = (int)wmo.materials[i].runtimeData2;

                    if ((int)wmo.materials[i].runtimeData3 != 0)
                        texFileDataID8 = (int)wmo.materials[i].runtimeData3;
                }

                mats[i] = new PreppedWMOMaterial()
                {
                    Shader = (int)wmo.materials[i].shader,
                    VertexShader = vertexShader,
                    PixelShader = pixelShader,
                    BlendMode = wmo.materials[i].blendMode,
                    TexFileDataID0 = (uint)texFileDataID0,
                    TexFileDataID1 = (uint)texFileDataID1,
                    TexFileDataID2 = (uint)texFileDataID2,
                    TexFileDataID3 = (uint)texFileDataID3,
                    TexFileDataID4 = (uint)texFileDataID4,
                    TexFileDataID5 = (uint)texFileDataID5,
                    TexFileDataID6 = (uint)texFileDataID6,
                    TexFileDataID7 = (uint)texFileDataID7,
                    TexFileDataID8 = (uint)texFileDataID8
                };
            }

            var doodadSets = new string[wmo.doodadSets.Length];
            for (uint i = 0; i < wmo.doodadSets.Length; i++)
                doodadSets[i] = wmo.doodadSets[i].setName;

            var doodads = new WMODoodad[wmo.doodadDefinitions.Length];
            for (var i = 0; i < wmo.doodadDefinitions.Length; i++)
            {
                if (wmo.doodadNames != null)
                {
                    for (var j = 0; j < wmo.doodadNames.Length; j++)
                        if (wmo.doodadDefinitions[i].offset == wmo.doodadNames[j].startOffset)
                            doodads[i].filename = wmo.doodadNames[j].filename;
                }
                else
                {
                    doodads[i].filedataid = wmo.doodadDefinitions[i].offset;
                }

                doodads[i].flags = wmo.doodadDefinitions[i].flags;
                doodads[i].position = new Vector3(wmo.doodadDefinitions[i].position.X, wmo.doodadDefinitions[i].position.Y, wmo.doodadDefinitions[i].position.Z);
                doodads[i].rotation = new Quaternion(wmo.doodadDefinitions[i].rotation.X, wmo.doodadDefinitions[i].rotation.Y, wmo.doodadDefinitions[i].rotation.Z, wmo.doodadDefinitions[i].rotation.W);
                doodads[i].scale = wmo.doodadDefinitions[i].scale;
                doodads[i].color = new Vector4(wmo.doodadDefinitions[i].color[0], wmo.doodadDefinitions[i].color[1], wmo.doodadDefinitions[i].color[2], wmo.doodadDefinitions[i].color[3]);
                doodads[i].doodadSet = 0; // Default to 0.

                // Search all the doodadSets to see which one this doodad falls into.
                for (uint j = 0; j < wmo.doodadSets.Length; j++)
                {
                    var doodadSet = wmo.doodadSets[j];
                    if (i >= doodadSet.firstInstanceIndex && i < doodadSet.firstInstanceIndex + doodadSet.numDoodads)
                    {
                        doodads[i].doodadSet = j;
                        break; // Assumingly, a doodad cannot be in more than one doodadSet.
                    }
                }
            }


            return new PreppedWMO()
            {
                BoundingBox = new BoundingBox()
                {
                    min = new Vector3(wmo.header.boundingBox1.X, wmo.header.boundingBox1.Y, wmo.header.boundingBox1.Z),
                    max = new Vector3(wmo.header.boundingBox2.X, wmo.header.boundingBox2.Y, wmo.header.boundingBox2.Z)
                },
                FileDataID = fileDataID,
                Materials = [.. mats],
                Doodads = doodads,
                DoodadSets = doodadSets,
                PreppedWMOGroups = [.. groupBatches]
            };
        }

        public static unsafe WorldModel LoadWMO(PreppedWMO preppedWMO, GL gl, uint shaderProgram)
        {
            var wmoBatch = new WorldModel()
            {
                groupBatches = new WorldModelGroupBatches[preppedWMO.PreppedWMOGroups.Length],
                rootWMOFileDataID = preppedWMO.FileDataID,
                boundingBox = preppedWMO.BoundingBox,
                boundingRadius = CalculateBoundingRadius(preppedWMO.BoundingBox.min, preppedWMO.BoundingBox.max)
            };

            for (var g = 0; g < preppedWMO.PreppedWMOGroups.Length; g++)
            {
                var preppedGroup = preppedWMO.PreppedWMOGroups[g];

                wmoBatch.groupBatches[g] = new WorldModelGroupBatches()
                {
                    groupName = preppedGroup.groupName,
                    vao = gl.GenVertexArray(),
                    vertexBuffer = gl.GenBuffer(),
                    indiceBuffer = gl.GenBuffer(),
                    verticeCount = (uint)preppedGroup.vertexBuffer.Length / (uint)sizeof(WMOVertex)
                };

                gl.BindVertexArray(wmoBatch.groupBatches[g].vao);
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, wmoBatch.groupBatches[g].vertexBuffer);

                fixed (byte* buf = preppedGroup.vertexBuffer)
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)preppedGroup.vertexBuffer.Length, buf, BufferUsageARB.StaticDraw);

                //Set pointers in buffer
                var normalAttrib = gl.GetAttribLocation(shaderProgram, "normal");
                gl.EnableVertexAttribArray((uint)normalAttrib);
                gl.VertexAttribPointer((uint)normalAttrib, 3, VertexAttribPointerType.Float, false, sizeof(float) * 26, (void*)(sizeof(float) * 0));

                var texCoordAttrib = gl.GetAttribLocation(shaderProgram, "texCoord");
                gl.EnableVertexAttribArray((uint)texCoordAttrib);
                gl.VertexAttribPointer((uint)texCoordAttrib, 2, VertexAttribPointerType.Float, false, sizeof(float) * 26, (void*)(sizeof(float) * 3));
                var texCoord2Attrib = gl.GetAttribLocation(shaderProgram, "texCoord2");
                gl.EnableVertexAttribArray((uint)texCoord2Attrib);
                gl.VertexAttribPointer((uint)texCoord2Attrib, 2, VertexAttribPointerType.Float, false, sizeof(float) * 26, (void*)(sizeof(float) * 5));

                var texCoord3Attrib = gl.GetAttribLocation(shaderProgram, "texCoord3");
                gl.EnableVertexAttribArray((uint)texCoord3Attrib);
                gl.VertexAttribPointer((uint)texCoord3Attrib, 2, VertexAttribPointerType.Float, false, sizeof(float) * 26, (void*)(sizeof(float) * 7));

                var texCoord4Attrib = gl.GetAttribLocation(shaderProgram, "texCoord4");
                gl.EnableVertexAttribArray((uint)texCoord4Attrib);
                gl.VertexAttribPointer((uint)texCoord4Attrib, 2, VertexAttribPointerType.Float, false, sizeof(float) * 26, (void*)(sizeof(float) * 9));

                var posAttrib = gl.GetAttribLocation(shaderProgram, "position");
                gl.EnableVertexAttribArray((uint)posAttrib);
                gl.VertexAttribPointer((uint)posAttrib, 3, VertexAttribPointerType.Float, false, sizeof(float) * 26, (void*)(sizeof(float) * 11));

                var colorAttrib = gl.GetAttribLocation(shaderProgram, "color1");
                gl.EnableVertexAttribArray((uint)colorAttrib);
                gl.VertexAttribPointer((uint)colorAttrib, 4, VertexAttribPointerType.Float, false, sizeof(float) * 26, (void*)(sizeof(float) * 14));

                var color2Attrib = gl.GetAttribLocation(shaderProgram, "color2");
                gl.EnableVertexAttribArray((uint)color2Attrib);
                gl.VertexAttribPointer((uint)color2Attrib, 4, VertexAttribPointerType.Float, false, sizeof(float) * 26, (void*)(sizeof(float) * 18));

                var color3Attrib = gl.GetAttribLocation(shaderProgram, "color3");
                gl.EnableVertexAttribArray((uint)color3Attrib);
                gl.VertexAttribPointer((uint)color3Attrib, 4, VertexAttribPointerType.Float, false, sizeof(float) * 26, (void*)(sizeof(float) * 22));

                //Switch to Index buffer
                gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, wmoBatch.groupBatches[g].indiceBuffer);

                fixed (byte* buf = preppedGroup.indiceBuffer)
                    gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)preppedGroup.indiceBuffer.Length, buf, BufferUsageARB.StaticDraw);
            }

            var mats = new WMOMaterial[preppedWMO.Materials.Length];
            for (var i = 0; i < preppedWMO.Materials.Length; i++)
            {
                var preppedMat = preppedWMO.Materials[i];

                if (CASC.FileExists(preppedMat.TexFileDataID0))
                    mats[i].textureID1 = (int)Cache.GetOrLoadBLP(gl, preppedMat.TexFileDataID0, preppedWMO.FileDataID);

                if (CASC.FileExists(preppedMat.TexFileDataID1))
                    mats[i].textureID2 = (int)Cache.GetOrLoadBLP(gl, preppedMat.TexFileDataID1, preppedWMO.FileDataID);

                if (CASC.FileExists(preppedMat.TexFileDataID2))
                    mats[i].textureID3 = (int)Cache.GetOrLoadBLP(gl, preppedMat.TexFileDataID2, preppedWMO.FileDataID);

                if (preppedMat.PixelShader == ShaderEnums.WMOPixelShader.MapObjUnkShader)
                {
                    if (CASC.FileExists(preppedMat.TexFileDataID3))
                        mats[i].textureID4 = (int)Cache.GetOrLoadBLP(gl, preppedMat.TexFileDataID3, preppedWMO.FileDataID);

                    if (CASC.FileExists(preppedMat.TexFileDataID4))
                        mats[i].textureID5 = (int)Cache.GetOrLoadBLP(gl, preppedMat.TexFileDataID4, preppedWMO.FileDataID);

                    if (CASC.FileExists(preppedMat.TexFileDataID5))
                        mats[i].textureID6 = (int)Cache.GetOrLoadBLP(gl, preppedMat.TexFileDataID5, preppedWMO.FileDataID);

                    if (CASC.FileExists(preppedMat.TexFileDataID6))
                        mats[i].textureID7 = (int)Cache.GetOrLoadBLP(gl, preppedMat.TexFileDataID6, preppedWMO.FileDataID);

                    if (CASC.FileExists(preppedMat.TexFileDataID7))
                        mats[i].textureID8 = (int)Cache.GetOrLoadBLP(gl, preppedMat.TexFileDataID7, preppedWMO.FileDataID);

                    if (CASC.FileExists(preppedMat.TexFileDataID8))
                        mats[i].textureID9 = (int)Cache.GetOrLoadBLP(gl, preppedMat.TexFileDataID8, preppedWMO.FileDataID);
                }
            }

            wmoBatch.doodadSets = preppedWMO.DoodadSets;
            wmoBatch.doodads = preppedWMO.Doodads;

            var renderBatches = new List<WMORenderBatch>();

            for (var g = 0; g < preppedWMO.PreppedWMOGroups.Length; g++)
            {
                var group = preppedWMO.PreppedWMOGroups[g];
                if (group.groupBatches == null) { continue; }
                for (var i = 0; i < group.groupBatches.Length; i++)
                {
                    var groupBatch = group.groupBatches[i];

                    var renderBatch = new WMORenderBatch()
                    {
                        firstFace = groupBatch.FirstFace,
                        numFaces = (uint)groupBatch.NumFaces,
                        materialID = [-1, -1, -1, -1, -1, -1, -1, -1, -1],
                        blendType = preppedWMO.Materials[groupBatch.MaterialID].BlendMode,
                        groupID = (uint)g,
                        shader = (uint)preppedWMO.Materials[groupBatch.MaterialID].Shader
                    };

                    renderBatch.materialID[0] = mats[groupBatch.MaterialID].textureID1;
                    renderBatch.materialID[1] = mats[groupBatch.MaterialID].textureID2;
                    renderBatch.materialID[2] = mats[groupBatch.MaterialID].textureID3;
                    renderBatch.materialID[3] = mats[groupBatch.MaterialID].textureID4;
                    renderBatch.materialID[4] = mats[groupBatch.MaterialID].textureID5;
                    renderBatch.materialID[5] = mats[groupBatch.MaterialID].textureID6;
                    renderBatch.materialID[6] = mats[groupBatch.MaterialID].textureID7;
                    renderBatch.materialID[7] = mats[groupBatch.MaterialID].textureID8;
                    renderBatch.materialID[8] = mats[groupBatch.MaterialID].textureID9;

                    renderBatches.Add(renderBatch);
                }
            }

            wmoBatch.wmoRenderBatch = [.. renderBatches];
            return wmoBatch;
        }

        private static float CalculateBoundingRadius(Vector3 min, Vector3 max)
        {
            var center = (min + max) * 0.5f;
            return Vector3.Distance(center, max);
        }

        public static void UnloadWMO(GL gl, WorldModel wmo)
        {
            for (var g = 0; g < wmo.groupBatches.Length; g++)
            {
                gl.DeleteBuffer(wmo.groupBatches[g].vertexBuffer);
                gl.DeleteBuffer(wmo.groupBatches[g].indiceBuffer);
                gl.DeleteVertexArray(wmo.groupBatches[g].vao);
            }

            if (wmo.mats != null)
            {
                foreach (var mat in wmo.mats)
                {
                    if (mat.textureID1 != -1)
                        Cache.ReleaseBLP(gl, (uint)mat.textureID1, wmo.rootWMOFileDataID);
                    if (mat.textureID2 != -1)
                        Cache.ReleaseBLP(gl, (uint)mat.textureID2, wmo.rootWMOFileDataID);
                    if (mat.textureID3 != -1)
                        Cache.ReleaseBLP(gl, (uint)mat.textureID3, wmo.rootWMOFileDataID);
                    if (mat.textureID4 != -1)
                        Cache.ReleaseBLP(gl, (uint)mat.textureID4, wmo.rootWMOFileDataID);
                    if (mat.textureID5 != -1)
                        Cache.ReleaseBLP(gl, (uint)mat.textureID5, wmo.rootWMOFileDataID);
                    if (mat.textureID6 != -1)
                        Cache.ReleaseBLP(gl, (uint)mat.textureID6, wmo.rootWMOFileDataID);
                    if (mat.textureID7 != -1)
                        Cache.ReleaseBLP(gl, (uint)mat.textureID7, wmo.rootWMOFileDataID);
                    if (mat.textureID8 != -1)
                        Cache.ReleaseBLP(gl, (uint)mat.textureID8, wmo.rootWMOFileDataID);
                    if (mat.textureID9 != -1)
                        Cache.ReleaseBLP(gl, (uint)mat.textureID9, wmo.rootWMOFileDataID);
                }
            }

            if (wmo.doodads != null)
            {
                foreach (var model in wmo.doodads)
                    if (model.filename != null)
                        Cache.ReleaseM2(gl, model.filedataid, wmo.rootWMOFileDataID);
            }
        }
    }
}
