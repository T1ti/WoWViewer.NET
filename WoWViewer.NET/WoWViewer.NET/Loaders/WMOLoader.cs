using Silk.NET.OpenGL;
using System.Numerics;
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
                CASCLib.Logger.WriteLine("Could not get filedataid for " + fileName);

            if (!WoWFormatLib.Utils.CASC.FileExists(fileDataID))
                throw new Exception("WMO " + fileName + " does not exist!");

            return LoadWMO(gl, fileDataID, shaderProgram, fileName);
        }

        public static unsafe WorldModel LoadWMO(GL gl, uint fileDataID, uint shaderProgram, string fileName = "")
        {
            Console.WriteLine("Loading WMO " + fileDataID);
            WMO wmo = new WMOReader().LoadWMO(fileDataID, 0, fileName);

            if (wmo.group.Count() == 0)
            {
                CASCLib.Logger.WriteLine("WMO has no groups: ", fileName);
                throw new Exception("Broken WMO! Report to developer (mail marlamin@marlamin.com) with this filename: " + fileName);
            }

            var wmoBatch = new Renderer.Structs.WorldModel()
            {
                groupBatches = new Renderer.Structs.WorldModelGroupBatches[wmo.group.Count()]
            };

            for (var g = 0; g < wmo.group.Count(); g++)
            {
                string groupName = null;
                for (var i = 0; i < wmo.groupNames.Count(); i++)
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

                gl.BindVertexArray(wmoBatch.groupBatches[g].vao);
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, wmoBatch.groupBatches[g].vertexBuffer);

                var wmovertices = new WMOVertex[wmo.group[g].mogp.vertices.Count()];

                for (var i = 0; i < wmo.group[g].mogp.vertices.Count(); i++)
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
                }

                //Push to buffer
                fixed (WMOVertex* buf = wmovertices)
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(wmovertices.Length * 14 * sizeof(float)), buf, BufferUsageARB.StaticDraw);

                //Set pointers in buffer
                //var normalAttrib = GL.GetAttribLocation(shaderProgram, "normal");
                //GL.EnableVertexAttribArray(normalAttrib);
                //GL.VertexAttribPointer(normalAttrib, 3, VertexAttribPointerType.Float, false, sizeof(float) * 14, sizeof(float) * 0);
                
                var texCoordAttrib = gl.GetAttribLocation(shaderProgram, "texCoord");
                gl.EnableVertexAttribArray((uint)texCoordAttrib);
                gl.VertexAttribPointer((uint)texCoordAttrib, 2, VertexAttribPointerType.Float, false, sizeof(float) * 14, (void*)(sizeof(float) * 3));

                //var texCoord2Attrib = gl.GetAttribLocation(shaderProgram, "texCoord2");
                //gl.EnableVertexAttribArray((uint)texCoord2Attrib);
                //gl.VertexAttribPointer((uint)texCoord2Attrib, 2, VertexAttribPointerType.Float, false, sizeof(float) * 14, (void*)(sizeof(float) * 5));

                //var texCoord3Attrib = gl.GetAttribLocation(shaderProgram, "texCoord3");
                //gl.EnableVertexAttribArray((uint)texCoord3Attrib);
                //gl.VertexAttribPointer((uint)texCoord3Attrib, 2, VertexAttribPointerType.Float, false, sizeof(float) * 14, (void*)(sizeof(float) * 7));

                //var texCoord4Attrib = gl.GetAttribLocation(shaderProgram, "texCoord4");
                //gl.EnableVertexAttribArray((uint)texCoord4Attrib);
                //gl.VertexAttribPointer((uint)texCoord4Attrib, 2, VertexAttribPointerType.Float, false, sizeof(float) * 14, (void*)(sizeof(float) * 9));

                var posAttrib = gl.GetAttribLocation(shaderProgram, "position");
                gl.EnableVertexAttribArray((uint)posAttrib);
                gl.VertexAttribPointer((uint)posAttrib, 3, VertexAttribPointerType.Float, false, sizeof(float) * 14, (void*)(sizeof(float) * 11));

                //Switch to Index buffer
                gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, wmoBatch.groupBatches[g].indiceBuffer);

                var wmoindicelist = new List<uint>();
                for (var i = 0; i < wmo.group[g].mogp.indices.Count(); i++)
                    wmoindicelist.Add(wmo.group[g].mogp.indices[i].indice);

                wmoBatch.groupBatches[g].indices = wmoindicelist.ToArray();

                fixed (uint* buf = wmoBatch.groupBatches[g].indices)
                    gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(wmoBatch.groupBatches[g].indices.Length * sizeof(uint)), buf, BufferUsageARB.StaticDraw);

                if (fileDataID == 342280)
                {
                    Console.WriteLine("Created indice buffer " + wmoBatch.groupBatches[g].indiceBuffer + " with " + wmoBatch.groupBatches[g].indices.Length + " indices");
                }
            }

            wmoBatch.mats = new Renderer.Structs.Material[wmo.materials.Count()];
            for (var i = 0; i < wmo.materials.Count(); i++)
            {
                wmoBatch.mats[i].texture1 = wmo.materials[i].texture1;
                wmoBatch.mats[i].texture2 = wmo.materials[i].texture2;
                wmoBatch.mats[i].texture3 = wmo.materials[i].texture3;

                if (wmo.materials[i].shader == 23)
                {
                    wmoBatch.mats[i].texture4 = wmo.materials[i].color3;
                    wmoBatch.mats[i].texture5 = wmo.materials[i].runtimeData0;
                    wmoBatch.mats[i].texture6 = wmo.materials[i].runtimeData1;
                    wmoBatch.mats[i].texture7 = wmo.materials[i].runtimeData2;
                    wmoBatch.mats[i].texture8 = wmo.materials[i].runtimeData3;
                }
    
                if (WoWFormatLib.Utils.CASC.FileExists(wmo.materials[i].texture1))
                    wmoBatch.mats[i].textureID1 = Cache.GetOrLoadBLP(gl, wmo.materials[i].texture1);

                if (WoWFormatLib.Utils.CASC.FileExists(wmo.materials[i].texture2))
                    wmoBatch.mats[i].textureID2 = Cache.GetOrLoadBLP(gl, wmo.materials[i].texture2);

                if (WoWFormatLib.Utils.CASC.FileExists(wmo.materials[i].texture3))
                    wmoBatch.mats[i].textureID3 = Cache.GetOrLoadBLP(gl, wmo.materials[i].texture3);

                if (wmo.materials[i].shader == 23)
                {
                    if (WoWFormatLib.Utils.CASC.FileExists(wmo.materials[i].color3))
                        wmoBatch.mats[i].textureID4 = Cache.GetOrLoadBLP(gl, wmo.materials[i].color3);

                    if (WoWFormatLib.Utils.CASC.FileExists(wmo.materials[i].runtimeData0))
                        wmoBatch.mats[i].textureID5 = Cache.GetOrLoadBLP(gl, wmo.materials[i].runtimeData0);

                    if (WoWFormatLib.Utils.CASC.FileExists(wmo.materials[i].runtimeData1))
                        wmoBatch.mats[i].textureID6 = Cache.GetOrLoadBLP(gl, wmo.materials[i].runtimeData1);

                    if (WoWFormatLib.Utils.CASC.FileExists(wmo.materials[i].runtimeData2))
                        wmoBatch.mats[i].textureID7 = Cache.GetOrLoadBLP(gl, wmo.materials[i].runtimeData2);

                    if (WoWFormatLib.Utils.CASC.FileExists(wmo.materials[i].runtimeData3))
                        wmoBatch.mats[i].textureID8 = Cache.GetOrLoadBLP(gl, wmo.materials[i].runtimeData3);
                }
            }

            // Store all of the doodad set names for the WMO.
            wmoBatch.doodadSets = new string[wmo.doodadSets.Length];
            for (uint i = 0; i < wmo.doodadSets.Length; i++)
                wmoBatch.doodadSets[i] = wmo.doodadSets[i].setName;

            wmoBatch.doodads = new Renderer.Structs.WMODoodad[wmo.doodadDefinitions.Count()];
            for (var i = 0; i < wmo.doodadDefinitions.Count(); i++)
            {
                if (wmo.doodadNames != null)
                {
                    for (var j = 0; j < wmo.doodadNames.Count(); j++)
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

            var numRenderbatches = 0;
            //Get total amount of render batches
            for (var i = 0; i < wmo.group.Count(); i++)
            {
                if (wmo.group[i].mogp.renderBatches == null) { continue; }
                numRenderbatches = numRenderbatches + wmo.group[i].mogp.renderBatches.Count();
            }

            wmoBatch.wmoRenderBatch = new Renderer.Structs.RenderBatch[numRenderbatches];

            var rb = 0;
            for (var g = 0; g < wmo.group.Count(); g++)
            {
                var group = wmo.group[g];
                if (group.mogp.renderBatches == null) { continue; }
                for (var i = 0; i < group.mogp.renderBatches.Count(); i++)
                {
                    wmoBatch.wmoRenderBatch[rb].firstFace = group.mogp.renderBatches[i].firstFace;
                    wmoBatch.wmoRenderBatch[rb].numFaces = group.mogp.renderBatches[i].numFaces;
                    uint matID = 0;

                    if (group.mogp.renderBatches[i].flags == 2)
                    {
                        matID = (uint)group.mogp.renderBatches[i].possibleBox2_3;
                    }
                    else
                    {
                        matID = group.mogp.renderBatches[i].materialID;
                    }

                    wmoBatch.wmoRenderBatch[rb].shader = wmo.materials[matID].shader;

                    wmoBatch.wmoRenderBatch[rb].materialID = new uint[3];
                    for (var ti = 0; ti < wmoBatch.mats.Count(); ti++)
                    {
                        if (wmo.materials[matID].texture1 == wmoBatch.mats[ti].texture1)
                            wmoBatch.wmoRenderBatch[rb].materialID[0] = (uint)wmoBatch.mats[ti].textureID1;

                        if (wmo.materials[matID].texture2 == wmoBatch.mats[ti].texture2)
                            wmoBatch.wmoRenderBatch[rb].materialID[1] = (uint)wmoBatch.mats[ti].textureID2;

                        if (wmo.materials[matID].texture3 == wmoBatch.mats[ti].texture3)
                            wmoBatch.wmoRenderBatch[rb].materialID[2] = (uint)wmoBatch.mats[ti].textureID3;
                    }

                    wmoBatch.wmoRenderBatch[rb].blendType = wmo.materials[matID].blendMode;
                    wmoBatch.wmoRenderBatch[rb].groupID = (uint)g;
                    rb++;
                }
            }

            return wmoBatch;
        }
    }
}
