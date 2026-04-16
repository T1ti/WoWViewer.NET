using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Silk.NET.OpenGL;
using WoWFormatLib.Structs.M2;
using WoWRenderLib;
using WTEditor.Avalonia.ViewModels;
using static Avalonia.OpenGL.GlConsts;

namespace WTEditor.Avalonia.Controls
{
    public sealed class GlView : OpenGlControlBase
    {
        private WowClientConfig _wowConfig;

        private WowViewerEngine wowViewerEngine;

        private GL _gl;
        // private GlRenderer _renderer = new GlRenderer();

        // frame times
        private Stopwatch _sw = Stopwatch.StartNew();
        private double _last;

        private bool _glReady;

        private Editor3DViewModel? _vm;


        private int _fbWidth = 0;
        private int _fbHeight = 0;

        public GlView()
        {

        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            Focusable = true;
            Focus();

            // wowViewerEngine.SetHasFocus(Focusable);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            Debug.Assert(DataContext != null);
            _vm = DataContext as Editor3DViewModel;
        }

        private static void CheckError(GlInterface gl)
        {
            int err;
            while ((err = gl.GetError()) != GL_NO_ERROR)
            {
                Console.WriteLine($"GL ERROR: {err}");
            }
        }

        protected override void OnOpenGlInit(GlInterface gl)
        {
            base.OnOpenGlInit(gl);

            CheckError(gl);

            _gl = GL.GetApi(gl.GetProcAddress);

            var version = gl.Version; // avalonia defaults to 4.0 if using WGL
            var vendor = gl.Vendor;

            //double scale = VisualRoot?.RenderScaling ?? 1.0;
            float scale = 1.0f;
            int width = (int)(Bounds.Width * scale);
            int height = (int)(Bounds.Height * scale);

            wowViewerEngine = new WowViewerEngine(_wowConfig, null, false);

            wowViewerEngine.Initialize(_gl, new Silk.NET.Maths.Vector2D<int>(width, height));

            wowViewerEngine.Resize((uint)width, (uint)height);

            _glReady = true;
        }

        protected override void OnOpenGlDeinit(GlInterface gl)
        {
            // cleanup if needed

            base.OnOpenGlDeinit(gl);
        }

        protected override void OnOpenGlRender(GlInterface gl, int fb)
        {
            // Render loop - called by Avalonia when the control needs to be redrawn
            double now = _sw.Elapsed.TotalSeconds;
            double delta = now - _last;
            _last = now;

            double msec_delta = delta * 1000.0;

            if (!_glReady)
            {
                RequestNextFrameRendering(); // always request a new frame
                return;
            }

            // framrate cap
            // if (delta < 1.0 / 144.0)
            // {
            //     RequestNextFrameRendering();
            //     return;
            // }

            // re ensure scale every frame, onsizechanged event isn't enough because of dynamic scaling
            // var scale = VisualRoot?.RenderScaling ?? 1.0;
            float scale = 1.0f;
            int width = (int)(Bounds.Width * scale);
            int height = (int)(Bounds.Height * scale);

            if (width != _fbWidth || height != _fbHeight)
            {
                _fbWidth = width;
                _fbHeight = height;

                wowViewerEngine.Resize((uint)width, (uint)height);
            }

            // camera, inputs, view matrix...
            Update(delta);

            wowViewerEngine.Render(delta);

            // schedule next frame
            RequestNextFrameRendering();
            // Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);
            // Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);


            double endOfFrameTime = _sw.Elapsed.TotalSeconds;
            delta = endOfFrameTime - _last;

            // true frame time, how long it took for the frame to render.
            // not bound by avalonia refresh rate
            _vm.FrameTime = delta * 1000.0; // msecs
        }

        private void Update(double dt)
        {
            Debug.Assert(_vm != null);

            if (_vm == null)
                return;

            var inputFrame = BuildCameraInput();

            wowViewerEngine.Update(dt, inputFrame);

            _vm.Fps = wowViewerEngine.Stats.FPS;
            // _vm.FrameTime = _renderer.Stats.FrameTimeMs; // true frame time in onopenglrender()
            _vm.CameraPosition = wowViewerEngine.activeCamera.Position;
            _vm.DrawCalls = wowViewerEngine.Stats.DrawCalls;
            _vm.VertexCount = wowViewerEngine.Stats.VertexCount;
        }
        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);

            if (!_glReady)
                return;

            return; // handled in render loop

            var test = VisualRoot?.RenderTransform;

            // float scale = VisualRoot?.RenderScaling ?? 1.0;
            float scale = 1.0f;
            uint width = (uint)(e.NewSize.Width * scale);
            uint height = (uint)(e.NewSize.Height * scale);

            //Debug.Assert((width != _currentWidth || height != _currentHeight));>	WTEditor.Avalonia.dll!WTEditor.Avalonia.Controls.GlView.OnSizeChanged(Avalonia.Controls.SizeChangedEventArgs e) Ligne 175	C#


            wowViewerEngine.Resize(width, height);
        }

        private InputFrame BuildCameraInput()
        {
            var silkKeysDown = new HashSet<Silk.NET.Input.Key>();

            // always send in qwerty format for now
            if (_vm.Forward) silkKeysDown.Add(Silk.NET.Input.Key.W);
            if (_vm.Backward) silkKeysDown.Add(Silk.NET.Input.Key.S);
            if (_vm.Left) silkKeysDown.Add(Silk.NET.Input.Key.A);
            if (_vm.Right) silkKeysDown.Add(Silk.NET.Input.Key.D);
            if (_vm.Up) silkKeysDown.Add(Silk.NET.Input.Key.Q);
            if (_vm.Down) silkKeysDown.Add(Silk.NET.Input.Key.E);
            if (_vm.Shift) silkKeysDown.Add(Silk.NET.Input.Key.ShiftLeft);
            if (_vm.Ctrl) silkKeysDown.Add(Silk.NET.Input.Key.ControlLeft);
            if (_vm.Space) silkKeysDown.Add(Silk.NET.Input.Key.Space);



            var inputFrame = new InputFrame
            {
                MousePosition = _vm.MousePosition,
                LeftMouseDown = _vm.LeftMouseDown,
                RightMouseDown = _vm.RightMouseDown,
                KeysDown = silkKeysDown,
                MouseWheel = _vm.MouseWheel
            };

            return inputFrame;
        }

    }
}
