using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering;
using Avalonia.Threading;

// using fin.config;
using Silk.NET.OpenGL;
using WTEditor.Avalonia.Util;


namespace WTEditor.Avalonia.Rendering
{
    // base class for opengl interop control
    // uses SharpDxInteropControl or OpenTkControl
    public abstract class BGlPanel : Panel, ICustomHitTest
    {
        public event Action? OnInit;

        public bool PreferGlNativeInterop = true;

        // frame times
        public double TotalFrameTime = 0;

        protected BGlPanel()
        {
            //openTK  gl context is set either by SharpDxInteropControl or OpenTkControl with GL.LoadBindings

            Dispatcher.UIThread.InvokeAsync(async () => 
            {
                var initGl = (GL gl) => {
                    this.InitGl(gl);
                    this.OnInit?.Invoke();
                };
                var renderGl = (GL gl) => this.RenderGl(gl);
                var teardownGl = (GL gl) => this.TeardownGl(gl);

                Action<double> frameTime = (ms) =>
                {
                    // _vm.RenderFrameTime = ms;
                    TotalFrameTime = ms;
                };

                if (PreferGlNativeInterop)
                {
                    OpenGlVersionService.Init(false); // Uses opengl ES for rendering

                    if (await SharpDxInteropControl.TryToAddTo(this, initGl, renderGl, teardownGl, frameTime))
                    {
                        // SharpDxInteropControl dxControl = (SharpDxInteropControl)this.Children[0];
                        return;
                    }
                }
                // fallback to non native using avalonia's opengl context
                OpenGlVersionService.Init(true); // Uses
                this.Children.Add(new AvaloniaSilkGlControl(initGl, renderGl, teardownGl));
            });
        }
        protected abstract void InitGl(GL gl);
        protected abstract void RenderGl(GL gl);
        protected abstract void TeardownGl(GL gl);


        public bool HitTest(Point point) => this.Bounds.Contains(point);

        protected void GetBoundsForGlViewport(out int width, out int height)
        {
            var scaling = 1f;
            if (TopLevel.GetTopLevel(this) is Window window)
            {
                scaling = (float)window.RenderScaling;
            }

            var bounds = this.Bounds;
            width = (int)(scaling * bounds.Width);
            height = (int)(scaling * bounds.Height);
        }
    }
}
