using System.Numerics;

namespace WoWRenderLib.Structs;

/// <summary>One authored MFOG band. Color is the original packed client ARGB value.</summary>
public readonly record struct WmoFogBand(float End, float StartScalar, uint Color);

/// <summary>One wowlib-decoded WMO MFOG record in local WMO coordinates.</summary>
public readonly record struct WmoFogVolume(
    uint Flags,
    Vector3 Position,
    float SmallerRadius,
    float LargerRadius,
    WmoFogBand Fog,
    WmoFogBand UnderwaterFog);

/// <summary>
/// CMapObj::QueryViewerFog_PortalDist volume stage. The caller supplies the
/// first viewer group's four MOGP fog references and the local camera point.
/// The separate portal distance and liquid state are applied after this stage.
/// </summary>
public static class Wrath335WmoFogVolumes
{
    public const uint DisabledVolumeFlag = 1;
    public const int BaseFogIndex = 0;
    public const int NoFogReference = 0;
    public const int GroupFogReferenceCount = 4;
    private const int PackedColorMaximum = 255;
    private const int PackedColorShift = 8;

    public static bool TryEvaluate(ReadOnlySpan<WmoFogVolume> fogs,
        ReadOnlySpan<byte> groupFogIds, Vector3 viewerLocal,
        out WmoFogVolume result)
    {
        result = default;
        // The client skips interior fog handling for a root with one MFOG.
        if (fogs.Length <= 1)
            return false;

        result = fogs[BaseFogIndex];
        Span<(float Distance, int Index)> candidates =
            stackalloc (float, int)[GroupFogReferenceCount];
        var count = 0;
        for (var reference = 0;
             reference < Math.Min(groupFogIds.Length, GroupFogReferenceCount);
             reference++)
        {
            var index = groupFogIds[reference];
            if (index == NoFogReference || index >= fogs.Length)
                continue;
            var candidate = fogs[index];
            var distance = Vector3.Distance(viewerLocal, candidate.Position);
            if (distance >= candidate.LargerRadius ||
                (candidate.Flags & DisabledVolumeFlag) != 0)
                continue;
            candidates[count++] = (distance, index);
        }

        // The client priority queue pops the farthest candidate first.
        for (var i = 0; i < count; i++)
        {
            var farthest = i;
            for (var j = i + 1; j < count; j++)
                if (candidates[j].Distance > candidates[farthest].Distance)
                    farthest = j;
            (candidates[i], candidates[farthest]) =
                (candidates[farthest], candidates[i]);

            var (distance, index) = candidates[i];
            var volume = fogs[index];
            var width = volume.LargerRadius - volume.SmallerRadius;
            var weight = distance <= volume.SmallerRadius ? 1f
                : width > 0f ? Math.Clamp((volume.LargerRadius - distance) / width, 0f, 1f)
                : 0f;
            result = result with
            {
                Fog = Blend(result.Fog, volume.Fog, weight),
                UnderwaterFog = Blend(result.UnderwaterFog,
                    volume.UnderwaterFog, weight),
                Flags = i == count - 1 ? volume.Flags : result.Flags
            };
        }
        return true;
    }

    private static WmoFogBand Blend(WmoFogBand current, WmoFogBand target,
        float weight)
    {
        var alpha = (int)(PackedColorMaximum * weight);
        return new WmoFogBand(
            current.End + (target.End - current.End) * weight,
            current.StartScalar + (target.StartScalar - current.StartScalar) * weight,
            LerpPackedRgb(current.Color, target.Color, alpha));
    }

    internal static uint LerpPackedRgb(uint current, uint target, int alpha)
    {
        if (alpha == PackedColorMaximum)
            return (current & 0xFF000000u) | (target & 0x00FFFFFFu);
        uint output = current & 0xFF000000u;
        for (var shift = 0; shift <= 16; shift += PackedColorShift)
        {
            var from = (int)((current >> shift) & 0xFF);
            var to = (int)((target >> shift) & 0xFF);
            var blended = (byte)(from + ((alpha * (to - from)) >> PackedColorShift));
            output |= (uint)blended << shift;
        }
        return output;
    }
}
