using System.Numerics;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// Reads current retained placement/pose and loaded mesh identities. Preparation
/// supplies native eligibility, resolved shader/texture keys and any CM2 parent
/// distance; a WMO transform parent is not a CM2 attachment-distance parent.
/// This adapter does not submit draws or change global mesh/effect/water order.
/// </summary>
internal static class Wrath335M2RetainedMeshSortAdapter
{
    internal static bool TryBuild(in ParsedDoodadBatch model, M2InstancePacket packet,
        int instanceIndex, int batchIndex, M2AnimationPose? pose, in Matrix4x4 view,
        bool translucent, bool zFillEligible, bool projected,
        Wrath335M2ShaderSortKey? shader, ReadOnlyMemory<uint> textureIdentities,
        ushort materialBlendMode, uint elementFlags, out Wrath335M2ElementSortData element,
        float? parentDistance = null, bool parentCreationFlag1 = false)
    {
        element = default;
        if (model.submeshes is null || (uint)batchIndex >= model.submeshes.Length ||
            (uint)instanceIndex >= packet.Instances.Count ||
            (uint)instanceIndex >= packet.WorldMatrices.Length)
            return false;
        var batch = model.submeshes[batchIndex];
        var modelToView = packet.WorldMatrices[instanceIndex] * view;
        var modelDistance = Wrath335M2MeshSortInputAdapter.ModelDistance(
            modelToView, parentDistance, parentCreationFlag1);
        scoped ReadOnlySpan<Matrix4x4> bones = pose is null ? [] : pose.BoneModelMatrices;
        Span<Matrix4x4> rootBone = stackalloc Matrix4x4[1];
        if (pose is null && model.animation?.Wrath335UsesSimpleAnimation == true)
        {
            // Native simple animation writes only entry zero (0x82E495..0x82E4A2).
            // A model-space identity produces that full placement/view root.
            // Higher center indices remain invalid; missing complex poses still fail.
            rootBone[0] = Matrix4x4.Identity;
            bones = rootBone;
        }
        if (!Wrath335M2MeshSortInputAdapter.TryBuild(batch, modelToView,
            bones, modelDistance,
            translucent, zFillEligible, projected, out var inputs))
            return false;
        var identities = Wrath335M2RetainedSortIdentities.Mesh(model.submeshes, batchIndex);
        element = new()
        {
            Kind = projected ? Wrath335M2ElementKind.ProjectedMesh : Wrath335M2ElementKind.Mesh,
            Distance = inputs.Distance,
            Flags = elementFlags,
            PriorityPlane = inputs.PriorityPlane,
            MaterialLayer = inputs.MaterialLayer,
            SectionBoneComboIndex = inputs.SectionBoneComboIndex,
            ModelIdentity = Wrath335M2RetainedSortIdentities.Model(packet.Instances[instanceIndex]),
            SharedModelIdentity = identities.Shared,
            BatchIdentity = identities.Batch,
            Shader = shader,
            BlendMode = materialBlendMode,
            MaterialFlags = batch.renderFlags,
            TextureIdentities = textureIdentities
        };
        return true;
    }
}
