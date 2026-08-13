using System;

namespace WTEditor.Avalonia.Rendering;

internal static class Dx11RuntimeOptions
{
#if DEBUG
    public const string BuildConfiguration = "Debug";
    public const bool IsDebugBuild = true;
#else
    public const string BuildConfiguration = "Release";
    public const bool IsDebugBuild = false;
#endif

    public static bool IsDebugLayerRequested
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("WTEDITOR_D3D11_DEBUG");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string ProfilerEnvironmentLabel =>
        $"{BuildConfiguration} build · D3D11 validation {(IsDebugLayerRequested ? "on" : "off")}";
}
