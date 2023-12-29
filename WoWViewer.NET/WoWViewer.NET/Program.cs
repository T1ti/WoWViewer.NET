using CASCLib;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using WoWViewer.NET.Loaders;
using WoWViewer.NET.Objects;
using WoWViewer.NET.Utils;

namespace WoWViewer.NET
{
    internal class Program
    {
        private static readonly BackgroundWorkerEx cascWorker = new();
        private static readonly BackgroundWorkerEx listfileWorker = new();

        private static bool cascLoaded = false;
        private static bool listfileLoaded = true;

        private static string progressStatus = "";
        private static int progressPCT = 0;

        private static uint basicShaderProgram;
        private static uint adtShaderProgram;
        private static uint wmoShaderProgram;
        private static uint m2ShaderProgram;

        private static bool isMouseDragging = false;

        private static GL gl;

        private static Camera activeCamera;
        private static IInputContext inputContext;
        private static List<Container3D> sceneObjects = new();
        private static IWindow window;
        private static Vector2 LastMousePosition;

        static void Main(string[] args)
        {
            cascWorker.DoWork += CASCworker_DoWork;
            cascWorker.RunWorkerCompleted += CASCworker_RunWorkerCompleted;
            cascWorker.ProgressChanged += CASC_ProgressChanged;
            cascWorker.WorkerReportsProgress = true;

            listfileWorker.DoWork += ListfileWorker_DoWork;
            listfileWorker.RunWorkerCompleted += ListfileWorker_RunWorkerCompleted;
            listfileWorker.ProgressChanged += CASC_ProgressChanged;
            listfileWorker.WorkerReportsProgress = true;

            var windowOptions = WindowOptions.Default;
            windowOptions.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible | ContextFlags.Debug, new APIVersion(4, 3));
            windowOptions.ShouldSwapAutomatically = false;
            windowOptions.PreferredDepthBufferBits = 32;
            windowOptions.Size = new Silk.NET.Maths.Vector2D<int>(1920, 1080);
            window = Window.Create(windowOptions);

            gl = null;
            ImGuiController imGuiController = null;

            window.Load += () =>
            {
                gl = window.CreateOpenGL();
                gl.Enable(EnableCap.DebugOutput);
                gl.Enable(EnableCap.DebugOutputSynchronous);
                imGuiController = new ImGuiController(
                    gl,
                    window,
                    inputContext = window.CreateInput()
                );

                for (int i = 0; i < inputContext.Mice.Count; i++)
                {
                    //inputContext.Mice[i].Cursor.CursorMode = CursorMode.Raw;
                    inputContext.Mice[i].MouseMove += OnMouseMove;
                    inputContext.Mice[i].Scroll += OnMouseWheel;
                }

                cascWorker.RunWorkerAsync();

                var err = gl.GetError();
                if (err != GLEnum.NoError)
                {
                    Console.WriteLine("Render GL Error: " + err);
                }

                var compiler = new ShaderCompiler(gl);

                adtShaderProgram = compiler.CompileShader("adt");
                wmoShaderProgram = compiler.CompileShader("wmo");
                m2ShaderProgram = compiler.CompileShader("m2");
                basicShaderProgram = compiler.CompileShader("basic");

                activeCamera = new Camera(Vector3.UnitZ * 6, Vector3.UnitX, Vector3.UnitZ * -1, window.Size.X / window.Size.Y);

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

            };

            window.FramebufferResize += s =>
            {
                gl.Viewport(s);
            };

            window.Update += delta =>
            {
                var primaryKeyboard = inputContext.Keyboards.FirstOrDefault();

                var baseMoveSpeed = 5.0f;

                if (primaryKeyboard.IsKeyPressed(Key.ShiftLeft))
                    baseMoveSpeed *= 2.0f;

                var moveSpeed = baseMoveSpeed * (float)delta;

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
                    activeCamera.Position += Vector3.Normalize(Vector3.Cross(activeCamera.Front, activeCamera.Up)) * (moveSpeed * 4.0f);
                }
                if (primaryKeyboard.IsKeyPressed(Key.D))
                {
                    //Move right
                    activeCamera.Position -= Vector3.Normalize(Vector3.Cross(activeCamera.Front, activeCamera.Up)) * (moveSpeed * 4.0f);
                }

                if (primaryKeyboard.IsKeyPressed(Key.Up))
                    activeCamera.Position -= moveSpeed * activeCamera.Up;

                if (primaryKeyboard.IsKeyPressed(Key.Down))
                    activeCamera.Position += moveSpeed * activeCamera.Up;

                if (primaryKeyboard.IsKeyPressed(Key.R))
                {
                    activeCamera.Position = Vector3.One;
                }
            };

            window.Render += delta =>
            {
                var err = gl.GetError();
                if (err != GLEnum.NoError)
                {
                    Console.WriteLine("Window Render GL Error: " + err);
                }

                gl.Enable(EnableCap.DepthTest);

                gl.ClearColor(1.0f, 1.0f, 1.0f, 1.0f);
                gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                if (cascLoaded && listfileLoaded && sceneObjects.Count == 0)
                {
                    var m2 = M2Loader.LoadM2(gl, 397940, m2ShaderProgram);
                    var m2Container = new M2Container(m2, "axistestobject.m2");
                    sceneObjects.Add(m2Container);

                    var wmo = WMOLoader.LoadWMO(gl, 106685, wmoShaderProgram);
                    var wmoContainer = new WMOContainer(wmo, "test.wmo");
                    sceneObjects.Add(wmoContainer);

                    Console.WriteLine("loaded model");
                }

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

                    var newPos = activeCamera.Position;
                    ImGui.DragFloat3("Camera position", ref newPos);
                    activeCamera.Position = newPos;

                    var newFront = activeCamera.Front;
                    ImGui.DragFloat3("Camera front", ref newFront);
                    activeCamera.Front = newFront;

                    var yaw = activeCamera.Yaw;
                    ImGui.DragFloat("Camera yaw", ref yaw);
                    activeCamera.Yaw = yaw;

                    var pitch = activeCamera.Pitch;
                    ImGui.DragFloat("Camera pitch", ref pitch);
                    activeCamera.Pitch = pitch;

                    var roll = activeCamera.Roll;
                    ImGui.DragFloat("Camera roll", ref roll);
                    activeCamera.Roll = roll;

                    var modelviewMatrix = Matrix4x4.CreateRotationZ(MathF.PI / 180f * 90f);
                    ImGuiExtensions.DrawMatrix4x4("Modelview matrix", modelviewMatrix);

                    var rotationMatrix = activeCamera.GetViewMatrix();
                    ImGuiExtensions.DrawMatrix4x4("Rotation matrix", rotationMatrix);

                    var projectionMatrix = activeCamera.GetProjectionMatrix();
                    ImGuiExtensions.DrawMatrix4x4("Projection matrix", projectionMatrix);

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

        private static void ListfileWorker_DoWork(object? sender, DoWorkEventArgs e)
        {
            listfileWorker.ReportProgress(0, "Loading listfile..");
            if (!File.Exists("listfile.csv"))
            {
                listfileWorker.ReportProgress(20, "Downloading listfile..");
                Listfile.Update();
            }
            else if (DateTime.Now.AddDays(-7) > File.GetLastWriteTime("listfile.csv"))
            {
                listfileWorker.ReportProgress(20, "Updating listfile..");
                Listfile.Update();
            }

            listfileWorker.ReportProgress(60, "Loading listfile from disk..");
            try
            {
                if (Listfile.FDIDToFilename.Count == 0)
                    Listfile.Load();
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Error loading listfile: " + ex.Message);
            }
        }

        private static unsafe void ListfileWorker_RunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
        {
            listfileLoaded = true;
        }

        private static void CASC_ProgressChanged(object? sender, ProgressChangedEventArgs e)
        {
            var state = (string)e.UserState;

            if (!string.IsNullOrEmpty(state))
                progressStatus = state;

            progressPCT = e.ProgressPercentage;
        }

        private static void CASCworker_RunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
        {
            cascLoaded = true;
            //listfileWorker.RunWorkerAsync();
        }

        private static void CASCworker_DoWork(object? sender, DoWorkEventArgs e)
        {
            //var basedir = ConfigurationManager.AppSettings["basedir"];
            //var program = ConfigurationManager.AppSettings["program"];
            var basedir = @"C:\World of Warcraft\";
            var program = "wow";

            if (Directory.Exists(basedir))
            {
                cascWorker.ReportProgress(0, "Loading WoW from disk..");
                try
                {
                    WoWFormatLib.Utils.CASC.InitCasc(cascWorker, basedir, program);
                }
                catch (Exception exception)
                {
                    Console.WriteLine("CASCWorker: Exception from {0} during CASC startup: {1}", exception.Source, exception.Message);
                }
            }
            else
            {
                cascWorker.ReportProgress(0, "Loading WoW from web..");
                try
                {
                    WoWFormatLib.Utils.CASC.InitCasc(cascWorker, null, program);
                }
                catch (Exception exception)
                {
                    Console.WriteLine("CASCWorker: Exception from {0} during CASC startup: {1}", exception.Source, exception.Message);
                }
            }
        }

        private static unsafe void OnMouseMove(IMouse mouse, Vector2 position)
        {
            if (!mouse.IsButtonPressed(MouseButton.Left) || ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow))
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
            activeCamera.ModifyZoom(scrollWheel.Y);
        }

        private static unsafe void RenderScene()
        {
            var err = gl.GetError();
            if (err != GLEnum.NoError)
            {
                Console.WriteLine("Render Scene GL Error: " + err);
            }

            foreach (var sceneObject in sceneObjects)
            {
                if (sceneObject is M2Container activeM2)
                {
                    var m2 = activeM2.DoodadBatch;

                    gl.UseProgram(m2ShaderProgram);

                    int modelview_location = gl.GetUniformLocation(m2ShaderProgram, "modelview_matrix");
                    var modelviewMatrix = Matrix4x4.CreateRotationZ(MathF.PI / 180f * 90f);
                    gl.UniformMatrix4(modelview_location, 1, false, (float*)&modelviewMatrix);

                    int rotation_location = gl.GetUniformLocation(m2ShaderProgram, "rotation_matrix");
                    var rotationMatrix = activeCamera.GetViewMatrix();
                    gl.UniformMatrix4(rotation_location, 1, false, (float*)&rotationMatrix);

                    int projection_location = gl.GetUniformLocation(m2ShaderProgram, "projection_matrix");
                    var projectionMatrix = activeCamera.GetProjectionMatrix();
                    gl.UniformMatrix4(projection_location, 1, false, (float*)&projectionMatrix);

                    var alphaRefLoc = gl.GetUniformLocation(m2ShaderProgram, "alphaRef");

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
                            default:
                                gl.Disable(EnableCap.Blend);
                                gl.Uniform1(alphaRefLoc, -1.0f);
                                break;
                        }

                        gl.BindTexture(TextureTarget.Texture2D, submesh.material);

                        gl.DrawElements(PrimitiveType.Triangles, submesh.numFaces, DrawElementsType.UnsignedInt, (void*)(submesh.firstFace * 4));
                    }
                }
                else if (sceneObject is WMOContainer wmoContainer)
                {
                    var wmo = wmoContainer.WorldModel;

                    gl.UseProgram(wmoShaderProgram);

                    int modelview_location = gl.GetUniformLocation(m2ShaderProgram, "modelview_matrix");
                    var modelviewMatrix = Matrix4x4.CreateRotationZ(MathF.PI / 180f * -180f);
                    gl.UniformMatrix4(modelview_location, 1, false, (float*)&modelviewMatrix);

                    int rotation_location = gl.GetUniformLocation(m2ShaderProgram, "rotation_matrix");
                    var rotationMatrix = activeCamera.GetViewMatrix();
                    gl.UniformMatrix4(rotation_location, 1, false, (float*)&rotationMatrix);

                    int projection_location = gl.GetUniformLocation(m2ShaderProgram, "projection_matrix");
                    var projectionMatrix = activeCamera.GetProjectionMatrix();
                    gl.UniformMatrix4(projection_location, 1, false, (float*)&projectionMatrix);

                    var alphaRefLoc = gl.GetUniformLocation(wmoShaderProgram, "alphaRef");

                    for (var j = 0; j < wmo.wmoRenderBatch.Length; j++)
                    {
                        gl.BindVertexArray(wmo.groupBatches[wmo.wmoRenderBatch[j].groupID].vao);

                        switch (wmo.wmoRenderBatch[j].blendType)
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
                            default:
                                gl.Disable(EnableCap.Blend);
                                gl.Uniform1(alphaRefLoc, -1.0f);
                                break;
                        }

                        gl.BindTexture(TextureTarget.Texture2D, wmo.wmoRenderBatch[j].materialID[0]);
                        gl.DrawElements(PrimitiveType.Triangles, wmo.wmoRenderBatch[j].numFaces, DrawElementsType.UnsignedInt, (void*)(wmo.wmoRenderBatch[j].firstFace * 4));
                    }
                }
            }

            gl.BindVertexArray(0);
        }
    }
}
