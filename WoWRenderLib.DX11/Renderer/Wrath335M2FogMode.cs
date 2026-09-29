namespace WoWRenderLib.DX11.Renderer;

/// <summary>Fog colors chosen by CM2SceneRender::SetupLighting for M2 materials.</summary>
internal enum Wrath335M2FogMode
{
    SceneColor = 0,
    Disabled = 1,
    Black = 2,
    White = 3,
    Gray = 4
}

internal static class Wrath335M2FogPolicy
{
    public static Wrath335M2FogMode ForMaterial(
        bool legacyClient, int blendMode, bool unfogged)
    {
        if (unfogged)
            return Wrath335M2FogMode.Disabled;
        if (!legacyClient)
            return Wrath335M2FogMode.SceneColor;

        // 3.3.5 g_m2BlendModeFogModeTable at Wow.exe:0xA45390:
        // { 1, 1, 1, 2, 2, 3, 4 } for blend modes 0..6.
        return blendMode switch
        {
            3 or 4 => Wrath335M2FogMode.Black,
            5 => Wrath335M2FogMode.White,
            6 => Wrath335M2FogMode.Gray,
            _ => Wrath335M2FogMode.SceneColor
        };
    }
}
