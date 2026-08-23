using System;

namespace WTEditor.Avalonia.ViewModels;

[Flags]
public enum TextureFlags : uint
{
    Flag_0x1_WrapX = 0x1,
    Flag_0x2_WrapY = 0x2
}

// wowlib does not currently expose the WMO shader-number table as an enum.
public enum MOMTShader : uint
{
    Diffuse = 0,
    Specular = 1,
    Metal = 2,
    Env = 3,
    Opaque = 4,
    EnvMetal = 5,
    TwoLayerDiffuse = 6,
    TwoLayerEnvMetal = 7,
    TwoLayerTerrain = 8,
    DiffuseEmissive = 9,
    WaterWindow = 10,
    MaskedEnvMetal = 11,
    EnvMetalEmissive = 12,
    TwoLayerDiffuseOpaque = 13,
    SubmarineWindow = 14,
    TwoLayerDiffuseEmissive = 15,
    DiffuseTerrain = 16,
    AdditiveMaskedEnvMetal = 17,
    TwoLayerDiffuseMod2x = 18,
    TwoLayerDiffuseMod2xNA = 19,
    TwoLayerDiffuseAlpha = 20,
    Lod = 21,
    Parallax = 22,
    DF_MoreTexture_Unknown = 23
}
