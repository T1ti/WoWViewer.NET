using System;

using Avalonia.OpenGL;

using Silk.NET.Core.Contexts;

namespace WTEditor.Avalonia.Rendering
{
    public sealed class AvaloniaSilkGlContext(GlInterface glInterface)
        : INativeContext // IBindingsContext
    {
        public void Dispose()
        {
            
        }
        public nint GetProcAddress(string proc, int? slot = null)
        {
            return glInterface.GetProcAddress(proc);
        }

        public bool TryGetProcAddress(string proc, out nint addr, int? slot = null)
        {
            addr = glInterface.GetProcAddress(proc);
            return addr != IntPtr.Zero;
        }
    }
}
