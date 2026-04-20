using System;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace WTEditor.Avalonia.Util;

public static partial class GlUtil {
  public static bool IsInitialized { get; private set; }

   public static GL? SilkGl;

  private static readonly object GL_LOCK_ = new();

    public static void RunLockedGl(Action handler)
    {
        lock (GL_LOCK_){
          handler();
        }
    }

    public unsafe static void InitGl()
    {
        SilkGl.Enable(EnableCap.DebugOutput);
        SilkGl.DebugMessageCallback((source, type, id, severity, length, message, userparam) =>
        {
            string msg = Marshal.PtrToStringAnsi(message, length);
            if (id == 131185)
                return;

            Console.Error.WriteLine($"[DebugMessageCallback] source: {source}, type: {type}, id: {id}, severity {severity}, length {length}, userParam {userparam}\n{msg}\n\n");
        }, (void*)0);
    }
}