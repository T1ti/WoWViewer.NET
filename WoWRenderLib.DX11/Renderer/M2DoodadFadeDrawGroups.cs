using WoWRenderLib.DX11.Objects;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>Retains pose ownership and isolates native faded instances from opaque instancing.
/// 0x822057 excludes translucent doodads from the native type-2 instanced path.</summary>
internal sealed class M2DoodadFadeDrawGroups
{
    private readonly List<M2AnimationDrawGroup> pool = [];
    private readonly List<M2AnimationDrawGroup> groups = [];

    public IReadOnlyList<M2AnimationDrawGroup> Build(IReadOnlyList<M2AnimationDrawGroup> source,
        IReadOnlyList<M2Container> instances, long frame, Submesh[]? materials = null)
    {
        groups.Clear();
        foreach (var original in source)
        {
            M2AnimationDrawGroup? baseline = null, nativeOpaque = null;
            var materialFade = false;
            foreach (var material in original.Pose?.Materials ?? [])
                materialFade |= material.Color.W < Wrath335M2FadeMaterial.OpaqueThreshold;
            if (materials is not null)
                for (var i = 0; i < materials.Length && !materialFade; i++)
                {
                    var batch = materials[i];
                    materialFade = Wrath335M2FadeMaterial.Resolve((int)batch.blendType, false,
                        batch.renderFlags, original.Pose?.Materials[i].Color.W ?? 1f, true,
                        1, batch.baseBlendType).Translucent;
                }
            foreach (var index in original.Indices)
            {
                var instance = instances[index];
                var opacity = 1f;
                var native = instance.ParentWMO?.TryGetDoodadSubmissionOpacity(
                    instance.WmoDoodadIndex, frame, out opacity) == true;
                var individual = materialFade || opacity != 1f;
                var group = native
                    ? individual
                        ? Next(original.Pose, true, opacity)
                        : nativeOpaque ??= Next(original.Pose, true, opacity)
                    : baseline ??= Next(original.Pose, false, 1);
                group.AddInstance(index);
            }
        }
        return groups;
    }

    private M2AnimationDrawGroup Next(M2AnimationPose? pose, bool native, float opacity)
    {
        if (groups.Count == pool.Count) pool.Add(new());
        var result = pool[groups.Count];
        result.Reset(pose);
        result.NativeDoodadFade = native;
        result.DoodadOpacity = opacity;
        groups.Add(result);
        return result;
    }

    public static (bool Opaque, bool Translucent) Passes(IReadOnlyList<M2AnimationDrawGroup> groups,
        Submesh[] materials, bool legacyDepth)
    {
        var opaque = false;
        var translucent = false;
        foreach (var group in groups)
            for (var i = 0; i < materials.Length; i++)
            {
                var batch = materials[i];
                var state = Wrath335M2FadeMaterial.Resolve((int)batch.blendType, legacyDepth,
                    batch.renderFlags, group.Pose?.Materials[i].Color.W ?? 1f,
                    group.NativeDoodadFade, group.DoodadOpacity, batch.baseBlendType);
                if (!state.Draw) continue;
                if (state.Translucent) translucent = true; else opaque = true;
            }
        return (opaque, translucent);
    }
}
