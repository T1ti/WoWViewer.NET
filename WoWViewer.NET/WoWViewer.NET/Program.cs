using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.Hexa.ImGui;
using Silk.NET.Windowing;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using WoWFormatLib.FileProviders;
using WoWFormatLib.Structs.M2;
using WoWViewer.NET.Managers;
using WoWViewer.NET.Objects;

namespace WoWViewer.NET
{
    internal class Program
    {
        private static bool cascLoaded = false;

        private static uint adtShaderProgram;
        private static uint wmoShaderProgram;
        private static uint m2ShaderProgram;
        private static uint debugShaderProgram;

        private static float movementSpeed = 150f;
        private static bool hasFocus = true;

        public static GL gl;

        private static Camera activeCamera;
        private static IInputContext inputContext;

        private static IWindow window;
        private static Vector2 LastMousePosition;
        private static Vector2? MouseDownPosition;
        private static bool wasMouseDown = false;

        private static bool shadersReady = false;

        private static string WDTFDIDInput = "";

        private static bool gizmoWasUsing = false;
        private static bool gizmoWasOver = false;
        private static ImGuizmoOperation currentGizmoOperation = ImGuizmoOperation.Translate;
        private static bool wasSpacePressed = false;

        private static ShaderManager shaderManager;
        private static SceneManager sceneManager;
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

            window.Load += () =>
            {
                gl = window.CreateOpenGL();

                shaderManager = new ShaderManager(gl, Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Shaders"));
                sceneManager = new SceneManager(gl, shaderManager);

                imGuiController = new ImGuiController(
                    gl,
                    window,
                    inputContext = window.CreateInput(),
                    null,
                    () => ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable
                );

                ImGui.GetStyle().WindowRounding = 5.0f;
                ImGui.GetStyle().WindowPadding = new Vector2(0.0f, 0.0f);
                ImGui.GetStyle().FrameRounding = 12.0f;
                ImGuizmo.SetImGuiContext(imGuiController.Context);

                var err = gl.GetError();
                if (err != GLEnum.NoError)
                {
                    Console.WriteLine("Render GL Error: " + err);
                }

                adtShaderProgram = shaderManager.GetOrCompileShader("adt");
                wmoShaderProgram = shaderManager.GetOrCompileShader("wmo");
                m2ShaderProgram = shaderManager.GetOrCompileShader("m2");
                debugShaderProgram = shaderManager.GetOrCompileShader("debug");

                sceneManager.Initialize(shaderManager, adtShaderProgram, wmoShaderProgram, m2ShaderProgram, debugShaderProgram);
                WDTFDIDInput = sceneManager.CurrentWDTFileDataID.ToString();
                shadersReady = true;

                // Start CASC initialization in background
                Task.Run(async () =>
                {
                    await Services.CASC.Initialize();

                    var tactFileProvider = new TACTSharpFileProvider();
                    tactFileProvider.InitTACT(Services.CASC.buildInstance);
                    FileProvider.SetDefaultBuild(TACTSharpFileProvider.BuildName);
                    FileProvider.SetProvider(tactFileProvider, TACTSharpFileProvider.BuildName);

                    cascLoaded = true;
                });

                var startPos = new Vector3(5305f, -4122f, 92f);
                activeCamera = new Camera(startPos, Vector3.UnitX, Vector3.UnitZ * -1, (float)window.FramebufferSize.X / (float)window.FramebufferSize.Y);
                activeCamera.Yaw = 168f;
                activeCamera.Pitch = 13f;
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
                var primaryMouse = inputContext.Mice[0];

                var moveSpeed = movementSpeed * (float)delta;

                if (primaryKeyboard.IsKeyPressed(Key.ShiftLeft))
                    moveSpeed *= 2.0f;

                imGuiController.Update((float)delta);

                var io = ImGui.GetIO();

                bool gizmoInUse = gizmoWasUsing || gizmoWasOver;

                if (primaryMouse.IsButtonPressed(MouseButton.Left) && !io.WantCaptureMouse && !gizmoInUse)
                {
                    var currentMousePos = primaryMouse.Position;

                    if (LastMousePosition == default)
                    {
                        LastMousePosition = currentMousePos;
                    }
                    else
                    {
                        var lookSensitivity = 0.1f;
                        var xOffset = (currentMousePos.X - LastMousePosition.X) * lookSensitivity;
                        var yOffset = (LastMousePosition.Y - currentMousePos.Y) * lookSensitivity;
                        LastMousePosition = currentMousePos;

                        activeCamera.ModifyDirection(xOffset, yOffset);
                    }
                }
                else
                {
                    LastMousePosition = default;
                }

                bool mouseDownThisFrame = primaryMouse.IsButtonPressed(MouseButton.Left);
                if (mouseDownThisFrame && !wasMouseDown && !io.WantCaptureMouse && !gizmoInUse)
                {
                    MouseDownPosition = primaryMouse.Position;
                }

                if (!mouseDownThisFrame && wasMouseDown)
                {
                    if (MouseDownPosition.HasValue && !io.WantCaptureMouse)
                    {
                        var dragDistance = Vector2.Distance(MouseDownPosition.Value, primaryMouse.Position);
                        if (dragDistance < 5.0f)
                        {
                            sceneManager.PerformRaycast(primaryMouse.Position.X, primaryMouse.Position.Y, activeCamera, window.Size.X, window.Size.Y);
                        }
                    }
                    MouseDownPosition = null;
                }

                wasMouseDown = mouseDownThisFrame;

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

                bool spacePressed = primaryKeyboard.IsKeyPressed(Key.Space);
                if (spacePressed && !wasSpacePressed && sceneManager.SelectedObject != null && !gizmoWasUsing)
                {
                    currentGizmoOperation = currentGizmoOperation switch
                    {
                        ImGuizmoOperation.Translate => ImGuizmoOperation.Rotate,
                        ImGuizmoOperation.Rotate => ImGuizmoOperation.Scale,
                        ImGuizmoOperation.Scale => ImGuizmoOperation.Translate,
                        _ => ImGuizmoOperation.Translate
                    };
                    Console.WriteLine($"Gizmo mode switched to: {currentGizmoOperation}");
                }
                wasSpacePressed = spacePressed;

#if DEBUG
                // Note -- this is extremely slow but allows for shader hot-reloading
                shaderManager.CheckForChanges();
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

                ImGuizmo.BeginFrame();

                if (cascLoaded)
                    sceneManager.GetCurrentWDT();

                sceneManager.UpdateTilesByCameraPos(activeCamera.Position);

                sceneManager.ProcessNextTile();

                if (!string.IsNullOrEmpty(sceneManager.StatusMessage))
                {
                    ImGui.Begin("Loading");
                    ImGui.Text(sceneManager.StatusMessage);
                    ImGui.End();
                }

                ImGui.Begin("Map selection");

                ImGui.InputText("WDT", ref WDTFDIDInput, 100);

                if (ImGui.Button("Load WDT"))
                {
                    if (uint.TryParse(WDTFDIDInput, out var newWDTI) && sceneManager.CurrentWDTFileDataID != newWDTI && Services.CASC.FileExists(newWDTI))
                    {
                        sceneManager.LoadWDT(newWDTI);
                    }
                }
                ImGui.End();

                if (sceneManager.SceneLoaded)
                {
                    ImGui.Begin("3D debug");

                    var renderADT = sceneManager.RenderADT;
                    ImGui.Checkbox("Render ADT", ref renderADT);
                    sceneManager.RenderADT = renderADT;

                    var renderWMO = sceneManager.RenderWMO;
                    ImGui.Checkbox("Render WMO", ref renderWMO);
                    sceneManager.RenderWMO = renderWMO;

                    var renderM2 = sceneManager.RenderM2;
                    ImGui.Checkbox("Render M2", ref renderM2);
                    sceneManager.RenderM2 = renderM2;

                    if (sceneManager.SelectedObject != null)
                    {
                        ImGui.Text("Selected Object: " + sceneManager.SelectedObject.FileDataId);

                        if (ImGui.Button("Deselect"))
                        {
                            sceneManager.SelectedObject.IsSelected = false;
                            sceneManager.SelectedObject = null;
                        }
                    }

                    var showBoundingBoxes = sceneManager.ShowBoundingBoxes;
                    ImGui.Checkbox("Show Bounding Boxes", ref showBoundingBoxes);
                    sceneManager.ShowBoundingBoxes = showBoundingBoxes;

                    var showBoundingSpheres = sceneManager.ShowBoundingSpheres;
                    ImGui.Checkbox("Show Bounding Spheres", ref showBoundingSpheres);
                    sceneManager.ShowBoundingSpheres = showBoundingSpheres;

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

                    var lightDir = sceneManager.LightDirection;
                    ImGui.DragFloat3("Light direction", ref lightDir);
                    sceneManager.LightDirection = lightDir;

                    ImGui.Text(sceneManager.SceneObjects.Count.ToString() + " loaded objects (" + sceneManager.SceneObjects.Count(x => x is M2Container).ToString() + " M2, " + sceneManager.SceneObjects.Count(x => x is WMOContainer).ToString() + " WMO, " + sceneManager.SceneObjects.Count(x => x is ADTContainer).ToString() + " ADT)");

                    var (x, y) = SceneManager.GetTileFromPosition(activeCamera.Position);

                    ImGui.Text("Current ADT: " + x + ", " + y);
                    ImGui.Text("RAM usage: " + (GC.GetTotalMemory(false) / 1024 / 1024).ToString() + " MB");

                    var i = 0;
                    if (ImGui.CollapsingHeader("Loaded WMOs"))
                    {
                        foreach (var sceneObject in sceneManager.SceneObjects.Where(x => x is WMOContainer))
                        {
                            var wmoContainer = (WMOContainer)sceneObject;
                            var wmoString = "WMO #" + i + " FDID " + wmoContainer.FileDataId.ToString();

                            if (sceneObject.IsSelected)
                                wmoString += " (Selected)";

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
                        foreach (var sceneObject in sceneManager.SceneObjects.Where(x => x is M2Container))
                        {
                            var m2Container = (M2Container)sceneObject;
                            var m2String = "M2 #" + i + " FDID " + m2Container.FileDataId.ToString();

                            if (sceneObject.IsSelected)
                                m2String += " (Selected)";

                            if (ImGui.CollapsingHeader(m2String))
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

                sceneManager.RenderScene(activeCamera, out bool renderGizmoWasUsing, out bool renderGizmoWasOver);
                RenderGizmo();
                sceneManager.RenderDebug(activeCamera, out bool debugGizmoWasUsing, out bool debugGizmoWasOver);

                gizmoWasUsing = renderGizmoWasUsing || debugGizmoWasUsing;
                gizmoWasOver = renderGizmoWasOver || debugGizmoWasOver;

                imGuiController.Render();

                window.SwapBuffers();
            };

            window.Closing += () =>
            {
                shaderManager.Dispose();
                sceneManager?.Dispose();
                imGuiController?.Dispose();
                inputContext?.Dispose();
                gl?.Dispose();
            };

            window.Run();

            window.Dispose();
        }

        private static unsafe void RenderGizmo()
        {
            if (sceneManager.SelectedObject == null)
                return;

            var windowPos = new Vector2(0, 0);
            var windowSize = new Vector2(window.Size.X, window.Size.Y);

            ImGuizmo.SetDrawlist(ImGui.GetForegroundDrawList());
            ImGuizmo.Enable(true);
            ImGuizmo.SetOrthographic(false);
            ImGuizmo.SetRect(windowPos.X, windowPos.Y, windowSize.X, windowSize.Y);

            var view = activeCamera.GetViewMatrix();
            view *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * 180f);
            var proj = activeCamera.GetProjectionMatrix();

            var sceneObject = sceneManager.SelectedObject;
            var transform = Matrix4x4.CreateScale(sceneObject.Scale);
            transform *= Matrix4x4.CreateRotationX(MathF.PI / 180f * sceneObject.Rotation.X);
            transform *= Matrix4x4.CreateRotationY(MathF.PI / 180f * -sceneObject.Rotation.Z);
            transform *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * (sceneObject.Rotation.Y - 270f));
            transform *= Matrix4x4.CreateTranslation(sceneObject.Position.X, sceneObject.Position.Z * -1, sceneObject.Position.Y);
            transform *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * -270f);

            ImGuizmo.DrawGrid(ref view, ref proj, ref transform, 10);
            ImGuizmo.DrawCubes(ref view, ref proj, ref transform, 1);

            ImGuizmo.PushID(0);

            if (ImGuizmo.Manipulate(ref view, ref proj, currentGizmoOperation, ImGuizmoMode.Local, ref transform))
            {
                var inversePostRot = Matrix4x4.CreateRotationZ(MathF.PI / 180f * 270f);
                var unrotated = transform * inversePostRot;

                if (currentGizmoOperation == ImGuizmoOperation.Translate)
                {
                    var newPosition = new Vector3(unrotated.M41, unrotated.M43, -unrotated.M42);
                    sceneObject.Position = newPosition;
                }
                else if (currentGizmoOperation == ImGuizmoOperation.Rotate)
                {
                    if (Matrix4x4.Decompose(unrotated, out _, out Quaternion rotation, out _))
                    {
                        var euler = QuaternionToEuler(rotation);
                        var xRotationDegrees = euler.X * (180f / MathF.PI);
                        var yRotationDegrees = euler.Y * (180f / MathF.PI);
                        var zRotationDegrees = euler.Z * (180f / MathF.PI);
                        sceneObject.Rotation = new Vector3(
                            xRotationDegrees,
                            zRotationDegrees + 270f,
                            -yRotationDegrees
                        );
                    }
                }
                else if (currentGizmoOperation == ImGuizmoOperation.Scale)
                {
                    var scaleX = new Vector3(unrotated.M11, unrotated.M12, unrotated.M13).Length();
                    var scaleY = new Vector3(unrotated.M21, unrotated.M22, unrotated.M23).Length();
                    var scaleZ = new Vector3(unrotated.M31, unrotated.M32, unrotated.M33).Length();
                    sceneObject.Scale = (scaleX + scaleY + scaleZ) / 3f;
                }
            }

            ImGuizmo.PopID();
        }

        private static Vector3 QuaternionToEuler(Quaternion q)
        {
            Vector3 euler;

            float sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
            float cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
            euler.X = MathF.Atan2(sinr_cosp, cosr_cosp);

            float sinp = 2 * (q.W * q.Y - q.Z * q.X);
            if (MathF.Abs(sinp) >= 1)
                euler.Y = MathF.CopySign(MathF.PI / 2, sinp);
            else
                euler.Y = MathF.Asin(sinp);

            float siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
            float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            euler.Z = MathF.Atan2(siny_cosp, cosy_cosp);

            return euler;
        }
    }
}
