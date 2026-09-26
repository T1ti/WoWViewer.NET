using System.Numerics;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Structs;

internal readonly record struct WorldLiquidCullBounds(BoundingBox Box, BoundingSphere Sphere)
{
    public static WorldLiquidCullBounds FromBox(BoundingBox box) =>
        new(box, new BoundingSphere(box.Center, Vector3.Distance(box.Center, box.Max)));

    public static WorldLiquidCullBounds Transform(BoundingBox box, Matrix4x4 matrix) =>
        FromBox(BoundingBox.Transform(box, matrix));
}
