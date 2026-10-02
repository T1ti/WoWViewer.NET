using System.Numerics;

namespace WoWRenderLib.DX11.Renderer;

[Flags]
internal enum Wrath335M2QueueMask
{
    None = 0,
    Opaque = 1,
    AboveWater = 2,
    BelowWater = 4
}

internal readonly record struct Wrath335M2WaterSelection(bool Above, bool Below);
internal readonly record struct Wrath335M2EntityWaterLighting(uint Flags, Vector4 Plane);
internal readonly record struct Wrath335M2MeshQueueRoute(Wrath335M2QueueMask Queues, bool ClipWaterPlane);
internal readonly record struct Wrath335M2WaterPassOrder(Wrath335M2QueueMask BeforeLiquid,
    Wrath335M2QueueMask AfterLiquid);

/// <summary>
/// Build 12340 CPU water partition. Lighting flags/plane and runtime particle
/// flags are explicit native inputs; authored emitter flags are a different field.
/// M2MeshRenderer consumes the mesh partition and its signed clipping plane.
/// </summary>
internal static class Wrath335M2WaterQueues
{
    // 0x7C10C0..0x7C112D: inputs are post-query entity +0x7C flags and +0x80 height.
    // Preserve unrelated lighting flags and the plane unless both entity bits are set.
    // The result plane is world-space; the normal camera-space stage follows separately.
    internal static Wrath335M2EntityWaterLighting EntityLighting(uint entityFlags,
        float waterHeight, uint lightingFlags, Vector4 retainedPlane)
    {
        if ((entityFlags & 0x20) == 0)
            return new((lightingFlags & ~0x60u) | 0x20, retainedPlane);
        if ((entityFlags & 0x40) == 0)
            return new((lightingFlags & ~0x60u) | 0x40, retainedPlane);
        return new(lightingFlags | 0x60, new(0, 0, 1, -waterHeight));
    }

    // Authored +4 bit 0x2000 -> runtime emitter +0x134 bit 0x40000 at 0x833D93..0x833D9C.
    // This supplies the initial water routing bit, not the entire runtime flags word.
    internal static uint InitialParticleWaterFlags(uint authoredFlags) =>
        (authoredFlags & 0x2000) != 0 ? 0x40000u : 0;

    // 0x821CAB..0x821DDA: refine only lighting flags 0x60, using the authored
    // bounds midpoint and radius times the first model-to-view axis length.
    internal static Wrath335M2WaterSelection SelectModel(uint lightingFlags,
        Vector3 boundsMinimum, Vector3 boundsMaximum, float boundsRadius,
        in Matrix4x4 modelToView, Vector4 viewPlane, bool splitCrossingModels,
        bool sceneBelowSide)
    {
        var selection = new Wrath335M2WaterSelection((lightingFlags & 0x20) != 0,
            (lightingFlags & 0x40) != 0);
        if (!selection.Above || !selection.Below) return selection;
        var (distance, radius) = SpherePlane(boundsMinimum, boundsMaximum,
            boundsRadius, modelToView, viewPlane);
        selection = new(distance >= -radius, distance <= radius);
        // Cache bit 2 permits both sides. Scene +0x140 is the viewer liquid ID
        // copied during update (0x7834F3), before the render query refreshes it.
        if (selection.Above && selection.Below && !splitCrossingModels)
            selection = new(!sceneBelowSide, sceneBelowSide);
        return selection;
    }

    // 0x822ACF..0x822BBC: particles compute their own above predicate, without
    // the mesh crossing collapse controlled by cache bit 2 / scene +0x140.
    internal static bool SelectParticleAbove(uint lightingFlags, Vector3 boundsMinimum,
        Vector3 boundsMaximum, float boundsRadius, in Matrix4x4 modelToView, Vector4 viewPlane)
    {
        if ((lightingFlags & 0x60) != 0x60) return (lightingFlags & 0x20) != 0;
        var (distance, radius) = SpherePlane(boundsMinimum, boundsMaximum,
            boundsRadius, modelToView, viewPlane);
        return distance >= -radius;
    }

    private static (double Distance, float Radius) SpherePlane(Vector3 minimum,
        Vector3 maximum, float authoredRadius, in Matrix4x4 modelToView, Vector4 plane)
    {
        var center = Vector3.Transform(new Vector3(
            (float)(((double)minimum.X + maximum.X) * 0.5),
            (float)(((double)minimum.Y + maximum.Y) * 0.5),
            (float)(((double)minimum.Z + maximum.Z) * 0.5)), modelToView);
        var radius = (float)(authoredRadius * Math.Sqrt(
            (double)modelToView.M11 * modelToView.M11 +
            (double)modelToView.M12 * modelToView.M12 +
            (double)modelToView.M13 * modelToView.M13));
        return ((double)center.Y * plane.Y + (double)center.Z * plane.Z +
            (double)center.X * plane.X + plane.W, radius);
    }

    // 0x8350A0: transform a point on the plane and its normal, normalize only
    // above squared length 2^-22, then reconstruct d. Caller supplies view matrix.
    internal static Vector4 PlaneToView(Vector4 worldPlane, in Matrix4x4 view)
    {
        var normal = Vector3.TransformNormal(new(worldPlane.X, worldPlane.Y, worldPlane.Z), view);
        var lengthSquared = (double)normal.X * normal.X + (double)normal.Y * normal.Y +
            (double)normal.Z * normal.Z;
        if (lengthSquared > BitConverter.Int32BitsToSingle(0x34800000))
        {
            var inverseLength = 1 / Math.Sqrt(lengthSquared);
            normal = new((float)(normal.X * inverseLength), (float)(normal.Y * inverseLength),
                (float)(normal.Z * inverseLength));
        }
        var point = Vector3.Transform(new Vector3(-worldPlane.W * worldPlane.X,
            -worldPlane.W * worldPlane.Y, -worldPlane.W * worldPlane.Z), view);
        var offset = (float)(-((double)point.Z * normal.Z + (double)point.Y * normal.Y +
            (double)point.X * normal.X));
        return new(normal, offset);
    }

    // 0x822403..0x82246F / projected branch 0x822408: opaque is shared;
    // projected transparent meshes choose below if selected, otherwise above.
    internal static Wrath335M2MeshQueueRoute RouteMesh(bool translucent, bool projected,
        Wrath335M2WaterSelection selection)
    {
        if (!translucent) return new(Wrath335M2QueueMask.Opaque, false);
        if (projected) return new(selection.Below ? Wrath335M2QueueMask.BelowWater :
            Wrath335M2QueueMask.AboveWater, false);
        var queues = (selection.Above ? Wrath335M2QueueMask.AboveWater : 0) |
            (selection.Below ? Wrath335M2QueueMask.BelowWater : 0);
        return new(queues, selection.Above && selection.Below);
    }

    // 0x822733..0x82285D: first ribbon material and composed model/track alpha.
    internal static Wrath335M2QueueMask RouteRibbon(ushort firstMaterialBlend,
        float composedAlpha, bool modelAbove) =>
        firstMaterialBlend <= 1 && composedAlpha >= Wrath335M2FadeMaterial.OpaqueThreshold
            ? Wrath335M2QueueMask.Opaque : Side(modelAbove);

    // 0x8219C8..0x821A16: runtime +0xD0 is a signed resolved Gx blend;
    // runtime +0x134 bit 0x40000 forces below only after opaque classification.
    internal static Wrath335M2QueueMask RouteParticle(int resolvedBlend, float composedAlpha,
        bool particleAbove, uint runtimeFlags) =>
        resolvedBlend <= 1 && composedAlpha >= Wrath335M2FadeMaterial.OpaqueThreshold
            ? Wrath335M2QueueMask.Opaque : Side(particleAbove && (runtimeFlags & 0x40000) == 0);

    // 0x82294D..0x8229E0: callback model runtime flag 0x20 selects opaque.
    internal static Wrath335M2QueueMask RouteCallback(bool opaque, bool modelAbove) =>
        opaque ? Wrath335M2QueueMask.Opaque : Side(modelAbove);

    private static Wrath335M2QueueMask Side(bool above) =>
        above ? Wrath335M2QueueMask.AboveWater : Wrath335M2QueueMask.BelowWater;

    // WorldRender 0x4F8EA0 branches on viewer liquid ID, not depth.
    internal static Wrath335M2WaterPassOrder PassOrder(uint viewerLiquidId) =>
        viewerLiquidId == 0 ? new(Wrath335M2QueueMask.BelowWater, Wrath335M2QueueMask.AboveWater)
            : new(Wrath335M2QueueMask.AboveWater, Wrath335M2QueueMask.BelowWater);
}
