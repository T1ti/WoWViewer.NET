using System.Drawing;

// using Silk.NET.OpenGL;

namespace WTEditor.Avalonia.Util;

public partial class GlState {
    public Rectangle Viewport { get; set; } = Rectangle.Empty;
}

public static partial class GlUtil
{
    public static void SetViewport(Rectangle viewport)
    {
        if (currentState_.Viewport == viewport)
        {
            return;
        }

        SilkGl?.Viewport(viewport);
    }
}