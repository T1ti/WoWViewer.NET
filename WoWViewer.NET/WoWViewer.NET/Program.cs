using ImGuiNET;
using SceneScriptLib;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using WoWFormatLib.FileProviders;
using WoWViewer.NET.Objects;
using WoWViewer.NET.Renderer;
using static WoWViewer.NET.Structs;

namespace WoWViewer.NET
{
    internal class Program
    {
        private static bool cascLoaded = false;
        private static bool sceneLoaded = false;

        private static string statusMessage = "";

        private static uint adtShaderProgram;
        private static uint wmoShaderProgram;
        private static uint m2ShaderProgram;

        private static float movementSpeed = 150f;
        private static bool isMouseDragging = false;
        private static bool hasFocus = true;

        public static GL gl;

        private static Camera activeCamera;
        private static IInputContext inputContext;

        public static List<Container3D> sceneObjects = new();
        public static Lock sceneObjectLock = new();

        private static IWindow window;
        private static Vector2 LastMousePosition;

        private static bool renderADT = true;
        private static bool renderWMO = true;
        private static bool renderM2 = false;
        private static bool shadersReady = false;

        private static readonly Dictionary<string, DateTime> shaderMTimes = [];
        private static readonly List<TimelineScene> scenes = [];

        private static uint defaultTextureID;

        private static Queue<MapTile> tilesToLoad = new();
        private static int totalTilesToLoad = 0;
        private static HashSet<uint> usedUUIDs = new();
        private static HashSet<MapTile> loadedTiles = new();

        private static uint CurrentWDTFileDataID = 775971;

        static void Main(string[] args)
        {
            var windowOptions = WindowOptions.Default;
            windowOptions.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible | ContextFlags.Debug, new APIVersion(4, 3));
            windowOptions.ShouldSwapAutomatically = false;
            windowOptions.Size = new Vector2D<int>(1920, 1080);
            windowOptions.Title = "WoWViewer.NET";
            window = Window.Create(windowOptions);

#if DEBUG
            Evergine.Bindings.RenderDoc.RenderDoc.Load(out Evergine.Bindings.RenderDoc.RenderDoc renderDoc);
#endif
            gl = null;

            ImGuiController imGuiController = null;

            foreach (var file in Directory.GetFiles(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Shaders"), "*.shader"))
            {
                shaderMTimes.Add(file, File.GetLastWriteTime(file));
            }

            window.Load += () =>
            {
                gl = window.CreateOpenGL();

                //   gl.Enable(EnableCap.DebugOutput);
                //    gl.Enable(EnableCap.DebugOutputSynchronous);
                imGuiController = new ImGuiController(
                    gl,
                    window,
                    inputContext = window.CreateInput()
                );

                ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;
                ImGui.GetIO().ConfigDockingAlwaysTabBar = true;

                ImGui.GetStyle().WindowRounding = 5.0f;
                ImGui.GetStyle().WindowPadding = new Vector2(0.0f, 0.0f);
                ImGui.GetStyle().FrameRounding = 12.0f;

                for (int i = 0; i < inputContext.Mice.Count; i++)
                {
                    inputContext.Mice[i].MouseMove += OnMouseMove;
                    inputContext.Mice[i].Scroll += OnMouseWheel;
                }

                var err = gl.GetError();
                if (err != GLEnum.NoError)
                {
                    Console.WriteLine("Render GL Error: " + err);
                }

                adtShaderProgram = ShaderCompiler.CompileShader("adt");
                wmoShaderProgram = ShaderCompiler.CompileShader("wmo");
                m2ShaderProgram = ShaderCompiler.CompileShader("m2");
                shadersReady = true;

                // Start CASC initialization in background
                statusMessage = "Initializing CASC..";
                Task.Run(async () =>
                {
                    await Services.CASC.Initialize();

                    var tactFileProvider = new TACTSharpFileProvider();
                    tactFileProvider.InitTACT(Services.CASC.buildInstance);
                    FileProvider.SetDefaultBuild(TACTSharpFileProvider.BuildName);
                    FileProvider.SetProvider(tactFileProvider, TACTSharpFileProvider.BuildName);

                    cascLoaded = true;

                    statusMessage = "";
                });

                activeCamera = new Camera(new Vector3(0f, -0f, 200f), Vector3.UnitX, Vector3.UnitZ * -1, (float)window.FramebufferSize.X / (float)window.FramebufferSize.Y);
                gl.Viewport(window.FramebufferSize);
                gl.ClearColor(1.0f, 0.0f, 0.0f, 1.0f);

                var err2 = gl.GetError();
                if (err2 != GLEnum.NoError)
                {
                    Console.WriteLine("Load GL Error: " + err2);
                }

                unsafe
                {
                    gl.DebugMessageCallback((source, type, id, severity, length, message, userparam) =>
                    {
                        string msg = Marshal.PtrToStringAnsi(message, length);
                        if (id == 131185)
                            return;

                        Console.Error.WriteLine($"[DebugMessageCallback] source: {source}, type: {type}, id: {id}, severity {severity}, length {length}, userParam {userparam}\n{msg}\n\n");
                    }, (void*)0);
                }

                defaultTextureID = MakeDefaultTexture();
            };

            window.FramebufferResize += s =>
            {
                activeCamera.AspectRatio = (float)s.X / (float)s.Y;
                gl.Viewport(s);
            };

            window.FocusChanged += focused =>
            {
                hasFocus = true;
            };

            window.Update += delta =>
            {
                var primaryKeyboard = inputContext.Keyboards[0];

                var moveSpeed = movementSpeed * (float)delta;

                if (primaryKeyboard.IsKeyPressed(Key.ShiftLeft))
                    moveSpeed *= 2.0f;

                imGuiController.Update((float)delta);

                if (primaryKeyboard.IsKeyPressed(Key.W))
                    activeCamera.Position += moveSpeed * activeCamera.Front;

                if (primaryKeyboard.IsKeyPressed(Key.S))
                    activeCamera.Position -= moveSpeed * activeCamera.Front;

                if (primaryKeyboard.IsKeyPressed(Key.A))
                    activeCamera.Position += Vector3.Normalize(Vector3.Cross(activeCamera.Front, activeCamera.Up)) * moveSpeed;

                if (primaryKeyboard.IsKeyPressed(Key.D))
                    activeCamera.Position -= Vector3.Normalize(Vector3.Cross(activeCamera.Front, activeCamera.Up)) * moveSpeed;

                if (primaryKeyboard.IsKeyPressed(Key.Up))
                    activeCamera.Position -= moveSpeed * activeCamera.Up;

                if (primaryKeyboard.IsKeyPressed(Key.Down))
                    activeCamera.Position += moveSpeed * activeCamera.Up;

                if (primaryKeyboard.IsKeyPressed(Key.R))
                    activeCamera.Position = Vector3.One;

#if DEBUG
                // Note -- this is extremely slow but allows for shader hot-reloading
                foreach (var file in Directory.GetFiles(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Shaders"), "*.shader"))
                {
                    if (shaderMTimes[file] < File.GetLastWriteTime(file))
                    {
                        shadersReady = false;
                        Console.WriteLine("Reloading shader " + file);

                        if (Path.GetFileNameWithoutExtension(file).StartsWith("adt"))
                        {
                            adtShaderProgram = ShaderCompiler.CompileShader("adt");
                        }
                        else if (Path.GetFileNameWithoutExtension(file).StartsWith("wmo"))
                        {
                            wmoShaderProgram = ShaderCompiler.CompileShader("wmo");
                        }
                        else if (Path.GetFileNameWithoutExtension(file).StartsWith("m2"))
                        {
                            m2ShaderProgram = ShaderCompiler.CompileShader("m2");
                        }

                        shadersReady = true;

                        shaderMTimes[file] = File.GetLastWriteTime(file);
                    }
                }
#endif
            };

            window.Render += delta =>
            {
                if (!hasFocus || !shadersReady)
                    return;

#if DEBUG
                var err = gl.GetError();
                if (err != GLEnum.NoError)
                {
                    Console.WriteLine("Window Render GL Error: " + err);
                }
#endif

                gl.Enable(EnableCap.DepthTest);

                gl.ClearColor(0f, 0f, 0f, 0.5f);
                gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                if (tilesToLoad.Count > 0)
                {
                    var mapTile = tilesToLoad.Dequeue();
                    var tilesLoaded = totalTilesToLoad - tilesToLoad.Count;
                    statusMessage = $"Loading tile {mapTile.tileX},{mapTile.tileY} ({tilesLoaded}/{totalTilesToLoad})...";

                    var timer = new Stopwatch();
                    timer.Start();
                    var adt = Cache.GetOrLoadADT(gl, mapTile, adtShaderProgram, mapTile.wdtFileDataID);
                    timer.Stop();

                    Console.WriteLine($"Loaded ADT {mapTile.tileX},{mapTile.tileY} in {timer.ElapsedMilliseconds} ms");

                    var adtContainer = new ADTContainer(gl, adt, mapTile, adtShaderProgram);
                    sceneObjects.Add(adtContainer);

                    foreach (var worldModel in adt.worldModelBatches)
                    {
                        if (usedUUIDs.Contains(worldModel.uniqueID))
                            continue;

                        var worldModelContainer = new WMOContainer(gl, worldModel.fileDataID, wmoShaderProgram, adt.rootADTFileDataID)
                        {
                            Position = worldModel.position,
                            Rotation = worldModel.rotation,
                            Scale = worldModel.scale,
                            UniqueID = worldModel.uniqueID
                        };

                        sceneObjects.Add(worldModelContainer);
                        usedUUIDs.Add(worldModel.uniqueID);
                    }

                    foreach (var doodad in adt.doodads)
                    {
                        var doodadContainer = new M2Container(gl, doodad.fileDataID, m2ShaderProgram, adt.rootADTFileDataID)
                        {
                            Position = doodad.position,
                            Rotation = doodad.rotation,
                            Scale = doodad.scale
                        };

                        sceneObjects.Add(doodadContainer);
                    }

                    // Mark this tile as loaded
                    loadedTiles.Add(mapTile);

                    if (tilesToLoad.Count == 0)
                    {
                        sceneLoaded = true;
                        statusMessage = "";
                    }
                }

                UpdateTilesByCameraPos();

                ImGUIDockSpace();

                if (!string.IsNullOrEmpty(statusMessage))
                {
                    ImGui.Begin("Loading");
                    ImGui.Text(statusMessage);
                    ImGui.End();
                }

                if (sceneObjects.Count > 0)
                {
                    ImGui.Begin("3D debug");

                    ImGui.Checkbox("Render ADT", ref renderADT);
                    ImGui.Checkbox("Render WMO", ref renderWMO);
                    ImGui.Checkbox("Render M2", ref renderM2);
                    var newPos = activeCamera.Position;
                    ImGui.DragFloat3("Camera position", ref newPos);
                    activeCamera.Position = newPos;

                    var newFront = activeCamera.Front;
                    ImGui.DragFloat3("Camera front", ref newFront);
                    activeCamera.Front = newFront;

                    ImGui.DragFloat("Camera movement speed", ref movementSpeed);

                    var yaw = activeCamera.Yaw;
                    ImGui.DragFloat("Camera yaw", ref yaw);
                    activeCamera.Yaw = yaw;

                    var pitch = activeCamera.Pitch;
                    ImGui.DragFloat("Camera pitch", ref pitch);
                    activeCamera.Pitch = pitch;

                    var roll = activeCamera.Roll;
                    ImGui.DragFloat("Camera roll", ref roll);
                    activeCamera.Roll = roll;

                    ImGui.Text(sceneObjects.Count.ToString() + " loaded objects (" + sceneObjects.Count(x => x is M2Container).ToString() + " M2, " + sceneObjects.Count(x => x is WMOContainer).ToString() + " WMO, " + sceneObjects.Count(x => x is ADTContainer).ToString() + " ADT)");

                    ImGui.Text("Current ADT: " + GetTileFromPosition(activeCamera.Position).ToString());
                    ImGui.Text("RAM usage: " + (GC.GetTotalMemory(false) / 1024 / 1024).ToString() + " MB");

                    var i = 0;
                    if (ImGui.CollapsingHeader("Loaded WMOs"))
                    {
                        foreach (var sceneObject in sceneObjects.Where(x => x is WMOContainer))
                        {
                            var wmoContainer = (WMOContainer)sceneObject;
                            var wmoString = "WMO #" + i + " FDID " + wmoContainer.FileDataId.ToString();

                            if (ImGui.CollapsingHeader(wmoString))
                            {
                                var curPos = wmoContainer.Position;
                                ImGui.DragFloat3("WMO Pos " + i, ref curPos);
                                wmoContainer.Position = curPos;

                                var curRot = wmoContainer.Rotation;
                                ImGui.DragFloat3("WMO Rot " + i, ref curRot);
                                wmoContainer.Rotation = curRot;

                                var curScale = wmoContainer.Scale;
                                ImGui.DragFloat("WMO Scale " + i, ref curScale, 0.01f);
                                wmoContainer.Scale = curScale;
                            }

                            i++;
                        }
                    }

                    i = 0;
                    if (ImGui.CollapsingHeader("Loaded M2s"))
                    {
                        foreach (var sceneObject in sceneObjects.Where(x => x is M2Container))
                        {
                            var m2Container = (M2Container)sceneObject;
                            if (ImGui.CollapsingHeader("M2 #" + i + " FDID " + m2Container.FileDataId.ToString()))
                            {
                                var curPos = m2Container.Position;
                                ImGui.DragFloat3("M2 Pos " + i, ref curPos);
                                m2Container.Position = curPos;

                                var curRot = m2Container.Rotation;
                                ImGui.DragFloat3("M2 Rot " + i, ref curRot);
                                m2Container.Rotation = curRot;

                                var curScale = m2Container.Scale;
                                ImGui.DragFloat("M2 Scale " + i, ref curScale, 0.01f);
                                m2Container.Scale = curScale;
                            }

                            i++;
                        }
                    }
                    ImGui.End();
                }

                RenderScene();

                imGuiController.Render();

                window.SwapBuffers();
            };

            window.Closing += () =>
            {
                imGuiController?.Dispose();
                inputContext?.Dispose();
                gl?.Dispose();
            };

            window.Run();

            window.Dispose();
        }

        private static void ImGUIDockSpace()
        {
            var opt_fullscreen = true;
            var opt_padding = false;
            var dockspace_flags = ImGuiDockNodeFlags.None | ImGuiDockNodeFlags.PassthruCentralNode | ImGuiDockNodeFlags.NoDockingOverCentralNode;

            ImGuiWindowFlags window_flags = ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoBackground;

            if (opt_fullscreen)
            {
                var viewport = ImGui.GetMainViewport();
                ImGui.SetNextWindowPos(viewport.WorkPos);
                ImGui.SetNextWindowSize(viewport.WorkSize);
                ImGui.SetNextWindowViewport(viewport.ID);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
                window_flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
                window_flags |= ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;
            }

            dockspace_flags |= ImGuiDockNodeFlags.PassthruCentralNode;

            ImGui.Begin("DockSpace Demo", window_flags);

            if (!opt_padding)
                ImGui.PopStyleVar();
            if (opt_fullscreen)
                ImGui.PopStyleVar(2);

            // Submit the DockSpace
            if (ImGui.GetIO().ConfigFlags.HasFlag(ImGuiConfigFlags.DockingEnable))
            {
                var dockspace_id = ImGui.GetID("MyDockSpace");
                ImGui.DockSpace(dockspace_id, new Vector2(0.0f, 0.0f), dockspace_flags);
            }
        }

        private static void OnMouseMove(IMouse mouse, Vector2 position)
        {

            if (!mouse.IsButtonPressed(MouseButton.Left) || ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow) || ImGui.IsAnyItemActive())
            {
                LastMousePosition = default;
                return;
            }

            var lookSensitivity = 0.1f;
            if (LastMousePosition == default) { LastMousePosition = position; }
            else
            {
                var xOffset = (position.X - LastMousePosition.X) * lookSensitivity;
                var yOffset = (LastMousePosition.Y - position.Y) * lookSensitivity;
                LastMousePosition = position;

                activeCamera.ModifyDirection(xOffset, yOffset);
            }
        }
        private static void OnMouseWheel(IMouse mouse, ScrollWheel scrollWheel)
        {
            if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow) || ImGui.IsAnyItemActive())
                return;

            activeCamera.ModifyZoom(scrollWheel.Y);
        }

        private static unsafe void RenderScene()
        {
            var m2ProjLocation = gl.GetUniformLocation(m2ShaderProgram, "projection_matrix");
            var m2ViewLocation = gl.GetUniformLocation(m2ShaderProgram, "view_matrix");
            var m2ModelLocation = gl.GetUniformLocation(m2ShaderProgram, "model_matrix");

            var wmoProjLocation = gl.GetUniformLocation(wmoShaderProgram, "projection_matrix");
            var wmoViewLocation = gl.GetUniformLocation(wmoShaderProgram, "view_matrix");
            var wmoModelLocation = gl.GetUniformLocation(wmoShaderProgram, "model_matrix");
            var wmoVertexShaderIDLocation = gl.GetUniformLocation(wmoShaderProgram, "vertexShader");
            var wmoPixelShaderIDLocation = gl.GetUniformLocation(wmoShaderProgram, "pixelShader");

            var adtProjLocation = gl.GetUniformLocation(adtShaderProgram, "projection_matrix");
            var adtRotLocation = gl.GetUniformLocation(adtShaderProgram, "rotation_matrix");
            var adtModelLocation = gl.GetUniformLocation(adtShaderProgram, "model_matrix");

            var heightScaleUniforms = new int[8];
            for (int i = 0; i < 8; i++)
                heightScaleUniforms[i] = gl.GetUniformLocation(adtShaderProgram, $"heightScales[{i}]");

            var heightOffsetUniforms = new int[8];
            for (int i = 0; i < 8; i++)
                heightOffsetUniforms[i] = gl.GetUniformLocation(adtShaderProgram, $"heightOffsets[{i}]");

            var layerScaleUniforms = new int[8];
            for (int i = 0; i < 8; i++)
                layerScaleUniforms[i] = gl.GetUniformLocation(adtShaderProgram, $"layerScales[{i}]");

            var alphaLayerUniforms = new int[2];
            for (int i = 0; i < 2; i++)
                alphaLayerUniforms[i] = gl.GetUniformLocation(adtShaderProgram, $"alphaLayers[{i}]");

            var diffuseLayerUniforms = new int[8];
            for (int i = 0; i < 8; i++)
                diffuseLayerUniforms[i] = gl.GetUniformLocation(adtShaderProgram, $"diffuseLayers[{i}]");

            var heightLayerUniforms = new int[8];
            for (int i = 0; i < 8; i++)
                heightLayerUniforms[i] = gl.GetUniformLocation(adtShaderProgram, $"heightLayers[{i}]");

            var m2AlphaRefLoc = gl.GetUniformLocation(m2ShaderProgram, "alphaRef");
            var wmoAlphaRefLoc = gl.GetUniformLocation(wmoShaderProgram, "alphaRef");

            var projectionMatrix = activeCamera.GetProjectionMatrix();

            foreach (var sceneObject in sceneObjects)
            {
                if (sceneObject is M2Container activeM2)
                {
                    if (!renderM2 && !activeM2.forceRender)
                        continue;

                    if (activeM2.FileDataId == 2061670)
                    {
                        activeM2.FileDataId = 2061670;
                        activeM2.Rotation = new Vector3(activeM2.Rotation.X, activeM2.Rotation.Y * -1, activeM2.Rotation.Z);
                        activeM2.Scale = 1f;
                    }

                    var m2 = Cache.GetOrLoadM2(gl, activeM2.FileDataId, m2ShaderProgram, activeM2.ParentFileDataId);

                    gl.UseProgram(m2ShaderProgram);

                    gl.UniformMatrix4(m2ProjLocation, 1, false, (float*)&projectionMatrix);

                    var viewMatrix = activeCamera.GetViewMatrix();
                    viewMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * 180f);

                    gl.UniformMatrix4(m2ViewLocation, 1, false, (float*)&viewMatrix);

                    // Model matrix contains position, rotation and scale
                    var modelMatrix = Matrix4x4.CreateScale(sceneObject.Scale);

                    // Apply ADT rotation
                    modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * (sceneObject.Rotation.Y - 270f));
                    //modelMatrix *= Matrix4x4.CreateRotationX(MathF.PI / 180f * (-wmoContainer.Rotation.X));
                    //modelMatrix *= Matrix4x4.CreateRotationY(MathF.PI / 180f * (wmoContainer.Rotation.Z - 90f));

                    modelMatrix *= Matrix4x4.CreateTranslation(sceneObject.Position.X, sceneObject.Position.Z * -1, sceneObject.Position.Y);

                    // Post-transform rotation
                    modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * -270f);

                    gl.UniformMatrix4(m2ModelLocation, 1, false, (float*)&modelMatrix);

                    gl.ActiveTexture(TextureUnit.Texture0);

                    gl.BindVertexArray(m2.vao);

                    for (var i = 0; i < m2.submeshes.Length; i++)
                    {
                        var submesh = m2.submeshes[i];
                        if (!activeM2.EnabledGeosets[i])
                            continue;

                        SwitchBlendMode((int)submesh.blendType, gl, m2AlphaRefLoc);

                        gl.BindTexture(TextureTarget.Texture2D, submesh.material);
                        gl.DrawElements(PrimitiveType.Triangles, submesh.numFaces, DrawElementsType.UnsignedInt, (void*)(submesh.firstFace * 4));
                        gl.BindTexture(TextureTarget.Texture2D, 0);
                    }

#if DEBUG
                    var err = gl.GetError();
                    if (err != GLEnum.NoError)
                        Console.WriteLine("M2 render GL Error: " + err);
#endif
                }
                else if (sceneObject is WMOContainer wmoContainer)
                {
                    if (!renderWMO)
                        continue;

                    var wmo = Cache.GetOrLoadWMO(gl, sceneObject.FileDataId, wmoShaderProgram, sceneObject.ParentFileDataId);

                    gl.UseProgram(wmoShaderProgram);

                    gl.UniformMatrix4(wmoProjLocation, 1, false, (float*)&projectionMatrix);

                    var viewMatrix = activeCamera.GetViewMatrix();
                    viewMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * 180f);

                    gl.UniformMatrix4(wmoViewLocation, 1, false, (float*)&viewMatrix);

                    // Model matrix contains position, rotation and scale
                    var modelMatrix = Matrix4x4.CreateScale(sceneObject.Scale);

                    // Apply ADT rotation

                    // TODO: FIX
                    //modelMatrix *= Matrix4x4.CreateRotationX(MathF.PI / 180f * (wmoContainer.Rotation.X));
                    //modelMatrix *= Matrix4x4.CreateRotationY(MathF.PI / 180f * (-wmoContainer.Rotation.Z));
                    modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * (wmoContainer.Rotation.Y - 270f));

                    modelMatrix *= Matrix4x4.CreateTranslation(wmoContainer.Position.X, wmoContainer.Position.Z * -1, wmoContainer.Position.Y);

                    // Post-transform rotation
                    modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * -270f);

                    gl.UniformMatrix4(wmoModelLocation, 1, false, (float*)&modelMatrix);

                    for (var j = 0; j < wmo.wmoRenderBatch.Length; j++)
                    {
                        if (wmo.groupBatches[wmo.wmoRenderBatch[j].groupID].vao == 0)
                            continue;

                        var firstFace = wmo.wmoRenderBatch[j].firstFace;
                        var numFaces = wmo.wmoRenderBatch[j].numFaces;

                        gl.BindVertexArray(wmo.groupBatches[wmo.wmoRenderBatch[j].groupID].vao);

                        gl.Uniform1(wmoVertexShaderIDLocation, (float)ShaderEnums.WMOShaders[(int)wmo.wmoRenderBatch[j].shader].VertexShader);
                        gl.Uniform1(wmoPixelShaderIDLocation, (float)ShaderEnums.WMOShaders[(int)wmo.wmoRenderBatch[j].shader].PixelShader);

                        SwitchBlendMode((int)wmo.wmoRenderBatch[j].blendType, gl, wmoAlphaRefLoc);

                        for (var m = 0; m < wmo.wmoRenderBatch[j].materialID.Length; m++)
                        {
                            gl.ActiveTexture(TextureUnit.Texture0 + m);
                            if (wmo.wmoRenderBatch[j].materialID[m] == -1)
                                gl.BindTexture(TextureTarget.Texture2D, defaultTextureID);
                            else
                                gl.BindTexture(TextureTarget.Texture2D, (uint)wmo.wmoRenderBatch[j].materialID[m]);
                        }

                        gl.DrawElements(PrimitiveType.Triangles, numFaces, DrawElementsType.UnsignedShort, (void*)(firstFace * 2));

                        for (var m = 0; m < wmo.wmoRenderBatch[j].materialID.Length; m++)
                        {
                            gl.ActiveTexture(TextureUnit.Texture0 + m);
                            gl.BindTexture(TextureTarget.Texture2D, 0);
                        }
                    }
#if DEBUG
                    var err = gl.GetError();
                    if (err != GLEnum.NoError)
                        Console.WriteLine("WMO render GL Error: " + err);
#endif
                }
                else if (sceneObject is ADTContainer adt)
                {
                    if (!renderADT)
                        continue;

                    gl.UseProgram(adtShaderProgram);

                    var modelviewMatrix = Matrix4x4.CreateRotationZ(MathF.PI / 180f * 180f);
                    gl.UniformMatrix4(adtModelLocation, 1, false, (float*)&modelviewMatrix);

                    var rotationMatrix = activeCamera.GetViewMatrix();
                    gl.UniformMatrix4(adtRotLocation, 1, false, (float*)&rotationMatrix);
                    gl.UniformMatrix4(adtProjLocation, 1, false, (float*)&projectionMatrix);

                    gl.BindVertexArray(adt.Terrain.vao);
                    gl.Disable(EnableCap.Blend);

                    for (int c = 0; c < 256; c++)
                    {
                        var batch = adt.Terrain.renderBatches[c];

                        for (int j = 0; j < 2; j++)
                        {
                            gl.Uniform1(alphaLayerUniforms[j], j);
                            gl.ActiveTexture(TextureUnit.Texture0 + j);
                            gl.BindTexture(TextureTarget.Texture2D, (batch.alphaMaterialID[j]) == -1 ? defaultTextureID : (uint)batch.alphaMaterialID[j]);
                        }

                        for (int j = 0; j < 8; j++)
                        {
                            gl.Uniform1(heightScaleUniforms[j], batch.heightScales[j]);
                            gl.Uniform1(heightOffsetUniforms[j], batch.heightOffsets[j]);
                            gl.Uniform1(layerScaleUniforms[j], batch.scales[j]);

                            gl.Uniform1(diffuseLayerUniforms[j], j + 7);
                            gl.ActiveTexture(TextureUnit.Texture7 + j);
                            gl.BindTexture(TextureTarget.Texture2D, (batch.materialID[j]) == -1 ? defaultTextureID : (uint)batch.materialID[j]);
                            gl.Uniform1(heightLayerUniforms[j], j + 15);
                            gl.ActiveTexture(TextureUnit.Texture15 + j);
                            gl.BindTexture(TextureTarget.Texture2D, (batch.heightMaterialIDs[j]) == -1 ? defaultTextureID : (uint)batch.heightMaterialIDs[j]);
                        }

                        gl.DrawElements(PrimitiveType.Triangles, (uint)((c + 1) * 768) - (uint)c * 768, DrawElementsType.UnsignedInt, (void*)((c * 768) * 4));

                        for (int j = 0; j < 8; j++)
                        {
                            gl.ActiveTexture(TextureUnit.Texture0 + j);
                            gl.BindTexture(TextureTarget.Texture2D, 0);
                            gl.ActiveTexture(TextureUnit.Texture7 + j);
                            gl.BindTexture(TextureTarget.Texture2D, 0);
                            gl.ActiveTexture(TextureUnit.Texture15 + j);
                            gl.BindTexture(TextureTarget.Texture2D, 0);
                        }
                    }

#if DEBUG
                    var err = gl.GetError();
                    if (err != GLEnum.NoError)
                        Console.WriteLine("ADT render GL Error: " + err);
#endif
                }
            }

            gl.BindVertexArray(0);
        }

        private static (byte x, byte y) GetTileFromPosition(Vector3 position)
        {
            const float tileSize = 533.33333f;
            const int mapCenter = 32;

            // todo: this is not super correct but close enough for 3x3 load, check math with minimap tool at some point
            int tileX = mapCenter - (int)Math.Floor(position.Y / tileSize);
            int tileY = mapCenter - (int)Math.Floor(position.X / tileSize);

            tileX = Math.Clamp(tileX, 0, 63);
            tileY = Math.Clamp(tileY, 0, 63);

            return ((byte)tileX, (byte)tileY);
        }

        private static void UpdateTilesByCameraPos()
        {
            if (!cascLoaded)
                return;

            var (x, y) = GetTileFromPosition(activeCamera.Position);

            var usedTiles = new List<MapTile>();

            for (int xOffset = -1; xOffset <= 1; xOffset++)
            {
                for (int yOffset = -1; yOffset <= 1; yOffset++)
                {
                    int tileX = x + xOffset;
                    int tileY = y + yOffset;

                    // oob check
                    if (tileX < 0 || tileX > 63 || tileY < 0 || tileY > 63)
                        continue;

                    var mapTile = new MapTile
                    {
                        tileX = (byte)tileX,
                        tileY = (byte)tileY,
                        wdtFileDataID = CurrentWDTFileDataID
                    };

                    usedTiles.Add(mapTile);

                    if (!loadedTiles.Contains(mapTile) && !tilesToLoad.Contains(mapTile))
                    {
                        Console.WriteLine($"Queuing tile {mapTile.tileX},{mapTile.tileY} for load (3x3 around camera, which is in tile {x},{y})");
                        tilesToLoad.Enqueue(mapTile);
                        totalTilesToLoad++;
                    }
                }
            }

            // Unload tiles that are no longer in the 3x3 around the camera
            foreach (var tile in loadedTiles)
            {
                if (!usedTiles.Contains(tile))
                {
                    Console.WriteLine($"Releasing tile {tile.tileX},{tile.tileY} as it's no longer in the 3x3 around the camera");
                    loadedTiles.Remove(tile);

                    lock (sceneObjectLock)
                    {
                        // Not a fan of using LINQ here, probably need a better way for this
                        var adtToRemove = sceneObjects.FirstOrDefault(x => x is ADTContainer adt && adt.mapTile.wdtFileDataID == tile.wdtFileDataID && adt.mapTile.tileX == tile.tileX && adt.mapTile.tileY == tile.tileY) as ADTContainer;
                        if (adtToRemove != null)
                        {
                            sceneObjects.Remove(adtToRemove);
                            Cache.ReleaseADT(gl, adtToRemove.mapTile, adtToRemove.mapTile.wdtFileDataID);

                            List<WMOContainer> wmosToRemove = sceneObjects.Where(x => x is WMOContainer wmo && wmo.ParentFileDataId == adtToRemove.Terrain.rootADTFileDataID).Select(x => (WMOContainer)x).ToList();
                            foreach (var wmo in wmosToRemove)
                            {
                                sceneObjects.Remove(wmo);
                                usedUUIDs.Remove(wmo.UniqueID);
                                Cache.ReleaseWMO(gl, wmo.FileDataId, wmo.ParentFileDataId);
                            }

                            List<M2Container> m2sToRemove = sceneObjects.Where(x => x is M2Container m2 && m2.ParentFileDataId == adtToRemove.Terrain.rootADTFileDataID).Select(x => (M2Container)x).ToList();
                            foreach (var m2 in m2sToRemove)
                            {
                                sceneObjects.Remove(m2);
                                Cache.ReleaseM2(gl, m2.FileDataId, m2.ParentFileDataId);
                            }
                        }
                    }
                }
            }
        }

        private static unsafe uint MakeDefaultTexture()
        {
            var defaultTexture = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, defaultTexture);
            byte[] fill = [0, 0, 0, 0];
            fixed (byte* fillPtr = fill)
            {
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, fillPtr);
            }

            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            return defaultTexture;
        }
        private static void SwitchBlendMode(int blendType, GL gl, int alphaRefLoc)
        {
            switch (blendType)
            {
                case 0: // GxBlend_Opaque
                    gl.Disable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    break;
                case 1: // GxBlend_AlphaKey
                    gl.Disable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, 0.90393700787f);
                    break;
                case 2: // GxBlend_Alpha
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    break;
                case 3: // GxBlend_Add
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.One, BlendingFactor.Zero, BlendingFactor.One);
                    break;
                case 4: // GxBlend_Mod
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.DstColor, BlendingFactor.Zero, BlendingFactor.DstAlpha, BlendingFactor.Zero);
                    break;
                case 5: // GxBlend_Mod2x
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.DstColor, BlendingFactor.SrcColor, BlendingFactor.DstAlpha, BlendingFactor.SrcAlpha);
                    break;
                case 6: // GxBlend_ModAdd
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.DstColor, BlendingFactor.One, BlendingFactor.DstAlpha, BlendingFactor.One);
                    break;
                case 7: // GxBlend_InvSrcAlphaAdd
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One);
                    break;
                case 8: // GxBlend_InvSrcAlphaOpaque
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.OneMinusSrcAlpha, BlendingFactor.Zero, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.Zero);
                    break;
                case 9: // GxBlend_SrcAlphaOpaque
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.Zero, BlendingFactor.SrcAlpha, BlendingFactor.Zero);
                    break;
                case 10: // GxBlend_NoAlphaAdd
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.One, BlendingFactor.One, BlendingFactor.Zero, BlendingFactor.One);
                    break;
                case 11: // GxBlend_ConstantAlpha
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.ConstantAlpha, BlendingFactor.OneMinusConstantAlpha, BlendingFactor.ConstantAlpha, BlendingFactor.OneMinusConstantAlpha);
                    break;
                case 12: // GxBlend_Screen
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.OneMinusDstColor, BlendingFactor.One, BlendingFactor.One, BlendingFactor.Zero);
                    break;
                case 13: // GxBlendAdd
                    gl.Enable(EnableCap.Blend);
                    gl.Uniform1(alphaRefLoc, -1.0f);
                    gl.BlendFuncSeparate(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                    break;
                default:
                    throw new Exception("Unsupport blend mode: " + blendType);
            }
        }
    }
}
