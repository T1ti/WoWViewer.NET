using Silk.NET.OpenGL;
using System.Numerics;
using WoWFormatLib.FileProviders;
using WoWFormatLib.FileReaders;
using WoWFormatLib.Structs.M2;
using WoWRenderLib.Cache;
using WoWRenderLib.Structs;
using static WoWRenderLib.Renderer.ShaderEnums;

namespace WoWRenderLib.Loaders
{
    class M2Loader
    {
        private static uint DEFAULT_TEXTURE_ID = 186184; // dungeons/textures/testing/color_01.blp

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

                doodadBatch.mats[i].textureID = BLPCache.GetOrLoad(gl, textureFileDataID, fileDataID);
            }

            // Submeshes
            doodadBatch.submeshes = new Submesh[model.skins[0].submeshes.Length];
            for (var i = 0; i < model.skins[0].submeshes.Length; i++)
            {
                uint material = 0;
                uint blendType = 0;
                var firstFace = model.skins[0].submeshes[i].startTriangle;
                var numFaces = model.skins[0].submeshes[i].nTriangles;
                uint vertexShaderID = 0;
                uint pixelShaderID = 0;

                for (var tu = 0; tu < model.skins[0].textureunit.Length; tu++)
                {
                    if (model.skins[0].textureunit[tu].submeshIndex != i)
                        continue;

                    var textureUnit = model.skins[0].textureunit[tu];

                    blendType = model.renderflags[textureUnit.renderFlagsIndex].blendingMode;
                    vertexShaderID = (uint)GetVertexShaderID(textureUnit.mode, textureUnit.shaderID);
                    pixelShaderID = (uint)GetPixelShaderID(textureUnit.mode, textureUnit.shaderID);

                    var textureID = model.texlookup[textureUnit.texture].textureID;

                    uint textureFileDataID = DEFAULT_TEXTURE_ID;

                    if (model.textureFileDataIDs != null && model.textureFileDataIDs.Length > 0 && model.textureFileDataIDs[textureID] != 0)
                        textureFileDataID = model.textureFileDataIDs[textureID];

                    material = BLPCache.GetOrLoad(gl, textureFileDataID, fileDataID);

                    break;
                }

                doodadBatch.submeshes[i] = new Submesh()
                {
                    firstFace = firstFace,
                    numFaces = numFaces,
                    material = material,
                    blendType = blendType,
                    index = i,
                    vertexShaderID = vertexShaderID,
                    pixelShaderID = pixelShaderID
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
                modelvertices[i].TexCoord1 = new Vector2(model.vertices[i].textureCoordX, model.vertices[i].textureCoordY);
                modelvertices[i].TexCoord2 = new Vector2(model.vertices[i].textureCoordX2, model.vertices[i].textureCoordY2);
            }
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, doodadBatch.vertexBuffer);
            fixed (M2Vertex* buf = modelvertices)
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(modelvertices.Length * 10 * sizeof(float)), buf, GLEnum.StaticDraw);

            //Set pointers in buffer
            var normalAttrib = gl.GetAttribLocation(shaderProgram, "normal");
            gl.EnableVertexAttribArray((uint)normalAttrib);
            gl.VertexAttribPointer((uint)normalAttrib, 3, VertexAttribPointerType.Float, false, sizeof(float) * 10, (void*)(sizeof(float) * 0));

            var texCoord1Attrib = gl.GetAttribLocation(shaderProgram, "texCoord1");
            gl.EnableVertexAttribArray((uint)texCoord1Attrib);
            gl.VertexAttribPointer((uint)texCoord1Attrib, 2, VertexAttribPointerType.Float, false, sizeof(float) * 10, (void*)(sizeof(float) * 3));

            var texCoord2Attrib = gl.GetAttribLocation(shaderProgram, "texCoord2");
            gl.EnableVertexAttribArray((uint)texCoord2Attrib);
            gl.VertexAttribPointer((uint)texCoord2Attrib, 2, VertexAttribPointerType.Float, false, sizeof(float) * 10, (void*)(sizeof(float) * 5));

            var posAttrib = gl.GetAttribLocation(shaderProgram, "position");
            gl.EnableVertexAttribArray((uint)posAttrib);
            gl.VertexAttribPointer((uint)posAttrib, 3, VertexAttribPointerType.Float, false, sizeof(float) * 10, (void*)(sizeof(float) * 7));

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
    }
}
