using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System.Diagnostics;
using WoWRenderLib;

namespace WoWViewer.NET
{
    public class SilkWindowHost
    {
        private WowClientConfig _wowConfig;

        private WowViewerEngine wowViewerEngine;

        private GL gl;

        private IInputContext inputContext;

        private IWindow window;

        private bool hasFocus = true;

        private SilkImGuiBackend silkImGuiBackend;

        public SilkWindowHost(WowClientConfig wowConfig)
        {
            _wowConfig = wowConfig;
        }

        public void Run()
        {
            var windowOptions = WindowOptions.Default;
            windowOptions.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible | ContextFlags.Debug, new APIVersion(4, 5));
            windowOptions.ShouldSwapAutomatically = false;
            windowOptions.VSync = false;
            windowOptions.Size = new Vector2D<int>(1920, 1080);
            windowOptions.Title = "WoWRenderLib";
            window = Window.Create(windowOptions);

#if DEBUG
            Evergine.Bindings.RenderDoc.RenderDoc.Load(out Evergine.Bindings.RenderDoc.RenderDoc renderDoc);
#endif

            window.Load += OnLoad;
            window.FramebufferResize += OnResize;
            window.FocusChanged += OnFocusChanged;
            window.Update += OnUpdate;

            window.Render += OnRender;
            window.Resize += OnResize;
            window.Closing += OnClose;

            // Starts main loop
            window.Run();

            // after exiting run loop
            window.Dispose();
        }

        private void OnClose()
        {
            inputContext.Dispose();
            gl.Dispose();

            wowViewerEngine.Dispose();
        }

        private void OnLoad()
        {
            gl = window.CreateOpenGL();

            inputContext = window.CreateInput();

            silkImGuiBackend = new SilkImGuiBackend(gl, window, inputContext);

            // var engine = new WowViewerEngine(_wowConfig, imgui);
            wowViewerEngine = new WowViewerEngine(_wowConfig, silkImGuiBackend, true);

            wowViewerEngine.Initialize(gl, window.FramebufferSize);

            wowViewerEngine.Resize((uint)window.FramebufferSize.X, (uint)window.FramebufferSize.Y);
        }

        private void OnResize(Vector2D<int> frameBufferSize)
        {
            // if this fails, we need to handle window size and FB size separately in engine
            Debug.Assert(frameBufferSize.X == window.Size.X && frameBufferSize.Y == window.Size.Y, "Framebuffer size should match window size");

            wowViewerEngine.Resize((uint)frameBufferSize.X, (uint)frameBufferSize.Y);
        }

        private void OnUpdate(double deltaTime)
        {
            // silkImGuiBackend.Update((float)deltaTime);

            // Build inputs
            var primaryKeyboard = inputContext.Keyboards[0];
            var primaryMouse = inputContext.Mice[0];

            // build inputs for this frame
            InputFrame inputFrame = new InputFrame
            {
                MousePosition = primaryMouse.Position,
                LeftMouseDown = primaryMouse.IsButtonPressed(MouseButton.Left),
                RightMouseDown = primaryMouse.IsButtonPressed(MouseButton.Right),
                MouseWheel = primaryMouse.ScrollWheels[0].Y,
                KeysDown = new HashSet<Key>()
            };
            foreach (Key key in primaryKeyboard.SupportedKeys)
            {
                if (primaryKeyboard.IsKeyPressed(key))
                {
                    inputFrame.KeysDown.Add(key);
                }
            }

            wowViewerEngine.Update(deltaTime, inputFrame);

        }

        private void OnRender(double deltaTime)
        {
            if (!window.IsVisible || window.WindowState == WindowState.Minimized)
                return; // can cap fps instead

            wowViewerEngine.Render(deltaTime);

            window.SwapBuffers();
        }

        private void OnFocusChanged(bool focused)
        {
            hasFocus = focused;

            wowViewerEngine.SetHasFocus(focused);
        }

    }
}
