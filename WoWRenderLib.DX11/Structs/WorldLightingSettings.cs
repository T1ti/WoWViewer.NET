using System.Numerics;

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
    bool HasLiquidAlphaData)
{
    public static WorldLightingSettings Defaults { get; } = new(
        0,
        0,
        new Vector3(-0.5f, -0.5f, 0.70710678f),
        new Vector3(104f / 255f, 130f / 255f, 154f / 255f),
        new Vector3(1f, 136f / 255f, 0f),
        Vector3.Zero,
        Vector3.Zero,
        Vector3.Zero,
        Vector3.Zero,
        1f,
        1f,
        1f,
        1f,
        false,
        false);
}
