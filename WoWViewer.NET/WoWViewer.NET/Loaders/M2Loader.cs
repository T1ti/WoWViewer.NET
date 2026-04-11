using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices.Marshalling;
using WoWFormatLib.FileProviders;
using WoWFormatLib.FileReaders;
using WoWFormatLib.Structs.M2;
using WoWViewer.NET.Renderer;
using static WoWViewer.NET.Structs;

namespace WoWViewer.NET.Loaders
{
    class M2Loader
    {
        private static uint DEFAULT_TEXTURE_ID = 528732; // dungeons/textures/testing/color_01.blp

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
                    min = bbMin,
                    max = bbMax
                },
                boundingRadius = bbRadius
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

                // Not set in TXID
                if (textureFileDataID == 0)
                    textureFileDataID = DEFAULT_TEXTURE_ID;

                doodadBatch.mats[i].textureID = Cache.GetOrLoadBLP(gl, textureFileDataID, fileDataID);
            }

            // Submeshes
            doodadBatch.submeshes = new Submesh[model.skins[0].submeshes.Length];
            for (var i = 0; i < model.skins[0].submeshes.Length; i++)
            {
                uint material = 0;
                uint blendType = 0;
                var firstFace = model.skins[0].submeshes[i].startTriangle;
                var numFaces = model.skins[0].submeshes[i].nTriangles;

                for (var tu = 0; tu < model.skins[0].textureunit.Length; tu++)
                {
                    if (model.skins[0].textureunit[tu].submeshIndex != i)
                        continue;

                    var textureUnit = model.skins[0].textureunit[tu];

                    blendType = model.renderflags[textureUnit.renderFlags].blendingMode;

                    var textureID = model.texlookup[textureUnit.texture].textureID;

                    uint textureFileDataID = DEFAULT_TEXTURE_ID;

                    if (model.textureFileDataIDs != null && model.textureFileDataIDs.Length > 0 && model.textureFileDataIDs[textureID] != 0)
                        textureFileDataID = model.textureFileDataIDs[textureID];

                    material = Cache.GetOrLoadBLP(gl, textureFileDataID, fileDataID);

                    break;
                }

                doodadBatch.submeshes[i] = new Submesh()
                {
                    firstFace = firstFace,
                    numFaces = numFaces,
                    material = material,
                    blendType = blendType,
                    index = i
                };
            }

            doodadBatch.vao = gl.GenVertexArray();
            gl.BindVertexArray(doodadBatch.vao);

            doodadBatch.vertexBuffer = gl.GenBuffer();
            doodadBatch.indiceBuffer = gl.GenBuffer();

            gl.BindBuffer(BufferTargetARB.ArrayBuffer, doodadBatch.vertexBuffer);
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, doodadBatch.indiceBuffer);

            var modelindicelist = new List<uint>();
            for (var i = 0; i < model.skins[0].triangles.Length; i++)
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

            var modelvertices = new M2Vertex[model.vertices.Length];

            for (var i = 0; i < model.vertices.Length; i++)
            {
                modelvertices[i].Position = new Vector3(model.vertices[i].position.X, model.vertices[i].position.Y, model.vertices[i].position.Z);
                modelvertices[i].Normal = new Vector3(model.vertices[i].normal.X, model.vertices[i].normal.Y, model.vertices[i].normal.Z);
                modelvertices[i].TexCoord = new Vector2(model.vertices[i].textureCoordX, model.vertices[i].textureCoordY);
            }
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, doodadBatch.vertexBuffer);
            fixed (M2Vertex* buf = modelvertices)
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(modelvertices.Length * 8 * sizeof(float)), buf, GLEnum.StaticDraw);

            //Set pointers in buffer
            var normalAttrib = gl.GetAttribLocation(shaderProgram, "normal");
            gl.EnableVertexAttribArray((uint)normalAttrib);
            gl.VertexAttribPointer((uint)normalAttrib, 3, VertexAttribPointerType.Float, false, sizeof(float) * 8, (void*)(sizeof(float) * 0));

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
