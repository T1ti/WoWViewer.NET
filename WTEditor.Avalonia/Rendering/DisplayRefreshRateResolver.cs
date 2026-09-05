using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace WTEditor.Avalonia.Rendering;

/// <summary>
/// Reads the active Windows display mode for the screen hosting the main window.
/// Avalonia exposes the display identity but not its refresh rate.
/// </summary>
internal static class DisplayRefreshRateResolver
{
    private const int EnumCurrentSettings = -1;

    public static int GetRefreshRate(Window window)
    {
        var deviceName = window.Screens.ScreenFromWindow(window)?.DisplayName;
        return TryGetRefreshRate(deviceName, out var refreshRate)
            ? ViewportFrameRatePolicy.NormalizeForegroundFramesPerSecond(refreshRate)
            : ViewportFrameRatePolicy.DefaultForegroundFramesPerSecond;
    }

    private static bool TryGetRefreshRate(string? deviceName, out int refreshRate)
    {
        var deviceMode = new DeviceMode
        {
            Size = (short)Marshal.SizeOf<DeviceMode>()
        };

        if (EnumDisplaySettings(deviceName, EnumCurrentSettings, ref deviceMode) &&
            deviceMode.DisplayFrequency is >= ViewportFrameRatePolicy.MinimumForegroundFramesPerSecond and
                <= ViewportFrameRatePolicy.MaximumForegroundFramesPerSecond)
        {
            refreshRate = deviceMode.DisplayFrequency;
            return true;
        }

        refreshRate = 0;
        return false;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumDisplaySettings(
        string? deviceName,
        int modeNumber,
        ref DeviceMode deviceMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DeviceMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public short SpecVersion;
        public short DriverVersion;
        public short Size;
        public short DriverExtra;
        public int Fields;
        public int PositionX;
        public int PositionY;
        public int DisplayOrientation;
        public int DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short VerticalResolution;
        public short TrueTypeOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;
        public short LogPixels;
        public int BitsPerPixel;
        public int PixelsWidth;
        public int PixelsHeight;
        public int DisplayFlags;
        public int DisplayFrequency;
        public int IcmMethod;
        public int IcmIntent;
        public int MediaType;
        public int DitherType;
        public int Reserved1;
        public int Reserved2;
        public int PanningWidth;
        public int PanningHeight;
    }
}
