using System.Numerics;

namespace WoWRenderLib.Structs;

/// <summary>A lifetime curve whose ushort keys span the particle's 0..32767 life ratio.</summary>
public sealed record M2ParticleLifeTrack<T>(ushort[] Times, T[] Values)
{
    public bool HasKeys
    {
        get
        {
            if (Times.Length == 0 || Times.Length != Values.Length)
                return false;
            for (var i = 1; i < Times.Length; i++)
                if (Times[i] < Times[i - 1])
                    return false;
            return true;
        }
    }

    public T Sample(float ratio, T fallback, Func<T, T, float, T> interpolate)
    {
        if (!HasKeys) return fallback;
        var target = Math.Clamp(ratio, 0f, 1f) * 32767f;
        if (target <= Times[0] || Times.Length == 1) return Values[0];
        for (var i = 1; i < Times.Length; i++)
        {
            if (target > Times[i]) continue;
            var span = Times[i] - Times[i - 1];
            var amount = span > 0 ? (target - Times[i - 1]) / span : 0f;
            return interpolate(Values[i - 1], Values[i], amount);
        }
        return Values[^1];
    }
}

/// <summary>WotLK particle data retained after WowLib releases the M2 file.</summary>
public sealed record M2ParticleAnimation(
    int SourceIndex,
    int BoneIndex,
    Vector3 Position,
    uint TextureFileDataId,
    uint TextureFlags,
    uint Flags,
    byte BlendMode,
    byte EmitterType,
    ushort ColorIndex,
    short PriorityPlane,
    ushort Rows,
    ushort Columns,
    bool HasChildModels,
    float LifespanVariation,
    float EmissionRateVariation,
    Vector2 ScaleVariation,
    float Drag,
    float BaseSpin,
    float BaseSpinVariation,
    float SpinSpeed,
    float SpinSpeedVariation,
    float TwinkleSpeed,
    float TwinklePercent,
    Vector2 TwinkleScale,
    float TailLength,
    Vector3 Wind,
    float WindTime,
    M2Track<float> EmissionSpeed,
    M2Track<float> SpeedVariation,
    M2Track<float> VerticalRange,
    M2Track<float> HorizontalRange,
    M2Track<float> Gravity,
    M2Track<float> Lifespan,
    M2Track<float> EmissionRate,
    M2Track<float> AreaWidth,
    M2Track<float> AreaLength,
    M2Track<float> ZSource,
    M2Track<float> Enabled,
    M2ParticleLifeTrack<Vector3> Color,
    M2ParticleLifeTrack<float> Alpha,
    M2ParticleLifeTrack<Vector2> Scale,
    M2ParticleLifeTrack<float> HeadCell,
    M2ParticleLifeTrack<float> TailCell);
