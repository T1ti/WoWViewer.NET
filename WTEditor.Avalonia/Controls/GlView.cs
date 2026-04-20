using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
// using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering.Composition;
using Silk.NET.OpenAL;
using Silk.NET.OpenGL;
using WoWFormatLib.Structs.M2;
using WoWRenderLib;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Controls
{
    public sealed class GlView : BGlPanel
    {
        private WowClientConfig _wowConfig;

        private WowViewerEngine wowViewerEngine;

        private GL _gl;
        // private GlRenderer _renderer = new GlRenderer();

        private bool isMouseInView_;

        // frame times
        private Stopwatch _sw;
        private double _last;

        private bool _glReady;

        private Editor3DViewModel? _vm;

        private int _fbWidth = 0;
        private int _fbHeight = 0;

        // accumulate frame timings stats and update periodically
        private double _frameTimeAccum; // total frame times added
        private int _frameCount;
        private double _TotalFrameTimeAccum;// gpu interop frame timings

        private double _lastStatsUpdate;

        public GlView()
        {
            // to make 
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            Focusable = true;
            Focus();

            // wowViewerEngine.SetHasFocus(Focusable);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            wowViewerEngine.Dispose();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            Debug.Assert(DataContext != null);
            _vm = DataContext as Editor3DViewModel;
        }

        protected override void InitGl(GL gl)
        {
            // GlUtil.InitGl(); // inits debug print

            _gl = gl;

            //double scale = VisualRoot?.RenderScaling ?? 1.0;
            float scale = 1.0f;
            int width = (int)(Bounds.Width * scale);
            int height = (int)(Bounds.Height * scale);

            wowViewerEngine = new WowViewerEngine(_wowConfig, null, false);

            wowViewerEngine.Initialize(_gl, new Silk.NET.Maths.Vector2D<int>(width, height));

            wowViewerEngine.Resize((uint)width, (uint)height);

            _glReady = true;

            _sw = Stopwatch.StartNew();
        }

        protected override void TeardownGl(GL gl)
        {
            wowViewerEngine.Dispose();
        }

        protected override void RenderGl(GL gl)
        {
            if (!_glReady)
            {
                // RequestNextFrameRendering(); // always request a new frame
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
            GetBoundsForGlViewport(out width, out height);


            if (width != _fbWidth || height != _fbHeight)
            {
                _fbWidth = width;
                _fbHeight = height;
            
                wowViewerEngine.Resize((uint)width, (uint)height);
            }

            double frameStart = _sw.Elapsed.TotalSeconds;
            double frameDelta = frameStart - _last;
            _last = frameStart;

            // camera, inputs, view matrix...
            Update(frameDelta);
            ////
            wowViewerEngine.Render(frameDelta);
            
            // final delta after render for stats
            double endOfFrameTime = _sw.Elapsed.TotalSeconds;
            double frameTime = endOfFrameTime - frameStart;

            // accumulate
            _frameTimeAccum += frameTime;
            _TotalFrameTimeAccum += TotalFrameTime;
            _frameCount++;

            // true frame times, how long it took for the frame to render.
            // not bound by avalonia refresh rate
            if (endOfFrameTime - _lastStatsUpdate >= 1.0) // update every 1.0 sec
            {
                double avgFrameTime = _frameTimeAccum / _frameCount;
                double avgTotalFrameTime = _TotalFrameTimeAccum / _frameCount; // multiply this by 1000 for msec time
                // _vm.Fps = 1.0 / avgFrameTime;
                _vm.Fps = wowViewerEngine.Stats.FPS; // time between frames (frame capped)
                _vm.RenderFrameTime = avgFrameTime * 1000.0; // only Engine render timing (uncapped)
                _vm.TotalFrameTime = avgTotalFrameTime; // Engine render + gpu interop timing (uncapped)
                _vm.InteropFrameTime = avgTotalFrameTime - _vm.RenderFrameTime;
                _vm.UncappedFPS = 1 / avgTotalFrameTime * 1000.0;

                _vm.InteropPctCost = (float)((avgTotalFrameTime - _vm.RenderFrameTime) / _vm.RenderFrameTime * 100.0);

                // other stats
                _vm.CameraPosition = wowViewerEngine.activeCamera.Position;
                _vm.DrawCalls = wowViewerEngine.Stats.DrawCalls;
                _vm.VertexCount = wowViewerEngine.Stats.VertexCount;

                // reset
                _frameTimeAccum = 0;
                _TotalFrameTimeAccum = 0;
                _frameCount = 0;
                _lastStatsUpdate = endOfFrameTime;
            }
        }

        private void Update(double dt)
        {
            Debug.Assert(_vm != null);

            if (_vm == null)
                return;

            var inputFrame = BuildCameraInput();

            wowViewerEngine.Update(dt, inputFrame);

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
            int width = (int)(e.NewSize.Width * scale);
            int height = (int)(e.NewSize.Height * scale);
            GetBoundsForGlViewport(out width, out height);
            //Debug.Assert((width != _currentWidth || height != _currentHeight));>	WTEditor.Avalonia.dll!WTEditor.Avalonia.Controls.GlView.OnSizeChanged(Avalonia.Controls.SizeChangedEventArgs e) Ligne 175	C#


            wowViewerEngine.Resize((uint)width, (uint)height);
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
