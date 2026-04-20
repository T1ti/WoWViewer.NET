using System;
using Silk.NET.Windowing;
using WTEditor.Avalonia.Util;


namespace fin.ui.rendering.gl;

public static class SilkWindowConstants
{
    public static WindowOptions CreateNewNativeWindowSettings()
    {
        
        ContextProfile profile = ContextProfile.Core;
        if (GlConstants.Compatibility)
            profile = ContextProfile.Compatability;
        
        ContextFlags contextFlags = ContextFlags.ForwardCompatible;
        if (GlConstants.Debug)
        {
            contextFlags |= ContextFlags.Debug;
        }
        
        var nativeWindowSettings = new WindowOptions
        {
        
            API = new GraphicsAPI(OpenGlVersionService.Es ? ContextAPI.OpenGLES : ContextAPI.OpenGL,
            profile,
            contextFlags,
            new APIVersion(OpenGlVersionService.MajorVersion, OpenGlVersionService.MinorVersion)),
            VSync = true,
            IsVisible = false,
            Size = new Silk.NET.Maths.Vector2D<int>(100, 100),
            // RedBits = 8,
            // BlueBits = 8,
            // GreenBits = 8,
            // AlphaBits = 8,
            // DepthBits = 32,
        };
        
        return nativeWindowSettings;
    }
}