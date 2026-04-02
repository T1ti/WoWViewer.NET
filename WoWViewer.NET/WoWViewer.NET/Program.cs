using CASCLib;
using Evergine.Bindings.RenderDoc;
using ImGuiNET;
using SceneScriptLib;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using TACTSharp;
using WoWFormatLib.FileProviders;
using WoWViewer.NET.Objects;
using WoWViewer.NET.Renderer;

namespace WoWViewer.NET
{
    internal class Program
    {
        private static bool cascLoaded = false;
        private static bool listfileLoaded = false;

        private static string progressStatus = "";
        private static int progressPCT = 0;

        private static uint adtShaderProgram;
        private static uint wmoShaderProgram;
        private static uint m2ShaderProgram;

        private static float movementSpeed = 50f;
        private static bool isMouseDragging = false;
        private static bool hasFocus = true;

        public static GL gl;

        private static Camera activeCamera;
        private static IInputContext inputContext;
        private static List<Container3D> sceneObjects = new();
        private static IWindow window;
        private static Vector2 LastMousePosition;

        private static bool renderWMO = true;
        private static bool renderM2 = false;
        private static bool shadersReady = false;

        private static Dictionary<string, DateTime> shaderMTimes = new();

        private static List<TimelineScene> scenes = new();

        private static uint defaultTextureID;

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
        static void Main(string[] args)
        {
            var windowOptions = WindowOptions.Default;
            windowOptions.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible | ContextFlags.Debug, new APIVersion(4, 3));
            windowOptions.ShouldSwapAutomatically = false;
            windowOptions.Size = new Vector2D<int>(1920, 1080);
            windowOptions.Title = "WoWViewer.NET";
            window = Window.Create(windowOptions);

            RenderDoc.Load(out RenderDoc renderDoc);

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

                //ImGui.DockSpaceOverViewport(ImGui.GetMainViewport());

                for (int i = 0; i < inputContext.Mice.Count; i++)
                {
                    //inputContext.Mice[i].Cursor.CursorMode = CursorMode.Raw;
                    inputContext.Mice[i].MouseMove += OnMouseMove;
                    inputContext.Mice[i].Scroll += OnMouseWheel;
                }

                var tactFileProvider = new TACTSharpFileProvider();

                var buildInstance = new BuildInstance();
                buildInstance.Settings.Product = "wowt";

                buildInstance.Settings.Locale = RootInstance.LocaleFlags.enUS;
                buildInstance.Settings.Region = "us";
                buildInstance.Settings.RootMode = RootInstance.LoadMode.Normal;

                var buildConfig = "f8891ed6ab01b319b43b9aa0ceeb5f58";
                var cdnConfig = "99cd3ee53f0b63144232eef9ff25fc06";

                var basedir = @"C:\World of Warcraft\";
                if (Directory.Exists(basedir))
                {
                    buildInstance.Settings.BaseDir = basedir;
                    buildInstance.Settings.BuildConfig = buildConfig;
                    buildInstance.Settings.CDNConfig = cdnConfig;
                }

                buildInstance.LoadConfigs(buildConfig, cdnConfig);
                if (buildInstance.BuildConfig == null || buildInstance.CDNConfig == null)
                    throw new Exception("Failed to load build configs");

                buildInstance.Load();

                if (buildInstance.Encoding == null || buildInstance.Root == null || buildInstance.Install == null || buildInstance.GroupIndex == null)
                    throw new Exception("Failed to load build components");

                tactFileProvider.InitTACT(buildInstance);

                FileProvider.SetDefaultBuild(TACTSharpFileProvider.BuildName);
                FileProvider.SetProvider(tactFileProvider, TACTSharpFileProvider.BuildName);

                cascLoaded = true;

                Console.WriteLine("Loading listfile..");
                if (!File.Exists("listfile.csv"))
                {
                    Listfile.Update();
                }
                else if (DateTime.Now.AddDays(-7) > File.GetLastWriteTime("listfile.csv"))
                {
                    Listfile.Update();
                }

                try
                {
                    if (Listfile.FDIDToFilename.Count == 0)
                        Listfile.Load();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Error loading listfile: " + ex.Message);
                }

                listfileLoaded = true;

                var err = gl.GetError();
                if (err != GLEnum.NoError)
                {
                    Console.WriteLine("Render GL Error: " + err);
                }

                adtShaderProgram = ShaderCompiler.CompileShader("adt");
                wmoShaderProgram = ShaderCompiler.CompileShader("wmo");
                m2ShaderProgram = ShaderCompiler.CompileShader("m2");
                shadersReady = true;


                // sw new Vector3(-8938, 625, 200)
                // 32 new Vector3(0, 0, 200)
                // amird new Vector3(-138, 8208, 200)
                activeCamera = new Camera(new Vector3(-29.472f, 33.547f, 32.624f), Vector3.UnitX, Vector3.UnitZ * -1, (float)window.FramebufferSize.X / (float)window.FramebufferSize.Y);
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

                //foreach (var file in Directory.GetFiles("G:\\NewmansLandingProject\\scenes\\10.0.0 Newman's Landing Machinima\\", "*.lua"))
                //{
                //    Console.WriteLine(Path.GetFileNameWithoutExtension(file));
                //    var pathFileName = Path.GetFileNameWithoutExtension(file);
                //    if (pathFileName.Contains("Documentation"))
                //        continue;

                //    var contents = File.ReadAllText(file);
                //    var script = SceneScriptReader.ParseTimelineScript(contents);
                //    scenes.Add(script);

                //}

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
                {
                    //Move forwards
                    activeCamera.Position += moveSpeed * activeCamera.Front;
                }
                if (primaryKeyboard.IsKeyPressed(Key.S))
                {
                    //Move backwards
                    activeCamera.Position -= moveSpeed * activeCamera.Front;
                }
                if (primaryKeyboard.IsKeyPressed(Key.A))
                {
                    //Move left
                    activeCamera.Position += Vector3.Normalize(Vector3.Cross(activeCamera.Front, activeCamera.Up)) * moveSpeed;
                }
                if (primaryKeyboard.IsKeyPressed(Key.D))
                {
                    //Move right
                    activeCamera.Position -= Vector3.Normalize(Vector3.Cross(activeCamera.Front, activeCamera.Up)) * moveSpeed;
                }

                if (primaryKeyboard.IsKeyPressed(Key.Up))
                    activeCamera.Position -= moveSpeed * activeCamera.Up;

                if (primaryKeyboard.IsKeyPressed(Key.Down))
                    activeCamera.Position += moveSpeed * activeCamera.Up;

                if (primaryKeyboard.IsKeyPressed(Key.R))
                {
                    activeCamera.Position = Vector3.One;
                }

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
            };

            window.Render += delta =>
            {
                if (!hasFocus || !shadersReady)
                    return;

                var err = gl.GetError();
                if (err != GLEnum.NoError)
                {
                    Console.WriteLine("Window Render GL Error: " + err);
                }

                gl.Enable(EnableCap.DepthTest);

                gl.ClearColor(0f, 0f, 0f, 0.5f);
                gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                if (cascLoaded && listfileLoaded && sceneObjects.Count == 0)
                {
                    var m2Container = new M2Container(gl, 397940, m2ShaderProgram);
                    sceneObjects.Add(m2Container);

                    //foreach(var scene in scenes)
                    //{
                    //    foreach(var actor in scene.actors)
                    //    {
                    //        if(actor.Value.properties.Transform != null && actor.Value.properties.Transform?.events != null)
                    //        {
                    //            foreach (var transformEvent in actor.Value.properties.Transform.Value.events)
                    //            {
                    //                // {<-8855. 584. 145.854>}
                    //                var m2Container = new M2Container(gl, 1109379, m2ShaderProgram);
                    //                m2Container.Position = new Vector3(transformEvent.Value.Position.Y, transformEvent.Value.Position.Z, transformEvent.Value.Position.X);
                    //                m2Container.Rotation = new Vector3(0f, transformEvent.Value.Yaw, 0f);
                    //                m2Container.forceRender = true;
                    //                m2Container.Scale = 100f;
                    //                sceneObjects.Add(m2Container);
                    //                Console.WriteLine("Spawning camera at " + transformEvent.Value.Position);
                    //            }
                    //        }
                    //    }
                    //}

                    //var wmoContainer = new WMOContainer(gl, 2756726, wmoShaderProgram);
                    //var wmoContainer = new WMOContainer(gl, 5386825, wmoShaderProgram);
                    var wmoContainer = new WMOContainer(gl, 3924066, wmoShaderProgram);
                    wmoContainer.Position = new Vector3(34f, 17f, 42f);
                    wmoContainer.Scale = 1f;
                    sceneObjects.Add(wmoContainer);

                    var usedUUIDs = new List<uint>();

                    // sw 29 47
                    // amird 16 32
                    // valdr 33 31
                    byte startX = 33;
                    byte startY = 31;
                    /*
                      for (byte x = startX; x < startX + 3; x++)
                      {
                          for (byte y = startY; y < startY + 3; y++)
                          {
                              var mapTile = new Structs.MapTile();
                              mapTile.tileX = x;
                              mapTile.tileY = y;
                              //mapTile.wdtFileDataID = 4914790;
                              //mapTile.wdtFileDataID = 5339421; // amidr
                              mapTile.wdtFileDataID = 3694921; // valdr
                              var adt = Loaders.ADTLoader.LoadADT(gl, mapTile, adtShaderProgram, true);
                              var adtContainer = new ADTContainer(gl, adt, mapTile.wdtFileDataID, adtShaderProgram);
                              sceneObjects.Add(adtContainer);

                              foreach (var worldModel in adt.worldModelBatches)
                              {
                                  if (usedUUIDs.Contains(worldModel.uniqueID))
                                      continue;
                                  var worldModelContainer = new WMOContainer(gl, worldModel.fileDataID, wmoShaderProgram);
                                  worldModelContainer.Position = worldModel.position;
                                  worldModelContainer.Rotation = worldModel.rotation;
                                  worldModelContainer.Scale = worldModel.scale;
                                  sceneObjects.Add(worldModelContainer);
                                  usedUUIDs.Add(worldModel.uniqueID);
                              }

                              //foreach (var doodad in adt.doodads)
                              //{
                              //    var doodadContainer = new M2Container(gl, doodad.fileDataID, m2ShaderProgram);
                              //    doodadContainer.Position = doodad.position;
                              //    doodadContainer.Rotation = doodad.rotation;
                              //    doodadContainer.Scale = doodad.scale;
                              //    sceneObjects.Add(doodadContainer);
                              //}
                          }
                      }
                   */
                    Console.WriteLine("loaded model");
                }

                ImGUIDockSpace();
                if (!cascLoaded || !listfileLoaded)
                {
                    if (!cascLoaded)
                        ImGui.Begin("Loading CASC filesystem");
                    else if (!listfileLoaded)
                        ImGui.Begin("Loading listfile");

                    ImGui.ProgressBar(progressPCT / 100f, new Vector2(300, 0));
                    ImGui.Text(progressStatus);
                    ImGui.End();
                }
                else
                {
                    ImGui.Begin("3D debug");

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

                    //var modelviewMatrix = Matrix4x4.CreateRotationZ(MathF.PI / 180f * 90f);
                    //ImGuiExtensions.DrawMatrix4x4("Modelview matrix", modelviewMatrix);

                    //var rotationMatrix = activeCamera.GetViewMatrix();
                    //ImGuiExtensions.DrawMatrix4x4("Rotation matrix", rotationMatrix);

                    //var projectionMatrix = activeCamera.GetProjectionMatrix();
                    //ImGuiExtensions.DrawMatrix4x4("Projection matrix", projectionMatrix);

                    ImGui.Text(sceneObjects.Count.ToString() + " loaded objects (" + sceneObjects.Count(x => x is M2Container).ToString() + " M2, " + sceneObjects.Count(x => x is WMOContainer).ToString() + " WMO, " + sceneObjects.Count(x => x is ADTContainer).ToString() + " ADT)");

                    var i = 0;
                    if (ImGui.CollapsingHeader("Loaded WMOs"))
                    {
                        foreach (var sceneObject in sceneObjects.Where(x => x is WMOContainer))
                        {
                            var wmoContainer = (WMOContainer)sceneObject;
                            var wmoString = "WMO #" + i + " FDID " + wmoContainer.FileDataId.ToString() + " " + Listfile.FDIDToFilename[wmoContainer.FileDataId];

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
                            if (ImGui.CollapsingHeader("M2 #" + i + " FDID " + m2Container.FileDataId.ToString() + " " + Listfile.FDIDToFilename[m2Container.FileDataId]))
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

        private static unsafe void OnMouseMove(IMouse mouse, Vector2 position)
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
                var yOffset = (position.Y - LastMousePosition.Y) * lookSensitivity;
                LastMousePosition = position;

                activeCamera.ModifyDirection(xOffset, yOffset);
            }
        }
        private static unsafe void OnMouseWheel(IMouse mouse, ScrollWheel scrollWheel)
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

            var heightScaleLoc = gl.GetUniformLocation(adtShaderProgram, "pc_heightScale");
            var heightOffsetLoc = gl.GetUniformLocation(adtShaderProgram, "pc_heightOffset");


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

                    var m2 = Cache.GetOrLoadM2(gl, activeM2.FileDataId, m2ShaderProgram);

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

                    var alphaRefLoc = gl.GetUniformLocation(m2ShaderProgram, "alphaRef");
                    gl.ActiveTexture(TextureUnit.Texture0);

                    gl.BindVertexArray(m2.vao);

                    for (var i = 0; i < m2.submeshes.Length; i++)
                    {
                        var submesh = m2.submeshes[i];
                        if (!activeM2.EnabledGeosets[i])
                            continue;

                        switch (submesh.blendType)
                        {
                            case 0:
                                gl.Disable(EnableCap.Blend);
                                gl.Uniform1(alphaRefLoc, -1.0f);
                                break;
                            case 1:
                                gl.Disable(EnableCap.Blend);
                                gl.Uniform1(alphaRefLoc, 0.90393700787f);
                                break;
                            case 2:
                                gl.Enable(EnableCap.Blend);
                                gl.Uniform1(alphaRefLoc, -1.0f);
                                gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                                break;
                            case 3:
                                gl.Enable(EnableCap.Blend);
                                gl.Uniform1(alphaRefLoc, -1.0f);
                                gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.One, BlendingFactor.Zero, BlendingFactor.One);
                                break;
                            case 4:
                                gl.Enable(EnableCap.Blend);
                                gl.Uniform1(alphaRefLoc, -1.0f);
                                gl.BlendFuncSeparate(BlendingFactor.DstColor, BlendingFactor.Zero, BlendingFactor.DstAlpha, BlendingFactor.Zero);
                                break;
                            case 7:
                                gl.Enable(EnableCap.Blend);
                                gl.Uniform1(alphaRefLoc, -1.0f);
                                gl.BlendFuncSeparate(BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One);
                                break;
                            default:
                                gl.Disable(EnableCap.Blend);
                                gl.Uniform1(alphaRefLoc, -1.0f);
                                break;
                        }

                        gl.BindTexture(TextureTarget.Texture2D, submesh.material);

                        gl.DrawElements(PrimitiveType.Triangles, submesh.numFaces, DrawElementsType.UnsignedInt, (void*)(submesh.firstFace * 4));
                    }

                    var err = gl.GetError();
                    if (err != GLEnum.NoError)
                        Console.WriteLine("M2 render GL Error: " + err);
                }
                else if (sceneObject is WMOContainer wmoContainer)
                {
                    if (!renderWMO)
                        continue;

                    var wmo = Cache.GetOrLoadWMO(gl, sceneObject.FileDataId, wmoShaderProgram);

                    gl.UseProgram(wmoShaderProgram);

                    gl.UniformMatrix4(wmoProjLocation, 1, false, (float*)&projectionMatrix);

                    var viewMatrix = activeCamera.GetViewMatrix();
                    viewMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * 180f);

                    gl.UniformMatrix4(wmoViewLocation, 1, false, (float*)&viewMatrix);

                    // Model matrix contains position, rotation and scale
                    var modelMatrix = Matrix4x4.CreateScale(sceneObject.Scale);

                    // Apply ADT rotation
                    modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * (wmoContainer.Rotation.Y - 270f));

                    modelMatrix *= Matrix4x4.CreateTranslation(wmoContainer.Position.X, wmoContainer.Position.Z * -1, wmoContainer.Position.Y);

                    // Post-transform rotation
                    modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * -270f);

                    gl.UniformMatrix4(wmoModelLocation, 1, false, (float*)&modelMatrix);

                    var alphaRefLoc = gl.GetUniformLocation(wmoShaderProgram, "alphaRef");

                    for (var j = 0; j < wmo.wmoRenderBatch.Length; j++)
                    {
                        if (wmo.groupBatches[wmo.wmoRenderBatch[j].groupID].vao == 0)
                            continue;

                        var firstFace = wmo.wmoRenderBatch[j].firstFace;
                        var numFaces = wmo.wmoRenderBatch[j].numFaces;

                        gl.BindVertexArray(wmo.groupBatches[wmo.wmoRenderBatch[j].groupID].vao);

                        gl.Uniform1(wmoVertexShaderIDLocation, (float)ShaderEnums.WMOShaders[(int)wmo.wmoRenderBatch[j].shader].VertexShader);
                        gl.Uniform1(wmoPixelShaderIDLocation, (float)ShaderEnums.WMOShaders[(int)wmo.wmoRenderBatch[j].shader].PixelShader);

                        switch (wmo.wmoRenderBatch[j].blendType)
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
                                throw new Exception("Unsupport blend mode: " + wmo.wmoRenderBatch[j].blendType);
                        }

                        for (var m = 0; m < wmo.wmoRenderBatch[j].materialID.Length; m++)
                        {
                            gl.ActiveTexture(TextureUnit.Texture0 + m);
                            if (wmo.wmoRenderBatch[j].materialID[m] == -1)
                                gl.BindTexture(TextureTarget.Texture2D, defaultTextureID);
                            else
                                gl.BindTexture(TextureTarget.Texture2D, (uint)wmo.wmoRenderBatch[j].materialID[m]);
                        }

                        gl.DrawElements(PrimitiveType.Triangles, numFaces, DrawElementsType.UnsignedShort, (void*)(firstFace * 2));
                    }

                    var err = gl.GetError();
                    if (err != GLEnum.NoError)
                        Console.WriteLine("WMO render GL Error: " + err);
                }
                else if (sceneObject is ADTContainer adt)
                {
                    gl.UseProgram(adtShaderProgram);

                    var modelviewMatrix = Matrix4x4.CreateRotationZ(MathF.PI / 180f * 180f);
                    gl.UniformMatrix4(adtModelLocation, 1, false, (float*)&modelviewMatrix);

                    var rotationMatrix = activeCamera.GetViewMatrix();
                    gl.UniformMatrix4(adtRotLocation, 1, false, (float*)&rotationMatrix);
                    gl.UniformMatrix4(adtProjLocation, 1, false, (float*)&projectionMatrix);

                    gl.BindVertexArray(adt.Terrain.vao);
                    gl.Disable(EnableCap.Blend);
                    for (int i = 0; i < adt.Terrain.renderBatches.Length; i++)
                    {
                        gl.Uniform4(heightScaleLoc, adt.Terrain.renderBatches[i].heightScales);
                        gl.Uniform4(heightOffsetLoc, adt.Terrain.renderBatches[i].heightOffsets);

                        for (int j = 0; j < adt.Terrain.renderBatches[i].materialID.Length; j++)
                        {
                            var textureLoc = gl.GetUniformLocation(adtShaderProgram, "pt_layer" + j);
                            gl.Uniform1(textureLoc, j);

                            var scaleLoc = gl.GetUniformLocation(adtShaderProgram, "layer" + j + "scale");
                            gl.Uniform1(scaleLoc, adt.Terrain.renderBatches[i].scales[j]);

                            gl.ActiveTexture(TextureUnit.Texture0 + j);
                            gl.BindTexture(TextureTarget.Texture2D, (uint)adt.Terrain.renderBatches[i].materialID[j]);
                        }

                        for (int j = 1; j < adt.Terrain.renderBatches[i].alphaMaterialID.Length; j++)
                        {
                            var textureLoc = gl.GetUniformLocation(adtShaderProgram, "pt_blend" + j);
                            gl.Uniform1(textureLoc, 3 + j);

                            gl.ActiveTexture(TextureUnit.Texture3 + j);
                            gl.BindTexture(TextureTarget.Texture2D, (uint)adt.Terrain.renderBatches[i].alphaMaterialID[j]);
                        }

                        for (int j = 0; j < adt.Terrain.renderBatches[i].heightMaterialIDs.Length; j++)
                        {
                            var textureLoc = gl.GetUniformLocation(adtShaderProgram, "pt_height" + j);
                            gl.Uniform1(textureLoc, 7 + j);

                            gl.ActiveTexture(TextureUnit.Texture7 + j);
                            gl.BindTexture(TextureTarget.Texture2D, (uint)adt.Terrain.renderBatches[i].heightMaterialIDs[j]);
                        }

                        gl.DrawElements(PrimitiveType.Triangles, adt.Terrain.renderBatches[i].numFaces, DrawElementsType.UnsignedInt, (void*)(adt.Terrain.renderBatches[i].firstFace * 4));

                        for (int j = 0; j < 11; j++)
                        {
                            gl.ActiveTexture(TextureUnit.Texture0 + j);
                            gl.BindTexture(TextureTarget.Texture2D, 0);
                        }

                        gl.DrawRangeElements(PrimitiveType.Triangles, adt.Terrain.renderBatches[i].firstFace, adt.Terrain.renderBatches[i].firstFace + adt.Terrain.renderBatches[i].numFaces, adt.Terrain.renderBatches[i].numFaces, DrawElementsType.UnsignedInt, (void*)(adt.Terrain.renderBatches[i].firstFace * 4));
                    }

                    var err = gl.GetError();
                    if (err != GLEnum.NoError)
                        Console.WriteLine("ADT render GL Error: " + err);
                }
            }

            gl.BindVertexArray(0);
        }
    }
}
