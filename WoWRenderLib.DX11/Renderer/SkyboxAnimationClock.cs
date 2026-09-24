using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

internal static class SkyboxAnimationClock
{
    private const long LightDayUnits = 2880;

    public static long GetTimeMilliseconds(
        M2Animation animation, int skyboxFlags, long sceneTimeMilliseconds, long lightTime)
    {
        // LightSkybox 0x1 spans the full lighting day. Map the circular light
        // clock onto the selected M2 sequence rather than free-running it.
        if ((skyboxFlags & 0x1) == 0 || animation.Sequences.Length == 0)
            return sceneTimeMilliseconds;

        var duration = animation.DefaultSequenceDuration;
        if (duration == 0)
            return 0;
        var dayTime = ((lightTime % LightDayUnits) + LightDayUnits) % LightDayUnits;
        return dayTime * duration / LightDayUnits;
    }
}
