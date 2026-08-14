using System.Numerics;
using WoWFormatLib.FileReaders;
using WoWFormatLib.Structs.WMO;
using WoWRenderLib.Renderer;
using WoWRenderLib.Structs;

namespace WoWRenderLib.Loaders
{
    public class WMOLoader
    {
        public static unsafe PreppedWMO ParseWMO(uint fileDataID, string fileName = "")
        {
            WMO wmo = new WMOReader().LoadWMO(fileDataID, 0, fileName);

            var groupBatches = new List<PreppedWMOGroup>();
            for (var g = 0; g < wmo.group.Length; g++)
            {
                var group = wmo.group[g];

                MOGI? mogi = wmo.groupInfo is { } groupInfo && g < groupInfo.Length
                    ? groupInfo[g]
                    : null;
                var mogiGroupName = ResolveGroupName(
                    wmo.groupNames,
                    mogi?.nameIndex ?? (int)group.mogp.nameOffset);
                var groupName = ResolveGroupName(wmo.groupNames, (int)group.mogp.nameOffset);

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
                    sourceGroupIndex = g,
                    groupID = group.mogp.groupID,
                    groupName = groupName,
                    mogiGroupName = mogiGroupName,
                    mogiFlags = mogi is { } groupInfoEntry ? (uint)groupInfoEntry.flags : 0,
                    // Rendering classification comes from the group's MOGP
                    // header. Do not merge the root MOGI copy: those flags can
                    // disagree and incorrectly classify outdoor geometry as
                    // portal-cullable interior geometry.
                    flags = (uint)group.mogp.flags,
                    portalStart = group.mogp.ofsPortals,
                    portalCount = group.mogp.numPortals,
                    doodadReferences = group.mogp.doodadReferences ?? [],
                    boundingBox = new BoundingBox()
                    {
                        Min = new Vector3(wmo.group[g].mogp.boundingBox1.X, wmo.group[g].mogp.boundingBox1.Y, wmo.group[g].mogp.boundingBox1.Z),
                        Max = new Vector3(wmo.group[g].mogp.boundingBox2.X, wmo.group[g].mogp.boundingBox2.Y, wmo.group[g].mogp.boundingBox2.Z)
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
                        if (wmo.doodadDefinitions[i].offsetOrIndex == wmo.doodadNames[j].startOffset)
                            doodads[i].filename = wmo.doodadNames[j].filename;
                }
                else
                {
                    doodads[i].filedataid = wmo.doodadIds[wmo.doodadDefinitions[i].offsetOrIndex];
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
                    Min = new Vector3(wmo.header.boundingBox1.X, wmo.header.boundingBox1.Y, wmo.header.boundingBox1.Z),
                    Max = new Vector3(wmo.header.boundingBox2.X, wmo.header.boundingBox2.Y, wmo.header.boundingBox2.Z)
                },
                FileDataID = fileDataID,
                Materials = [.. mats],
                Doodads = doodads,
                DoodadSets = doodadSets,
                PreppedWMOGroups = [.. groupBatches],
                PortalVertices = wmo.portalVertices ?? [],
                Portals = wmo.portals?.Select(portal => new PreppedWMOPortal
                {
                    StartVertex = portal.startVertex,
                    VertexCount = portal.vertexCount,
                    Normal = portal.normal,
                    Distance = portal.distance
                }).ToArray() ?? [],
                PortalReferences = wmo.portalReferences?.Select(reference => new PreppedWMOPortalReference
                {
                    PortalIndex = reference.portalIndex,
                    GroupIndex = reference.groupIndex,
                    Side = reference.side
                }).ToArray() ?? [],
                SourceGroupCount = wmo.group.Length
            };
        }

        private static float CalculateBoundingRadius(Vector3 min, Vector3 max)
        {
            var center = (min + max) * 0.5f;
            return Vector3.Distance(center, max);
        }

        private static string ResolveGroupName(MOGN[] groupNames, int nameOffset)
        {
            for (var index = 0; index < groupNames.Length; index++)
            {
                if (groupNames[index].offset == nameOffset)
                    return groupNames[index].name.Replace(" ", "_");
            }

            return "";
        }
    }
}
