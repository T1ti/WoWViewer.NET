using Silk.NET.OpenGL;
using System.Numerics;
using WoWFormatLib.FileProviders;
using WoWFormatLib.FileReaders;
using WoWFormatLib.Structs.WMO;
using WoWViewer.NET.Renderer;
using static WoWViewer.NET.Renderer.Structs;

namespace WoWViewer.NET.Loaders
{
    public class WMOLoader
    {
        public static unsafe WorldModel LoadWMO(GL gl, string fileName, uint shaderProgram)
        {
            if (!Listfile.TryGetFileDataID(fileName, out uint fileDataID))
                Console.WriteLine("Could not get filedataid for " + fileName);

            if (!FileProvider.FileExists(fileDataID))
                throw new Exception("WMO " + fileName + " does not exist!");

            return LoadWMO(gl, fileDataID, shaderProgram, fileName);
        }

        public static unsafe WorldModel LoadWMO(GL gl, uint fileDataID, uint shaderProgram, string fileName = "")
        {
            Console.WriteLine("Loading WMO " + fileDataID);
            WMO wmo = new WMOReader().LoadWMO(fileDataID, 0, fileName);

            if (wmo.group.Length == 0)
            {
                Console.WriteLine("WMO has no groups: ", fileName);
                throw new Exception("Broken WMO! Report to developer (mail marlamin@marlamin.com) with this filename: " + fileName);
            }

            var wmoBatch = new Renderer.Structs.WorldModel()
            {
                groupBatches = new Renderer.Structs.WorldModelGroupBatches[wmo.group.Length]
            };

            for (var g = 0; g < wmo.group.Length; g++)
            {
                string groupName = null;
                for (var i = 0; i < wmo.groupNames.Length; i++)
                    if (wmo.group[g].mogp.nameOffset == wmo.groupNames[i].offset)
                        groupName = wmo.groupNames[i].name.Replace(" ", "_");

                if (groupName == "antiportal")
                {
                    Console.WriteLine("Skipping group " + groupName + " because antiportal");
                    continue;
                }

                if (wmo.group[g].mogp.vertices == null)
                {
                    Console.WriteLine("Skipping group " + groupName + " because it has no vertices");
                    continue;
                }

                wmoBatch.groupBatches[g].groupName = groupName;

                wmoBatch.groupBatches[g].vao = gl.GenVertexArray();
                wmoBatch.groupBatches[g].vertexBuffer = gl.GenBuffer();
                wmoBatch.groupBatches[g].indiceBuffer = gl.GenBuffer();
                wmoBatch.groupBatches[g].verticeCount = (uint)wmo.group[g].mogp.vertices.Length;

                gl.BindVertexArray(wmoBatch.groupBatches[g].vao);
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, wmoBatch.groupBatches[g].vertexBuffer);

                var wmovertices = new WMOVertex[wmo.group[g].mogp.vertices.Length];

                for (var i = 0; i < wmo.group[g].mogp.vertices.Length; i++)
                {
                    wmovertices[i].Position = new Vector3(wmo.group[g].mogp.vertices[i].vector.X, wmo.group[g].mogp.vertices[i].vector.Y, wmo.group[g].mogp.vertices[i].vector.Z);
                    wmovertices[i].Normal = new Vector3(wmo.group[g].mogp.normals[i].normal.X, wmo.group[g].mogp.normals[i].normal.Y, wmo.group[g].mogp.normals[i].normal.Z);
                    if (wmo.group[g].mogp.textureCoords[0] == null)
                        wmovertices[i].TexCoord = new Vector2(0.0f, 0.0f);
                    else
                        wmovertices[i].TexCoord = new Vector2(wmo.group[g].mogp.textureCoords[0][i].X, wmo.group[g].mogp.textureCoords[0][i].Y);

                    if (wmo.group[g].mogp.textureCoords[1] == null)
                        wmovertices[i].TexCoord2 = new Vector2(0.0f, 0.0f);
                    else
                        wmovertices[i].TexCoord2 = new Vector2(wmo.group[g].mogp.textureCoords[1][i].X, wmo.group[g].mogp.textureCoords[1][i].Y);

                    if (wmo.group[g].mogp.textureCoords[2] == null)
                        wmovertices[i].TexCoord3 = new Vector2(0.0f, 0.0f);
                    else
                        wmovertices[i].TexCoord3 = new Vector2(wmo.group[g].mogp.textureCoords[2][i].X, wmo.group[g].mogp.textureCoords[2][i].Y);

                    if (wmo.group[g].mogp.textureCoords[3] == null)
                        wmovertices[i].TexCoord4 = new Vector2(0.0f, 0.0f);
                    else
                        wmovertices[i].TexCoord4 = new Vector2(wmo.group[g].mogp.textureCoords[3][i].X, wmo.group[g].mogp.textureCoords[3][i].Y);

                    if (wmo.group[g].mogp.colors != null)
                    {
                        wmovertices[i].Color = new Vector4(wmo.group[g].mogp.colors[i].X, wmo.group[g].mogp.colors[i].Y, wmo.group[g].mogp.colors[i].Z, wmo.group[g].mogp.colors[i].W);
                    }
                    else
                    {
                        wmovertices[i].Color = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
                    }

                    if (wmo.group[g].mogp.colors2 != null)
                    {
                        wmovertices[i].Color2 = new Vector4(wmo.group[g].mogp.colors2[i].X, wmo.group[g].mogp.colors2[i].Y, wmo.group[g].mogp.colors2[i].Z, wmo.group[g].mogp.colors2[i].W);
                    }
                    else
                    {
                        wmovertices[i].Color2 = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
                    }

                    if (wmo.group[g].mogp.colors3 != null)
                    {
                        wmovertices[i].Color3 = new Vector4(wmo.group[g].mogp.colors3[i].X, wmo.group[g].mogp.colors3[i].Y, wmo.group[g].mogp.colors3[i].Z, wmo.group[g].mogp.colors3[i].W);
                    }
                    else
                    {
                        wmovertices[i].Color3 = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
                    }
                }

                //Push to buffer
                fixed (WMOVertex* buf = wmovertices)
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(wmovertices.Length * 26 * sizeof(float)), buf, BufferUsageARB.StaticDraw);

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

                var colorAttrib = gl.GetAttribLocation(shaderProgram, "color");
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

                fixed (ushort* buf = wmo.group[g].mogp.indices)
                    gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(wmo.group[g].mogp.indices.Length * sizeof(ushort)), buf, BufferUsageARB.StaticDraw);
            }

            wmoBatch.mats = new Renderer.Structs.Material[wmo.materials.Length];
            for (var i = 0; i < wmo.materials.Length; i++)
            {
                wmoBatch.mats[i].texture1 = (int)wmo.materials[i].texture1;
                wmoBatch.mats[i].texture2 = (int)wmo.materials[i].texture2;
                wmoBatch.mats[i].texture3 = (int)wmo.materials[i].texture3;
                wmoBatch.mats[i].texture4 = -1;
                wmoBatch.mats[i].texture5 = -1;
                wmoBatch.mats[i].texture6 = -1;
                wmoBatch.mats[i].texture7 = -1;
                wmoBatch.mats[i].texture8 = -1;
                wmoBatch.mats[i].texture9 = -1;

                wmoBatch.mats[i].textureID1 = -1;
                wmoBatch.mats[i].textureID2 = -1;
                wmoBatch.mats[i].textureID3 = -1;
                wmoBatch.mats[i].textureID4 = -1;
                wmoBatch.mats[i].textureID5 = -1;
                wmoBatch.mats[i].textureID6 = -1;
                wmoBatch.mats[i].textureID7 = -1;
                wmoBatch.mats[i].textureID8 = -1;
                wmoBatch.mats[i].textureID9 = -1;

                var (VertexShader, PixelShader) = ShaderEnums.WMOShaders[(int)wmo.materials[i].shader];
                if (PixelShader == ShaderEnums.WMOPixelShader.MapObjParallax)
                {
                    if((int)wmo.materials[i].color3 != 0)
                        wmoBatch.mats[i].texture4 = (int)wmo.materials[i].color3;

                    if((int)wmo.materials[i].flags3 != 0)
                        wmoBatch.mats[i].texture5 = (int)wmo.materials[i].flags3;

                    if((int)wmo.materials[i].runtimeData0 != 0)
                        wmoBatch.mats[i].texture6 = (int)wmo.materials[i].runtimeData0;
                }
                else if (PixelShader == ShaderEnums.WMOPixelShader.MapObjUnkShader)
                {
                    if ((int)wmo.materials[i].color3 != 0)
                        wmoBatch.mats[i].texture4 = (int)wmo.materials[i].color3;

                    if ((int)wmo.materials[i].flags3 != 0)
                        wmoBatch.mats[i].texture5 = (int)wmo.materials[i].flags3;

                    if ((int)wmo.materials[i].runtimeData0 != 0)
                        wmoBatch.mats[i].texture6 = (int)wmo.materials[i].runtimeData0;

                    if ((int)wmo.materials[i].runtimeData1 != 0)
                        wmoBatch.mats[i].texture7 = (int)wmo.materials[i].runtimeData1;

                    if ((int)wmo.materials[i].runtimeData2 != 0)
                        wmoBatch.mats[i].texture8 = (int)wmo.materials[i].runtimeData2;

                    if ((int)wmo.materials[i].runtimeData3 != 0)
                        wmoBatch.mats[i].texture9 = (int)wmo.materials[i].runtimeData3;
                }

                if (FileProvider.FileExists(wmo.materials[i].texture1))
                    wmoBatch.mats[i].textureID1 = (int)Cache.GetOrLoadBLP(gl, wmo.materials[i].texture1);

                if (FileProvider.FileExists(wmo.materials[i].texture2))
                    wmoBatch.mats[i].textureID2 = (int)Cache.GetOrLoadBLP(gl, wmo.materials[i].texture2);

                if (FileProvider.FileExists(wmo.materials[i].texture3))
                    wmoBatch.mats[i].textureID3 = (int)Cache.GetOrLoadBLP(gl, wmo.materials[i].texture3);

                if (PixelShader == ShaderEnums.WMOPixelShader.MapObjUnkShader)
                {
                    if (FileProvider.FileExists(wmo.materials[i].color3))
                        wmoBatch.mats[i].textureID4 = (int)Cache.GetOrLoadBLP(gl, wmo.materials[i].color3);

                    if (FileProvider.FileExists(wmo.materials[i].flags3))
                        wmoBatch.mats[i].textureID5 = (int)Cache.GetOrLoadBLP(gl, wmo.materials[i].flags3);

                    if (FileProvider.FileExists(wmo.materials[i].runtimeData0))
                        wmoBatch.mats[i].textureID6 = (int)Cache.GetOrLoadBLP(gl, wmo.materials[i].runtimeData0);

                    if (FileProvider.FileExists(wmo.materials[i].runtimeData1))
                        wmoBatch.mats[i].textureID7 = (int)Cache.GetOrLoadBLP(gl, wmo.materials[i].runtimeData1);

                    if (FileProvider.FileExists(wmo.materials[i].runtimeData2))
                        wmoBatch.mats[i].textureID8 = (int)Cache.GetOrLoadBLP(gl, wmo.materials[i].runtimeData2);

                    if (FileProvider.FileExists(wmo.materials[i].runtimeData3))
                        wmoBatch.mats[i].textureID9 = (int)Cache.GetOrLoadBLP(gl, wmo.materials[i].runtimeData3);
                }
            }

            // Store all of the doodad set names for the WMO.
            wmoBatch.doodadSets = new string[wmo.doodadSets.Length];
            for (uint i = 0; i < wmo.doodadSets.Length; i++)
                wmoBatch.doodadSets[i] = wmo.doodadSets[i].setName;

            wmoBatch.doodads = new WMODoodad[wmo.doodadDefinitions.Length];
            for (var i = 0; i < wmo.doodadDefinitions.Length; i++)
            {
                if (wmo.doodadNames != null)
                {
                    for (var j = 0; j < wmo.doodadNames.Length; j++)
                        if (wmo.doodadDefinitions[i].offset == wmo.doodadNames[j].startOffset)
                            wmoBatch.doodads[i].filename = wmo.doodadNames[j].filename;
                }
                else
                {
                    wmoBatch.doodads[i].filedataid = wmo.doodadDefinitions[i].offset;
                }

                wmoBatch.doodads[i].flags = wmo.doodadDefinitions[i].flags;
                wmoBatch.doodads[i].position = new Vector3(wmo.doodadDefinitions[i].position.X, wmo.doodadDefinitions[i].position.Y, wmo.doodadDefinitions[i].position.Z);
                wmoBatch.doodads[i].rotation = new Quaternion(wmo.doodadDefinitions[i].rotation.X, wmo.doodadDefinitions[i].rotation.Y, wmo.doodadDefinitions[i].rotation.Z, wmo.doodadDefinitions[i].rotation.W);
                wmoBatch.doodads[i].scale = wmo.doodadDefinitions[i].scale;
                wmoBatch.doodads[i].color = new Vector4(wmo.doodadDefinitions[i].color[0], wmo.doodadDefinitions[i].color[1], wmo.doodadDefinitions[i].color[2], wmo.doodadDefinitions[i].color[3]);
                wmoBatch.doodads[i].doodadSet = 0; // Default to 0.

                // Search all the doodadSets to see which one this doodad falls into.
                for (uint j = 0; j < wmo.doodadSets.Length; j++)
                {
                    var doodadSet = wmo.doodadSets[j];
                    if (i >= doodadSet.firstInstanceIndex && i < doodadSet.firstInstanceIndex + doodadSet.numDoodads)
                    {
                        wmoBatch.doodads[i].doodadSet = j;
                        break; // Assumingly, a doodad cannot be in more than one doodadSet.
                    }
                }
            }

            var renderBatches = new List<RenderBatch>();

            for (var g = 0; g < wmo.group.Length; g++)
            {
                var group = wmo.group[g];
                if (group.mogp.renderBatches == null) { continue; }
                for (var i = 0; i < group.mogp.renderBatches.Length; i++)
                {
                    var renderBatch = new RenderBatch()
                    {
                        firstFace = group.mogp.renderBatches[i].firstFace,
                        numFaces = group.mogp.renderBatches[i].numFaces,
                        shader = (uint)wmo.materials[group.mogp.renderBatches[i].materialID].shader,
                        materialID = [-1, -1, -1, -1, -1, -1, -1, -1, -1],
                        blendType = wmo.materials[group.mogp.renderBatches[i].materialID].blendMode,
                        groupID = (uint)g
                    };

                    int matID = 0;

                    if ((group.mogp.renderBatches[i].flags & 2) == 2)
                        matID = group.mogp.renderBatches[i].possibleBox2_3;
                    else
                        matID = group.mogp.renderBatches[i].materialID;

                    for (var ti = 0; ti < wmoBatch.mats.Length; ti++)
                    {
                        if (wmo.materials[matID].texture1 == wmoBatch.mats[ti].texture1)
                            renderBatch.materialID[0] = (int)wmoBatch.mats[ti].textureID1;

                        if (wmo.materials[matID].texture2 == wmoBatch.mats[ti].texture2)
                            renderBatch.materialID[1] = (int)wmoBatch.mats[ti].textureID2;

                        if (wmo.materials[matID].texture3 == wmoBatch.mats[ti].texture3)
                            renderBatch.materialID[2] = (int)wmoBatch.mats[ti].textureID3;

                        if (wmo.materials[matID].color3 == wmoBatch.mats[ti].texture4)
                            renderBatch.materialID[3] = (int)wmoBatch.mats[ti].textureID4;

                        if (wmo.materials[matID].flags3 == wmoBatch.mats[ti].texture5)
                            renderBatch.materialID[4] = (int)wmoBatch.mats[ti].textureID5;

                        if (wmo.materials[matID].runtimeData0 == wmoBatch.mats[ti].texture6)
                            renderBatch.materialID[5] = (int)wmoBatch.mats[ti].textureID6;

                        if (wmo.materials[matID].runtimeData1 == wmoBatch.mats[ti].texture7)
                            renderBatch.materialID[6] = (int)wmoBatch.mats[ti].textureID7;

                        if (wmo.materials[matID].runtimeData2 == wmoBatch.mats[ti].texture8)
                            renderBatch.materialID[7] = (int)wmoBatch.mats[ti].textureID8;

                        if (wmo.materials[matID].runtimeData3 == wmoBatch.mats[ti].texture9)
                            renderBatch.materialID[8] = (int)wmoBatch.mats[ti].textureID9;
                    }

                    renderBatch.blendType = wmo.materials[matID].blendMode;
                    renderBatch.groupID = (uint)g;

                    renderBatches.Add(renderBatch);
                }

                //var definingRenderBatch = false;
                //var currentRenderBatch = new RenderBatch()
                //{
                //    firstFace = 0,
                //    numFaces = 1,
                //    shader = 99,
                //    materialID = [1, 1, 1],
                //    blendType = 1,
                //    groupID = (uint)g
                //};

                //for (var i = 0; i < group.mogp.materialInfo.Length; i++)
                //{
                //    var materialInfo = group.mogp.materialInfo[i];
                //    if(materialInfo.materialID == 0xFF)
                //    {
                //        if (!definingRenderBatch)
                //        {
                //            currentRenderBatch.firstFace = (uint)i;
                //            currentRenderBatch.numFaces = 1;
                //            definingRenderBatch = true;
                //        }
                //        else
                //        {
                //            currentRenderBatch.numFaces++;
                //        }
                //    }
                //    else
                //    {
                //        if (definingRenderBatch)
                //        {
                //            definingRenderBatch = false;
                //            renderBatches.Add(currentRenderBatch);
                //            currentRenderBatch = new RenderBatch()
                //            {
                //                firstFace = 0,
                //                numFaces = 1,
                //                shader = 99,
                //                materialID = [1, 1, 1],
                //                blendType = 1,
                //                groupID = (uint)g
                //            };
                //        }
                //    }
                //}

                //if (definingRenderBatch)
                //    renderBatches.Add(currentRenderBatch);

                //for(var i = 0; i < group.mogp.bspNodes.Length; i++)
                //{
                //    var renderBatch = new RenderBatch()
                //    {
                //        firstFace = group.mogp.bspNodes[i].faceStart,
                //        numFaces = group.mogp.bspNodes[i].nFaces,
                //        shader = 99,
                //        materialID = [1, 1, 1],
                //        blendType = 1,
                //        groupID = (uint)g
                //    };

                //    renderBatches.Add(renderBatch);
                //}
            }

            wmoBatch.wmoRenderBatch = [.. renderBatches];
            return wmoBatch;
        }
    }
}
