using System.Numerics;

namespace WoWRenderLib.Structs;

public readonly record struct M2RibbonVertex(Vector3 Position, Vector2 TexCoord, Vector4 Color);

public readonly record struct M2RibbonMesh(M2RibbonVertex[] Vertices, ushort[] Indices)
{
    public static M2RibbonMesh Empty => new([], []);
}

/// <summary>
/// Reconstructs a ribbon from absolute time. Each edge samples the bone and
/// appearance at its own spawn time, so the result does not depend on render cadence.
/// </summary>
public static class M2RibbonMeshBuilder
{
    private const int MaxEdges = 4096;
    private const float DefaultHeight = 10f;

    public static M2RibbonMesh Build(
        M2Animation animation,
        M2RibbonAnimation ribbon,
        int sequenceIndex,
        double timeMilliseconds)
    {
        if ((uint)ribbon.BoneIndex >= animation.Bones.Length ||
            ribbon.TextureRows == 0 || ribbon.TextureColumns == 0)
            return M2RibbonMesh.Empty;

        sequenceIndex = animation.ResolveSequenceIndex(sequenceIndex);
        var sequence = (uint)sequenceIndex < animation.Sequences.Length
            ? animation.Sequences[sequenceIndex] : default;
        if (ribbon.Visibility.Sample(sequenceIndex, sequence, animation.GlobalLoops,
                timeMilliseconds, 1f, float.Lerp) == 0f)
            return M2RibbonMesh.Empty;

        var rate = MathF.Ceiling(ribbon.EdgesPerSecond);
        var lifetime = MathF.Max(ribbon.EdgeLifetime, 0.25f);
        if (!float.IsFinite(rate) || !float.IsFinite(lifetime) || rate <= 0f)
            return M2RibbonMesh.Empty;
        var product = (double)rate * lifetime;
        if (!double.IsFinite(product) || product < 2d)
            return M2RibbonMesh.Empty;
        var edges = (int)Math.Min(MaxEdges, Math.Floor(product));
        if (edges < 2)
            return M2RibbonMesh.Empty;

        var slotValue = ribbon.TextureSlot.Sample(sequenceIndex, sequence,
            animation.GlobalLoops, timeMilliseconds, 0f, float.Lerp);
        if (!float.IsFinite(slotValue) || slotValue < 0f ||
            (double)slotValue > uint.MaxValue)
            return M2RibbonMesh.Empty;
        var slot = (uint)slotValue;
        var tileU = 1f / ribbon.TextureColumns;
        var tileV = 1f / ribbon.TextureRows;
        var uMin = (slot % ribbon.TextureColumns) * tileU;
        var vMin = ((slot / ribbon.TextureColumns) % ribbon.TextureRows) * tileV;

        var vertices = new M2RibbonVertex[edges * 2];
        var indices = new ushort[(edges - 1) * 6];
        var rigidChain = animation.HasRigidBoneChain(ribbon.BoneIndex);
        var movingRigidChain = rigidChain && animation.HasAnimatedBones &&
            animation.HasTimeDependentBoneChain(ribbon.BoneIndex);
        var staticBone = rigidChain && !movingRigidChain
            ? animation.EvaluateRigidBone(ribbon.BoneIndex, sequenceIndex, 0d)
            : Matrix4x4.Identity;
        var palette = rigidChain ? null : new Matrix4x4[animation.Bones.Length];
        if (palette is not null && !animation.HasAnimatedBones)
            animation.Evaluate(sequenceIndex, 0, palette);
        var now = Math.Max(0d, timeMilliseconds);
        for (var edge = 0; edge < edges; edge++)
        {
            var age = edge / rate;
            var spawnTime = Math.Max(0d, now - age * 1000d);
            Matrix4x4 matrix;
            if (rigidChain)
                matrix = movingRigidChain
                    ? animation.EvaluateRigidBone(ribbon.BoneIndex,
                        sequenceIndex, spawnTime)
                    : staticBone;
            else
            {
                if (animation.HasAnimatedBones)
                    animation.Evaluate(sequenceIndex, spawnTime, palette!);
                matrix = palette![ribbon.BoneIndex];
            }
            var origin = Vector3.Transform(ribbon.Position, matrix);
            origin.Z += ribbon.Gravity * age * age;
            var yAxis = new Vector3(matrix.M21, matrix.M22, matrix.M23);
            var above = ribbon.HeightAbove.Sample(sequenceIndex, sequence,
                animation.GlobalLoops, spawnTime, DefaultHeight, float.Lerp);
            var below = ribbon.HeightBelow.Sample(sequenceIndex, sequence,
                animation.GlobalLoops, spawnTime, DefaultHeight, float.Lerp);
            var rgb = ribbon.Color.Sample(sequenceIndex, sequence,
                animation.GlobalLoops, spawnTime, Vector3.One, Vector3.Lerp);
            var alpha = ribbon.Alpha.Sample(sequenceIndex, sequence,
                animation.GlobalLoops, spawnTime, 1f, float.Lerp);
            var color = new Vector4(rgb, alpha);
            var u = uMin + age * tileU / lifetime;
            vertices[edge * 2] = new M2RibbonVertex(
                origin - below * yAxis, new Vector2(u, vMin), color);
            vertices[edge * 2 + 1] = new M2RibbonVertex(
                origin + above * yAxis, new Vector2(u, vMin + tileV), color);

            if (edge + 1 == edges)
                continue;
            var offset = edge * 6;
            var baseVertex = edge * 2;
            indices[offset] = (ushort)baseVertex;
            indices[offset + 1] = (ushort)(baseVertex + 1);
            indices[offset + 2] = (ushort)(baseVertex + 2);
            indices[offset + 3] = (ushort)(baseVertex + 1);
            indices[offset + 4] = (ushort)(baseVertex + 3);
            indices[offset + 5] = (ushort)(baseVertex + 2);
        }
        return new M2RibbonMesh(vertices, indices);
    }
}
