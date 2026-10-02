using System.Numerics;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>12340's retained-viewer-group stage of the camera WMO fog query.</summary>
internal static class Wrath335WmoInteriorFogQuery
{
    internal static bool TryEvaluate(in WorldModel wmo, WmoViewerGroups viewer,
        Vector3 eyeLocal, Wrath335FogState outdoor, float farClip, bool expansionMode,
        out Wrath335FogState current)
    {
        current = outdoor;
        if (!wmo.wrath335 || !wmo.legacyLighting || wmo.fogs is not { Length: > 1 } ||
            wmo.groupBatches == null)
            return false;

        var hasInterior = false;
        var portalDistance = Wrath335PortalFogDistance.MaximumDistance;
        // 0x7A11BE..0x7A11F9 visits both retained groups. Only strict MOGP
        // interiors contribute; exterior-lit groups do not activate MFOG.
        for (var index = 0; index < 2; index++)
        {
            var groupIndex = index == 0 ? viewer.PrimaryGroupIndex : viewer.SecondaryGroupIndex;
            if ((uint)groupIndex >= (uint)wmo.groupBatches.Length ||
                (wmo.groupBatches[groupIndex].flags & Wrath335PortalFogDistance.ExteriorGroupFlags) != 0)
                continue;
            hasInterior = true;
            if (Wrath335PortalFogDistance.TryFind(wmo, groupIndex, eyeLocal, out var distance))
                portalDistance = MathF.Min(portalDistance, distance);
        }
        if (!hasInterior)
            return false;

        // The volume stage deliberately samples the FIRST group's MOGP IDs
        // (0x7A12B0), even when the second group supplied interior eligibility.
        var fogIds = (uint)viewer.PrimaryGroupIndex < (uint)wmo.groupBatches.Length
            ? wmo.groupBatches[viewer.PrimaryGroupIndex].fogIds : null;
        if (!Wrath335WmoFogVolumes.TryEvaluate(wmo.fogs, fogIds ?? [], eyeLocal, out var volume))
            return false;
        var target = Wrath335InteriorFog.EvaluateTarget(volume.Fog, farClip, expansionMode);
        current = Wrath335InteriorFog.BlendPortal(outdoor, target, portalDistance);
        return true;
    }
}
