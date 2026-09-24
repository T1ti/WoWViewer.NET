using System.Numerics;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Structs;

/// <summary>
/// Immutable renderer-facing snapshot of every world-lighting value currently
/// consumed by the DX11 world and MH2O passes.
/// </summary>
public readonly record struct WorldLightingSettings(
    long LightParamId,
    long Time,
    Vector3 LightDirection,
    Vector3 AmbientColor,
    Vector3 DiffuseColor,
    Vector3 OceanCloseColor,
    Vector3 OceanFarColor,
    Vector3 RiverCloseColor,
    Vector3 RiverFarColor,
    float WaterShallowAlpha,
    float WaterDeepAlpha,
    float OceanShallowAlpha,
    float OceanDeepAlpha,
    bool HasLiquidColorData,
    bool HasLiquidAlphaData,
    bool IsDynamic)
{
    /// <summary>
    /// Clamps values consumed by shaders.
    /// </summary>
    public WorldLightingSettings NormalizeForRendering()
    {
        var directionLengthSquared = LightDirection.LengthSquared();
        var direction = directionLengthSquared > 0.000001f
            ? LightDirection / MathF.Sqrt(directionLengthSquared)
            : Vector3.UnitZ;
        return this with
        {
            LightDirection = direction,
            AmbientColor = ClampColor(AmbientColor),
            DiffuseColor = ClampColor(DiffuseColor),
            OceanCloseColor = ClampColor(OceanCloseColor),
            OceanFarColor = ClampColor(OceanFarColor),
            RiverCloseColor = ClampColor(RiverCloseColor),
            RiverFarColor = ClampColor(RiverFarColor),
            WaterShallowAlpha = Math.Clamp(WaterShallowAlpha, 0f, 1f),
            WaterDeepAlpha = Math.Clamp(WaterDeepAlpha, 0f, 1f),
            OceanShallowAlpha = Math.Clamp(OceanShallowAlpha, 0f, 1f),
            OceanDeepAlpha = Math.Clamp(OceanDeepAlpha, 0f, 1f)
        };
    }

    private static Vector3 ClampColor(Vector3 color) => new(
        Math.Clamp(color.X, 0f, 4f),
        Math.Clamp(color.Y, 0f, 4f),
        Math.Clamp(color.Z, 0f, 4f));

    public static WorldLightingSettings Defaults { get; } = new(
        0,
        1440,
        new Vector3(0.5f, 0.5f, 0.70710678f),
        new Vector3(104f / 255f, 130f / 255f, 154f / 255f),
        new Vector3(1f, 136f / 255f, 0f),
        WorldLiquidColorDefaults.OceanClose,
        WorldLiquidColorDefaults.OceanFar,
        WorldLiquidColorDefaults.RiverClose,
        WorldLiquidColorDefaults.RiverFar,
        1f,
        1f,
        1f,
        1f,
        true,
        false,
        false);
}
