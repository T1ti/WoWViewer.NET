using Silk.NET.OpenGL;
using System.Numerics;
using WoWFormatLib.FileProviders;
using WoWFormatLib.FileReaders;
using WoWFormatLib.Structs.M2;
using WoWViewer.NET.Renderer;
using static WoWViewer.NET.Renderer.Structs;

namespace WoWViewer.NET.Loaders
{
    class M2Loader
    {
        private static uint DEFAULT_TEXTURE_ID = 528732; // dungeons/textures/testing/color_01.blp
        private static uint MISSING_TEXTURE_ID = 186184; // textures/shanecube.blp

        public static DoodadBatch LoadM2(GL gl, string fileName, uint shaderProgram)
        {
            fileName = fileName.ToLower().Replace(".mdx", ".m2");
            fileName = fileName.ToLower().Replace(".mdl", ".m2");

            if (Listfile.TryGetFileDataID(fileName, out var fileDataID))
                return LoadM2(gl, fileDataID, shaderProgram);
            else
                throw new Exception("Filename " + fileName + " does not exist in listfile!");
        }

        public static unsafe DoodadBatch LoadM2(GL gl, uint fileDataID, uint shaderProgram)
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
                throw new Exception("Model " + fileDataID + " does not exist!");
            }

            //if (model.boundingbox == null)
            //    throw new Exception("Model does not contain bounding box: " + fileName);

            var doodadBatch = new DoodadBatch()
            {
                boundingBox = new BoundingBox()
                {
                    min = new Vector3(model.boundingbox[0].X, model.boundingbox[0].Y, model.boundingbox[0].Z),
                    max = new Vector3(model.boundingbox[1].X, model.boundingbox[1].Y, model.boundingbox[1].Z)
                }
            };

            if (model.textures == null)
                throw new Exception("Model does not contain textures: " + fileDataID);

            if (model.skins == null)
                throw new Exception("Model does not contain skins: " + fileDataID);

            // Textures
            doodadBatch.mats = new Material[model.textures.Count()];
            for (var i = 0; i < model.textures.Count(); i++)
            {
                uint textureFileDataID = DEFAULT_TEXTURE_ID;
                doodadBatch.mats[i].flags = model.textures[i].flags;

                switch (model.textures[i].type)
                {
                    case 0: // NONE
                        if (model.textureFileDataIDs != null && model.textureFileDataIDs.Length > 0 && model.textureFileDataIDs[i] != 0)
                            textureFileDataID = model.textureFileDataIDs[i];
                        else
                            throw new NotImplementedException();
                        //textureFileDataID = WoWFormatLib.Utils.CASC.getFileDataIdByName(model.textures[i].filename);
                        break;
                    case 1: // TEX_COMPONENT_SKIN
                    case 2: // TEX_COMPONENT_OBJECT_SKIN
                    case 11: // TEX_COMPONENT_MONSTER_1
                        break;
                }

                // Not set in TXID
                if (textureFileDataID == 0)
                    textureFileDataID = DEFAULT_TEXTURE_ID;

                if (!FileProvider.FileExists(textureFileDataID))
                    textureFileDataID = MISSING_TEXTURE_ID;

                doodadBatch.mats[i].textureID = Cache.GetOrLoadBLP(gl, textureFileDataID);
                doodadBatch.mats[i].filename = textureFileDataID.ToString();
            }

            // Submeshes
            doodadBatch.submeshes = new Renderer.Structs.Submesh[model.skins[0].submeshes.Count()];
            for (var i = 0; i < model.skins[0].submeshes.Count(); i++)
            {
                doodadBatch.submeshes[i].firstFace = model.skins[0].submeshes[i].startTriangle;
                doodadBatch.submeshes[i].numFaces = model.skins[0].submeshes[i].nTriangles;
                for (var tu = 0; tu < model.skins[0].textureunit.Count(); tu++)
                {
                    if (model.skins[0].textureunit[tu].submeshIndex == i)
                    {
                        doodadBatch.submeshes[i].blendType = model.renderflags[model.skins[0].textureunit[tu].renderFlags].blendingMode;

                        uint textureFileDataID = DEFAULT_TEXTURE_ID;
                        if (!FileProvider.FileExists(textureFileDataID))
                            textureFileDataID = MISSING_TEXTURE_ID;

                        if (model.textureFileDataIDs != null && model.textureFileDataIDs.Length > 0 && model.textureFileDataIDs[model.texlookup[model.skins[0].textureunit[tu].texture].textureID] != 0)
                        {
                            textureFileDataID = model.textureFileDataIDs[model.texlookup[model.skins[0].textureunit[tu].texture].textureID];
                        }
                        else
                        {
                            if (Listfile.FilenameToFDID.TryGetValue(model.textures[model.texlookup[model.skins[0].textureunit[tu].texture].textureID].filename.Replace('\\', '/').ToLower(), out var filedataid))
                            {
                                textureFileDataID = filedataid;
                            }
                            else
                            {
                                textureFileDataID = DEFAULT_TEXTURE_ID;
                                if (!FileProvider.FileExists(textureFileDataID))
                                    textureFileDataID = MISSING_TEXTURE_ID;
                            }
                        }

                        if (!FileProvider.FileExists(textureFileDataID))
                            textureFileDataID = MISSING_TEXTURE_ID;

                        doodadBatch.submeshes[i].material = (uint)Cache.GetOrLoadBLP(gl, textureFileDataID);
                    }
                }
            }

            doodadBatch.vao = gl.GenVertexArray();
            gl.BindVertexArray(doodadBatch.vao);

            // Vertices & indices
            doodadBatch.vertexBuffer = gl.GenBuffer();
            doodadBatch.indiceBuffer = gl.GenBuffer();
            //var ebo = new BufferObject<uint>(gl, Indices, BufferTargetARB.ElementArrayBuffer);
            //var vbo = new BufferObject<float>(gl, Vertices, BufferTargetARB.ArrayBuffer);
            //var vao = new VertexArrayObject<float, uint>(gl, Vbo, Ebo);

            //Vao.VertexAttributePointer(0, 3, VertexAttribPointerType.Float, 5, 0);
            //Vao.VertexAttributePointer(1, 2, VertexAttribPointerType.Float, 5, 3);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, doodadBatch.vertexBuffer);
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, doodadBatch.indiceBuffer);

            var modelindicelist = new List<uint>();
            for (var i = 0; i < model.skins[0].triangles.Count(); i++)
            {
                modelindicelist.Add(model.skins[0].triangles[i].pt1);
                modelindicelist.Add(model.skins[0].triangles[i].pt2);
                modelindicelist.Add(model.skins[0].triangles[i].pt3);
            }

            var modelindices = modelindicelist.ToArray();

            doodadBatch.indices = modelindices;

            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, doodadBatch.indiceBuffer);
            fixed (uint* buf = doodadBatch.indices)
                gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(doodadBatch.indices.Length * sizeof(uint)), buf, GLEnum.StaticDraw);

            var modelvertices = new M2Vertex[model.vertices.Count()];

            for (var i = 0; i < model.vertices.Count(); i++)
            {
                modelvertices[i].Position = new Vector3(model.vertices[i].position.X, model.vertices[i].position.Y, model.vertices[i].position.Z);
                modelvertices[i].Normal = new Vector3(model.vertices[i].normal.X, model.vertices[i].normal.Y, model.vertices[i].normal.Z);
                modelvertices[i].TexCoord = new Vector2(model.vertices[i].textureCoordX, model.vertices[i].textureCoordY);
            }
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, doodadBatch.vertexBuffer);
            fixed (M2Vertex* buf = modelvertices)
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(modelvertices.Length * 8 * sizeof(float)), buf, GLEnum.StaticDraw);

            //Set pointers in buffer
            //var normalAttrib = GL.GetAttribLocation(shaderProgram, "normal");
            //GL.EnableVertexAttribArray(normalAttrib);
            //GL.VertexAttribPointer(normalAttrib, 3, VertexAttribPointerType.Float, false, sizeof(float) * 8, sizeof(float) * 0);

            var texCoordAttrib = gl.GetAttribLocation(shaderProgram, "texCoord");
            gl.EnableVertexAttribArray((uint)texCoordAttrib);
            gl.VertexAttribPointer((uint)texCoordAttrib, 2, VertexAttribPointerType.Float, false, sizeof(float) * 8, (void*)(sizeof(float) * 3));

            var posAttrib = gl.GetAttribLocation(shaderProgram, "position");
            gl.EnableVertexAttribArray((uint)posAttrib);
            gl.VertexAttribPointer((uint)posAttrib, 3, VertexAttribPointerType.Float, false, sizeof(float) * 8, (void*)(sizeof(float) * 5));

            return doodadBatch;
        }
    }
}
