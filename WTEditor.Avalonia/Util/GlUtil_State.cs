using System.Collections.Generic;
using Silk.NET.Core.Contexts;

namespace WTEditor.Avalonia.Util;

public partial class GlState;

public static partial class GlUtil {
    // private static NullFriendlyDictionary<object?, GlState> stateByKey_ = new();
    private static Dictionary<object, GlState> stateByKey_ = new();

    private static GlState currentState_;

    public static void SwitchContext(IGLContext? context)
    {
      if (!stateByKey_.TryGetValue(context, out var state))
      {
          stateByKey_.Add(context, state = new GlState());
      }
    
      currentState_ = state;
      context?.MakeCurrent();
    }

    public static void SwitchContext(object? any)
    {
        if (!stateByKey_.TryGetValue(any, out var state))
        {
            stateByKey_.Add(any, state = new GlState());
        }

        currentState_ = state;
    }
}