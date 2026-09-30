using System.Numerics;
using System.Runtime.InteropServices;
using WoWRenderLib.DX11.Objects;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>Instance stream shared by scene models and sky models. W=0 retains scene lighting.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct M2InstanceData
{
    public Matrix4x4 World;
    public Vector4 Ambient;
    public Vector4 Diffuse;
    public Vector4 Direction;

    internal static M2InstanceData ForScene(M2Container instance, Matrix4x4 world)
    {
        var result = new M2InstanceData { World = world };
        if (instance.ParentWMO is not { } parent)
            return result;
        return ForWmo(parent.GetWMO(), instance.WmoDoodadIndex, world);
    }

    internal static M2InstanceData ForWmo(in Structs.WorldModel model, int doodadIndex, Matrix4x4 world)
    {
        var result = new M2InstanceData { World = world };
        if (!model.wrath335 || !model.legacyLighting ||
            model.doodadLighting == null || (uint)doodadIndex >= (uint)model.doodadLighting.Length)
            return result;
        var lighting = model.doodadLighting[doodadIndex];
        if (!lighting.Referenced)
            return result;
        if (!lighting.Interior)
        {
            result.Ambient.W = 2; // Sunlight, with the 12340 per-material lit gate.
            return result;
        }
        result.Ambient = new(lighting.Ambient, 1);
        result.Diffuse = new(lighting.Diffuse, 0);
        result.Direction = new(Vector3.Normalize(Wrath335WmoDoodadLighting.DirectionToLight), 0);
        return result;
    }
}
