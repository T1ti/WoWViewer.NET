using Silk.NET.OpenGL;

namespace WTEditor.Avalonia.Rendering
{
    public interface IGlControl
    {
        // void InitGl();
        // void RenderGl();
        // void TeardownGl();

        void InitGl(GL gl);
        void RenderGl(GL gl);
        void TeardownGl(GL gl);
    }

}
