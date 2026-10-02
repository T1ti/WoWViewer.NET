using System.Numerics;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// Prepares one native-profile mesh from decoded data, retained placement/pose,
/// and current resolved effect/texture/water inputs. Callers retain borrowed
/// resource-key storage through sorting. Submission and ZFill cloning follow
/// separately; this does not invent missing complex poses or shader resources.
/// </summary>
internal static class Wrath335M2MeshQueuePreparation
{
    internal static bool TryAdd(Wrath335M2ElementQueues queues, in ParsedDoodadBatch model,
        M2InstancePacket packet, int instanceIndex, int batchIndex, M2AnimationPose? pose,
        in Matrix4x4 view, float materialAlpha, float modelAlpha,
        Wrath335M2EffectSortTables? effect, ReadOnlyMemory<uint> textureIdentities,
        in Wrath335M2ShaderEnvironment shaderEnvironment, Vector4 viewWaterPlane,
        uint cacheFlags, bool sceneBelowSide, bool projected = false,
        bool zFillEligible = false, uint elementFlags = 0, float? parentDistance = null,
        bool parentCreationFlag1 = false)
    {
        if (model.submeshes is null || (uint)batchIndex >= model.submeshes.Length ||
            (uint)instanceIndex >= packet.WorldMatrices.Length ||
            model.wrath335Bounds is not { } bounds || effect is not { } tables)
            return false;
        var batch = model.submeshes[batchIndex];
        if (batch.wrath335Sort is not { } metadata || metadata.ShaderId == 0x8000)
            return false; // 0x821E97/0x821F97: exact sentinel and missing effect are independent gates.
        var alpha = (float)((double)materialAlpha * modelAlpha);
        var material = Wrath335M2FadeMaterial.Resolve((int)batch.blendType,
            true, batch.renderFlags, materialAlpha, true, modelAlpha, batch.baseBlendType);
        if (!material.Draw) return false;
        var selection = Wrath335M2ShaderSelectors.Select((ushort)batch.blendType,
            batch.renderFlags, metadata.BoneInfluences, alpha, projected, shaderEnvironment);
        if (!tables.TryResolve(selection, out var shader)) return false;
        var modelToView = packet.WorldMatrices[instanceIndex] * view;
        var water = Wrath335M2WaterQueues.SelectModel(shaderEnvironment.LightingFlags,
            bounds.Minimum, bounds.Maximum, bounds.Radius, modelToView,
            viewWaterPlane, (cacheFlags & 2) != 0, sceneBelowSide);
        var route = Wrath335M2WaterQueues.RouteMesh(material.Translucent, projected, water);
        if (route.Queues == Wrath335M2QueueMask.None) return false;
        elementFlags = (elementFlags & ~2u) | (route.ClipWaterPlane ? 2u : 0u);
        if (!Wrath335M2RetainedMeshSortAdapter.TryBuild(model, packet, instanceIndex,
            batchIndex, pose, view, material.Translucent, zFillEligible, projected,
            shader, textureIdentities, (ushort)batch.blendType, elementFlags, out var element,
            parentDistance, parentCreationFlag1)) return false;
        queues.Add(element, route.Queues);
        return true;
    }
}
