using System;

using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using WTEditor.Avalonia.Util;

using Silk.NET.OpenGL;
using WTEditor.Avalonia.Util;

namespace WTEditor.Avalonia.Rendering
{

    // Used as a fallback by BGlPanel if PreferGlNativeInterop is false
    public class AvaloniaSilkGlControl(Action<GL> initGl, Action<GL> renderGl, Action<GL> teardownGl)
        : OpenGlControlBase
    {
        private AvaloniaSilkGlContext? avaloniaSilkGlContext_;
        private TimedCallback renderCallback_;

        private GL? gl_;

        private static bool isLoaded_ = false;

        public static int FPSConstant = 100;

        protected sealed override void OnOpenGlInit(GlInterface gl)
        {
            if (!isLoaded_ || this.avaloniaSilkGlContext_ == null)
            {
                //Initialize the Silk<->Avalonia Bridge
                this.avaloniaSilkGlContext_ = new AvaloniaSilkGlContext(gl);
                // avaloniaSilkGlContext_.GetProcAddress();

                gl_ = GL.GetApi(avaloniaSilkGlContext_);
                isLoaded_ = true;
            }

            GlUtil.SwitchContext(this); 
            initGl(gl_!);

            this.renderCallback_ = TimedCallback.WithFrequency(
                () => Dispatcher.UIThread.Post(this.RequestNextFrameRendering,
                                               DispatcherPriority.Background),
                FPSConstant);
        }

        protected override void OnOpenGlRender(GlInterface gl, int fb)
        {
            this.RequestNextFrameRendering();

            GlUtil.SwitchContext(this);
            if (gl_ != null)
                renderGl(gl_);
        }

        protected sealed override void OnOpenGlDeinit(GlInterface gl)
        {
            if (gl_ != null)
                teardownGl(gl_);
        }

    }
}
