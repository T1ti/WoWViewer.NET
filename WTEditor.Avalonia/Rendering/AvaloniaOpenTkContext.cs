using System;

using Avalonia.OpenGL;

using OpenTK;

namespace WTEditor.Avalonia.Rendering
{
    public sealed class AvaloniaOpenTkContext(GlInterface glInterface)
        : IBindingsContext
    {
        public IntPtr GetProcAddress(string procName)
          => glInterface.GetProcAddress(procName);
    }
}
