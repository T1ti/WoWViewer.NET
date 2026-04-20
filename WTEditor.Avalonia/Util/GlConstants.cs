namespace WTEditor.Avalonia.Util;

public static class GlConstants {
  public const bool Compatibility = false;
  public const bool Debug
#if DEBUG
      = true;
#else
      = false;
#endif
}