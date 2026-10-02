using System.Numerics;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

internal readonly record struct Wrath335M2MeshSortInputs(
    Wrath335M2DistanceKeys Distance, int PriorityPlane, ushort MaterialLayer, ushort SectionBoneComboIndex);

/// <summary>
/// Adapts decoded 12340 metadata and caller-supplied retained transforms to CPU
/// ordering inputs. Bone matrices are in model space, before world/view; callers
/// supply the evaluated pose including any billboard adjustment. This does not
/// choose identity tokens, infer native eligibility or enqueue GPU draws.
/// </summary>
internal static class Wrath335M2MeshSortInputAdapter
{
    internal static float ModelDistance(in Matrix4x4 modelToView,
        float? parentDistance = null, bool parentCreationFlag1 = false) =>
        Wrath335M2ElementOrdering.ModelDistance(
            new(modelToView.M41, modelToView.M42, modelToView.M43), parentDistance, parentCreationFlag1);

    /// <summary>
    /// Returns false for absent client metadata or a missing center bone on a
    /// translucent batch. Opaque batches do not read a center bone. Missing
    /// matrices never silently become identity; malformed-client behavior is unclaimed.
    /// Reads current transforms each call, without caching across pose versions.
    /// </summary>
    internal static bool TryBuild(in Submesh batch, in Matrix4x4 modelToView,
        ReadOnlySpan<Matrix4x4> boneModelMatrices, float modelDistance,
        bool translucent, bool zFillEligible, bool projected, out Wrath335M2MeshSortInputs inputs)
    {
        inputs = default;
        if (batch.wrath335Sort is not { } metadata)
            return false;
        Wrath335M2DistanceKeys distance;
        if (!translucent)
        {
            distance = new(modelDistance, modelDistance);
        }
        else
        {
            if (metadata.CenterBoneIndex >= boneModelMatrices.Length)
                return false;
            // 0x822120..0x822144: selected center bone transforms the sort center.
            var boneToView = boneModelMatrices[metadata.CenterBoneIndex] * modelToView;
            var center = Vector3.Transform(metadata.SortCenter, boneToView);
            float radius = 0;
            if (!metadata.RawDistance && (metadata.BatchFlags & 3) != 0)
            {
                // 0x8221B2..0x8221CA / 0x8222A1..0x8222B9 use the first axis,
                // not max-axis scale or a bounding sphere rebuilt from vertices.
                double x = boneToView.M11, y = boneToView.M12, z = boneToView.M13;
                radius = (float)(metadata.SortRadius * Math.Sqrt(x * x + y * y + z * z));
            }
            distance = Wrath335M2ElementOrdering.MeshDistance(center, radius,
                metadata.BatchFlags, modelDistance, true, metadata.RawDistance,
                zFillEligible, projected, (batch.renderFlags & 0x10) != 0);
        }
        inputs = new(distance, metadata.PriorityPlane, metadata.MaterialLayer, metadata.SectionBoneComboIndex);
        return true;
    }
}
