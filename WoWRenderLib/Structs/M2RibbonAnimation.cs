using System.Numerics;

namespace WoWRenderLib.Structs;

/// <summary>One parsed WotLK ribbon emitter and its first material/texture slot.</summary>
public sealed record M2RibbonAnimation(
    int BoneIndex,
    Vector3 Position,
    uint TextureFileDataId,
    uint TextureFlags,
    ushort MaterialFlags,
    ushort BlendMode,
    float EdgesPerSecond,
    float EdgeLifetime,
    float Gravity,
    ushort TextureRows,
    ushort TextureColumns,
    M2Track<Vector3> Color,
    M2Track<float> Alpha,
    M2Track<float> HeightAbove,
    M2Track<float> HeightBelow,
    M2Track<float> TextureSlot,
    M2Track<float> Visibility);
