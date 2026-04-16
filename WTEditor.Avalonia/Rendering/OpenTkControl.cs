using System;

using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;

using fin.ui.rendering.gl;
using fin.util.time;

using OpenTK.Graphics.OpenGL4;
using Silk.NET.OpenGL;

namespace WTEditor.Avalonia.Rendering
{
    public class OpenTkControl(Action initGl, Action renderGl, Action teardownGl)
        : OpenGlControlBase
    {
        private AvaloniaOpenTkContext? avaloniaTkContext_;
        private TimedCallback renderCallback_;

        private static bool isLoaded_ = false;

        protected sealed override void OnOpenGlInit(GlInterface gl)
        {
            if (!isLoaded_)
            {
                //Initialize the OpenTK<->Avalonia Bridge
                this.avaloniaTkContext_ = new AvaloniaOpenTkContext(gl);

                GL.LoadBindings(this.avaloniaTkContext_);
                isLoaded_ = true;
            }

            GlUtil.SwitchContext(this);
            initGl();

            this.renderCallback_ = TimedCallback.WithFrequency(
                () => Dispatcher.UIThread.Post(this.RequestNextFrameRendering,
                                               DispatcherPriority.Background),
                UiConstants.FPS);
        }

        protected override void OnOpenGlRender(GlInterface gl, int fb)
        {
            this.RequestNextFrameRendering();

            GlUtil.SwitchContext(this);
            renderGl();
        }

        protected sealed override void OnOpenGlDeinit(GlInterface gl)
          => teardownGl();
    }
}
