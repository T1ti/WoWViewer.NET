using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WoWFormatLib.FileProviders;
using WoWViewer.NET.Managers;
using WoWViewer.NET.Objects;
using WoWViewer.NET.Services;
using WTEditor.Rendering;
using WTEditor.SilkRenderer.WPF.Common;
using WTEditor.SilkRenderer.WPF.OpenGL;

namespace WTEditor;

public partial class EditorView : UserControl
{
    private static bool cascLoaded = false;

    private static uint adtShaderProgram;
    private static uint wmoShaderProgram;
    private static uint m2ShaderProgram;
    private static uint debugShaderProgram;

    private static float movementSpeed = 150f;
    private static bool hasFocus = true;

    private static Camera activeCamera;

    private static Vector2 LastMousePosition;

    private static bool shadersReady = false;

    private static string WDTFDIDInput = "";

    private static bool gizmoWasUsing = false;
    private static bool gizmoWasOver = false;
    private static ImGuizmoOperation currentGizmoOperation = ImGuizmoOperation.Translate;

    private static ShaderManager shaderManager;
    private static SceneManager sceneManager;
    private static ImGuiController imguiController;

    private bool _hasFocus = false;
    private HashSet<Key> _pressedKeys = new HashSet<Key>();

    private Point? _mouseDownPosition;
    private bool _wasLeftMouseDown = false;

    private Vector3 _movementDirection = Vector3.Zero;
    private float _speedMultiplier = 1.0f;

    private DirectKeyboardState directKeyboard = new DirectKeyboardState();
    private DirectMouseState directMouse;

    public EditorView()
    {
        InitializeComponent();

        Preview.Setting = new SilkRenderer.WPF.OpenGL.Settings()
        {
            MajorVersion = 4,
            MinorVersion = 5,
            GraphicsProfile = ContextProfile.Compatability
        };

        Preview.MouseDown += OnMouseDown;
        Preview.MouseUp += OnMouseUp;

        Preview.Ready += Game_Ready;
        Preview.Render += Game_Render;
        Preview.UpdateFrame += Game_UpdateFrame;
        Preview.SizeChanged += (s, e) =>
        {
            activeCamera.AspectRatio = (float)e.NewSize.Width / (float)e.NewSize.Height;
            imguiController?.WindowResized((int)e.NewSize.Width, (int)e.NewSize.Height);
            UpdateControlOffset();
        };

        Preview.Loaded += (s, e) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);

            if (source != null)
            {
                directMouse = new DirectMouseState(source.Handle);
                directMouse.MouseDelta += OnDirectMouseDelta;
                directMouse.MouseButtonChanged += OnDirectMouseButtonChanged;

                UpdateControlOffset();
            }

            directKeyboard.AddKeys(
                Key.W, Key.A, Key.S, Key.D,
                Key.Up, Key.Down,
                Key.LeftShift, Key.RightShift,
                Key.R, Key.Space
            );

            directKeyboard.KeyStateChanged += OnDirectKeyStateChanged;
        };

        Preview.MouseEnter += (s, e) => Keyboard.Focus(Preview);

        Preview.GotKeyboardFocus += (s, e) =>
        {
            _hasFocus = true;
        };

        Preview.LostKeyboardFocus += (s, e) =>
        {
            _hasFocus = false;
            _pressedKeys.Clear();
            UpdateMovementDirection();
        };

        Preview.Start();
    }

    private void OnDirectMouseDelta(float deltaX, float deltaY)
    {
        if (!_hasFocus || activeCamera == null)
            return;

        // Clamp spikes just in case
        deltaX = Math.Clamp(deltaX, -50, 50);
        deltaY = Math.Clamp(deltaY, -50, 50);

        float sensitivity = 0.2f;

        activeCamera.ModifyDirection(
            deltaX * sensitivity,
            deltaY * sensitivity
        );

        UpdateMovementDirection();
    }

    private void OnDirectMouseButtonChanged(DirectMouseState.MouseButton button, bool isDown)
    {
        if (!_hasFocus)
            return;

        Debug.WriteLine($"Mouse Button: {button}, IsDown: {isDown}");

        // Update ImGui mouse button state
        if (imguiController != null)
        {
            if (button == DirectMouseState.MouseButton.Left)
                imguiController.SetMouseButton(0, isDown);
            else if (button == DirectMouseState.MouseButton.Right)
                imguiController.SetMouseButton(1, isDown);
            else if (button == DirectMouseState.MouseButton.Middle)
                imguiController.SetMouseButton(2, isDown);
        }

        if (button == DirectMouseState.MouseButton.Right)
        {
            if (isDown)
            {
                Mouse.Capture(Preview);
            }
            else
            {
                Mouse.Capture(null);
            }
        }
        else if (button == DirectMouseState.MouseButton.Left)
        {
            if (isDown && !_wasLeftMouseDown && !gizmoWasUsing && !gizmoWasOver)
            {
                // Record mouse down position for click detection (using direct polling)
                _mouseDownPosition = directMouse.CurrentPosition;
            }
            else if (!isDown && _wasLeftMouseDown)
            {
                // Mouse button released - check if it was a click (not a drag)
                if (_mouseDownPosition.HasValue)
                {
                    var currentPos = directMouse.CurrentPosition;
                    var dragDistance = Math.Sqrt(
                        Math.Pow(currentPos.X - _mouseDownPosition.Value.X, 2) +
                        Math.Pow(currentPos.Y - _mouseDownPosition.Value.Y, 2)
                    );

                    // If drag distance is small, treat as click and perform raycast
                    if (dragDistance < 5.0)
                    {
                        Debug.WriteLine($"Click detected at control coords: {currentPos.X}, {currentPos.Y} (Preview size: {Preview.ActualWidth}x{Preview.ActualHeight})");
                        sceneManager?.PerformRaycast(
                            (float)currentPos.X,
                            (float)currentPos.Y,
                            activeCamera,
                            (int)Preview.ActualWidth,
                            (int)Preview.ActualHeight
                        );
                    }
                }
                _mouseDownPosition = null;
            }

            _wasLeftMouseDown = isDown;
        }
    }

    private void OnDirectKeyStateChanged(Key key, bool isKeyDown)
    {
        if (!_hasFocus)
            return;

        if (imguiController != null)
        {
            var imguiKey = ImGuiController.ConvertWpfKeyToImGui(key);
            if (imguiKey != ImGuiKey.None)
            {
                imguiController.SetKeyDown(imguiKey, isKeyDown);
            }

            if (key == Key.LeftCtrl || key == Key.RightCtrl)
                imguiController.SetControlKey(isKeyDown);
            if (key == Key.LeftShift || key == Key.RightShift)
                imguiController.SetShiftKey(isKeyDown);
            if (key == Key.LeftAlt || key == Key.RightAlt)
                imguiController.SetAltKey(isKeyDown);
        }

        if (isKeyDown)
        {
            if (key == Key.Space && !_pressedKeys.Contains(Key.Space) && sceneManager != null && sceneManager.SelectedObject != null && !gizmoWasUsing)
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

            _pressedKeys.Add(key);
        }
        else
        {
            _pressedKeys.Remove(key);
        }

        UpdateMovementDirection();
    }

    private void Game_Ready()
    {
        shaderManager = new ShaderManager(RenderContext.GL, Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory)!, "Shaders"));
        sceneManager = new SceneManager(RenderContext.GL, shaderManager);

        adtShaderProgram = shaderManager.GetOrCompileShader("adt");
        wmoShaderProgram = shaderManager.GetOrCompileShader("wmo");
        m2ShaderProgram = shaderManager.GetOrCompileShader("m2");
        debugShaderProgram = shaderManager.GetOrCompileShader("debug");

        sceneManager.Initialize(shaderManager, adtShaderProgram, wmoShaderProgram, m2ShaderProgram, debugShaderProgram);
        WDTFDIDInput = sceneManager.CurrentWDTFileDataID.ToString();
        shadersReady = true;

        Task.Run(async () =>
        {
            await CASC.Initialize(Settings.wowDir, Settings.wowProgram, Settings.buildConfig, Settings.cdnConfig);

            var tactFileProvider = new TACTSharpFileProvider();
            tactFileProvider.InitTACT(CASC.buildInstance);
            FileProvider.SetDefaultBuild(TACTSharpFileProvider.BuildName);
            FileProvider.SetProvider(tactFileProvider, TACTSharpFileProvider.BuildName);

            cascLoaded = true;
        });

        activeCamera = new Camera(new Vector3(0f, -0f, 200f), Vector3.UnitX, Vector3.UnitZ * -1, (float)ActualWidth / (float)ActualHeight);
        activeCamera.Yaw = 45f;
        RenderContext.GL.ClearColor(1.0f, 0.0f, 0.0f, 1.0f);

        var err2 = RenderContext.GL.GetError();
        if (err2 != GLEnum.NoError)
        {
            Console.WriteLine("Load GL Error: " + err2);
        }

        unsafe
        {
            RenderContext.GL.DebugMessageCallback((source, type, id, severity, length, message, userparam) =>
            {
                string msg = Marshal.PtrToStringAnsi(message, length);
                if (id == 131185)
                    return;

                Console.Error.WriteLine($"[DebugMessageCallback] source: {source}, type: {type}, id: {id}, severity {severity}, length {length}, userParam {userparam}\n{msg}\n\n");
            }, (void*)0);
        }

        // Initialize ImGui with custom controller
        var width = (int)Preview.ActualWidth;
        var height = (int)Preview.ActualHeight;
        Debug.WriteLine($"Creating ImGuiController with Preview size: {width}x{height}");

        imguiController = new ImGuiController(RenderContext.GL, width > 0 ? width : 800, height > 0 ? height : 600);

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        // Initialize ImGui style
        ImGui.GetStyle().WindowRounding = 5.0f;
        ImGui.GetStyle().WindowPadding = new Vector2(0.0f, 0.0f);
        ImGui.GetStyle().FrameRounding = 12.0f;

        ImGuizmo.SetImGuiContext(ImGui.GetCurrentContext());
    }


    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Only used to ensure focus, actual button handling is done by direct polling
        Keyboard.Focus(Preview);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        // Handled by direct polling
    }

    private void UpdateMovementDirection()
    {
        if (activeCamera == null)
            return;

        Vector3 direction = Vector3.Zero;

        if (_pressedKeys.Contains(Key.W))
            direction += activeCamera.Front;

        if (_pressedKeys.Contains(Key.S))
            direction -= activeCamera.Front;

        if (_pressedKeys.Contains(Key.A))
            direction += Vector3.Normalize(Vector3.Cross(activeCamera.Front, activeCamera.Up));

        if (_pressedKeys.Contains(Key.D))
            direction -= Vector3.Normalize(Vector3.Cross(activeCamera.Front, activeCamera.Up));

        if (_pressedKeys.Contains(Key.Up))
            direction -= activeCamera.Up;

        if (_pressedKeys.Contains(Key.Down))
            direction += activeCamera.Up;

        if (_pressedKeys.Contains(Key.R))
        {
            activeCamera.Position = Vector3.One;
        }

        _movementDirection = direction.LengthSquared() > 0 ? Vector3.Normalize(direction) : Vector3.Zero;
        _speedMultiplier = _pressedKeys.Contains(Key.LeftShift) ? 2.0f : 1.0f;
    }

    private void Game_Render(TimeSpan obj)
    {
        if (!hasFocus || !cascLoaded || !shadersReady)
            return;
#if DEBUG
        var err = RenderContext.GL.GetError();
        if (err != GLEnum.NoError)
        {
            Console.WriteLine("Window Render GL Error: " + err);
        }
#endif

        activeCamera.AspectRatio = (float)ActualWidth / (float)ActualHeight;

        RenderContext.GL.Enable(EnableCap.DepthTest);

        RenderContext.GL.ClearColor(0f, 0f, 0f, 0.5f);
        RenderContext.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        activeCamera.UpdateFrustum();

        // Update ImGui controller size if needed
        imguiController.WindowResized((int)Preview.ActualWidth, (int)Preview.ActualHeight);

        // Start ImGui frame
        imguiController.Update((float)obj.TotalSeconds);
        ImGuizmo.BeginFrame();

        if (cascLoaded)
            sceneManager.GetCurrentWDT();

        sceneManager.ProcessQueue();

        sceneManager.UpdateTilesByCameraPos(activeCamera.Position);

        if (!string.IsNullOrEmpty(sceneManager.StatusMessage))
        {
            ImGui.Begin("Loading");
            ImGui.Text(sceneManager.StatusMessage);
            ImGui.End();
        }

        ImGui.Begin("Map selection");

        ImGui.InputText("WDT label", ref WDTFDIDInput, 100);

        if (ImGui.Button("Load WDT"))
        {
            if (uint.TryParse(WDTFDIDInput, out var newWDTI) && sceneManager.CurrentWDTFileDataID != newWDTI && CASC.FileExists(newWDTI))
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
            ImGui.Text("RAM usage: " + (GC.GetTotalMemory(false) / 1024 / 1024).ToString() + " MB");

            var i = 0;
            if (ImGui.CollapsingHeader("Loaded WMOs"))
            {
                foreach (var sceneObject in sceneManager.SceneObjects.Where(x => x is WMOContainer))
                {
                    var wmoContainer = (WMOContainer)sceneObject;
                    var wmoString = "WMO #" + i + " FDID " + wmoContainer.FileDataId.ToString();

                    if(sceneObject.IsSelected)
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

                    if(sceneObject.IsSelected)
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

        // End ImGui frame and render
        imguiController.Render();
    }

    private void Game_UpdateFrame(object arg1, TimeSpan arg2)
    {
        // Poll input state directly for immediate response (only when focused)
        if (_hasFocus)
        {
            directKeyboard.Update();
            directMouse?.Update();

            // Update ImGui mouse position
            if (imguiController != null && directMouse != null)
            {
                var mousePos = directMouse.CurrentPosition;
                imguiController.SetMousePosition((float)mousePos.X, (float)mousePos.Y);
            }
        }

        if (activeCamera != null && _movementDirection != Vector3.Zero)
        {
            var moveSpeed = movementSpeed * _speedMultiplier * (float)arg2.TotalSeconds;
            activeCamera.Position += _movementDirection * moveSpeed;
        }

#if DEBUG
        if (shaderManager != null)
            shaderManager.CheckForChanges();
#endif
    }

    private void UpdateControlOffset()
    {
        if (directMouse == null || Preview == null)
            return;

        try
        {
            var source = (HwndSource)PresentationSource.FromVisual(Preview);
            if (source != null)
            {
                // Get DPI scale factors
                var dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                var dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                Debug.WriteLine($"DPI Scale: {dpiScaleX}x, {dpiScaleY}y");

                // Set DPI scale in DirectMouseState
                directMouse.SetDpiScale(dpiScaleX, dpiScaleY);

                // Get the Preview control's screen position
                var previewScreenPos = Preview.PointToScreen(new Point(0, 0));
                Debug.WriteLine($"Preview screen position: {previewScreenPos.X}, {previewScreenPos.Y}");

                // Get the window's screen position (client area)
                var windowScreenPos = source.RootVisual.PointToScreen(new Point(0, 0));
                Debug.WriteLine($"Window screen position: {windowScreenPos.X}, {windowScreenPos.Y}");

                // Calculate offset in physical pixels (difference between control and window in screen coordinates)
                var controlTopLeft = new Point(
                    previewScreenPos.X - windowScreenPos.X,
                    previewScreenPos.Y - windowScreenPos.Y
                );

                directMouse.SetControlOffset(controlTopLeft);
                Debug.WriteLine($"Control offset set to: {controlTopLeft.X}, {controlTopLeft.Y} (physical pixels)");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating control offset: {ex.Message}");
        }
    }

    private unsafe void RenderGizmo()
    {
        if (sceneManager.SelectedObject == null)
            return;

        var windowPos = new Vector2(0, 0);
        var windowSize = new Vector2((float)ActualWidth, (float)ActualHeight);

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
