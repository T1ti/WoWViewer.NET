using System.Numerics;

namespace WoWRenderLib.DX11.Renderer;

internal enum Wrath335M2ElementKind
{
    Mesh,
    ProjectedMesh,
    DoodadRun,
    Ribbon,
    ParticleRun,
    Callback
}

internal readonly record struct Wrath335M2DistanceKeys(float Primary, float Secondary);
internal readonly record struct Wrath335M2ShaderSortKey(uint Vertex, uint Pixel);

/// <summary>
/// Decoded sort inputs for build 12340. Identity values model native unsigned
/// address ordering; texture identities use native wrapped pointer subtraction.
/// They are never FileDataIDs. Live DX11 identity adaptation is a separate contract.
/// Texture storage is borrowed and must remain unchanged while a queue is sorted.
/// </summary>
internal readonly record struct Wrath335M2ElementSortData
{
    public Wrath335M2ElementKind Kind { get; init; }
    public Wrath335M2DistanceKeys Distance { get; init; }
    public uint Flags { get; init; }
    public int PriorityPlane { get; init; }
    public uint ModelIdentity { get; init; }
    public Wrath335M2ShaderSortKey? Shader { get; init; }
    public ushort MaterialLayer { get; init; }
    public uint SharedModelIdentity { get; init; }
    public ushort SectionBoneComboIndex { get; init; }
    public ushort BlendMode { get; init; }
    public ushort MaterialFlags { get; init; }
    public ReadOnlyMemory<uint> TextureIdentities { get; init; }
    public uint BatchIdentity { get; init; }
    public uint RibbonIndex { get; init; }
    public int ParticleBlendMode { get; init; }
    public uint ParticleFlags { get; init; }
    public uint ParticleTextureIdentity { get; init; }
    public uint AdditiveGroup { get; init; }
}

/// <summary>
/// CPU-only 12340 key/comparator/heap contract. Not yet an adapter for live scene
/// queues. Retained adapters and queue policies are separate from GPU submission.
/// </summary>
internal static class Wrath335M2ElementOrdering
{
    // 0x822173/0x822260: compare squared center length to bits 0x34800000.
    private static readonly float NormalizeThreshold = BitConverter.Int32BitsToSingle(0x34800000);

    // 0x82E36A..0x82E3A2 and 0x82F31D..0x82F354.
    internal static float ModelDistance(Vector3 viewOrigin, float? parentDistance = null,
        bool parentCreationFlag1 = false) =>
        parentDistance.HasValue && !parentCreationFlag1
            ? parentDistance.Value
            : (float)SquaredLength(viewOrigin.X, viewOrigin.Y, viewOrigin.Z);

    // Inputs already include the selected section center-bone/view transform and
    // radius scaling. Eligibility is supplied by preparation, never inferred here.
    // 0x8220CF..0x82236B. Opaque elements and raw-distance flag bypass signed/radius rules.
    internal static Wrath335M2DistanceKeys MeshDistance(Vector3 viewCenter, float scaledRadius,
        byte batchFlags, float modelDistance, bool translucent, bool rawDistance,
        bool zFillEligible, bool projected, bool depthWriteDisabled)
    {
        if (!translucent)
            return new(modelDistance, modelDistance);
        double x = viewCenter.X, y = viewCenter.Y, z = viewCenter.Z;
        var length = SquaredLength(x, y, z);
        if (rawDistance)
            return new(modelDistance, (float)length);

        // Bit 1 wins if both near/far flags are set. Tiny centers retain their
        // unnormalized direction, matching the native threshold branch.
        if ((batchFlags & 3) != 0)
        {
            var inverseLength = length > NormalizeThreshold ? 1 / Math.Sqrt(length) : 1;
            var radius = (batchFlags & 1) != 0 ? -scaledRadius : scaledRadius;
            x += viewCenter.X * inverseLength * radius;
            y += viewCenter.Y * inverseLength * radius;
            z += viewCenter.Z * inverseLength * radius;
        }
        var signedDistance = (float)(z < 0 ? -SquaredLength(x, y, z) : SquaredLength(x, y, z));
        var primary = zFillEligible && !projected && !depthWriteDisabled ? modelDistance : signedDistance;
        return new(primary, signedDistance);
    }

    private static double SquaredLength(double x, double y, double z) => x * x + y * y + z * z;

    // 0x822C46..0x822CA7: bone-transformed emitter origin in view space,
    // squared without a sign/radius adjustment. Child emitters reuse this key.
    internal static Wrath335M2DistanceKeys ParticleDistance(Vector3 viewEmitterOrigin,
        float modelDistance) => new(modelDistance,
            (float)SquaredLength(viewEmitterOrigin.X, viewEmitterOrigin.Y, viewEmitterOrigin.Z));

    internal static Wrath335M2DistanceKeys ParticleDistance(Vector3 emitterOrigin,
        in Matrix4x4 boneModelToView, float modelDistance) =>
        ParticleDistance(Vector3.Transform(emitterOrigin, boneModelToView), modelDistance);

    // 0x822712/0x82271B: ribbons use the whole-model key for both fields.
    internal static Wrath335M2DistanceKeys RibbonDistance(float modelDistance) =>
        new(modelDistance, modelDistance);

    // 0x81EF30..0x81F0D6, including the complete opaque fallback.
    internal static int CompareTransparent(in Wrath335M2ElementSortData a,
        in Wrath335M2ElementSortData b, bool groupShaders)
    {
        var result = DescendingDistance(a.Distance.Primary, b.Distance.Primary);
        if (result != 0) return result;
        result = (b.Flags & 1).CompareTo(a.Flags & 1);
        if (result != 0) return result;
        result = a.PriorityPlane.CompareTo(b.PriorityPlane);
        if (result != 0) return result;
        result = DescendingDistance(a.Distance.Secondary, b.Distance.Secondary);
        if (result != 0) return result;
        if (groupShaders && (a.Kind != b.Kind || a.ModelIdentity != b.ModelIdentity))
        {
            result = CompareShaders(a.Shader, b.Shader);
            if (result != 0) return result;
        }
        result = a.ModelIdentity.CompareTo(b.ModelIdentity);
        if (result != 0) return result;
        result = ((int)a.Kind).CompareTo((int)b.Kind);
        if (result != 0) return result;
        if ((int)a.Kind <= (int)Wrath335M2ElementKind.DoodadRun)
        {
            result = a.MaterialLayer.CompareTo(b.MaterialLayer);
            if (result != 0) return result;
        }
        if (groupShaders)
        {
            result = CompareShaders(a.Shader, b.Shader);
            if (result != 0) return result;
        }
        return CompareOpaque(a, b);
    }

    // Native x87 unordered comparisons fall through. float.CompareTo would
    // impose an extra NaN ordering absent from these instructions.
    private static int DescendingDistance(float a, float b) => b < a ? -1 : b > a ? 1 : 0;

    private static int CompareShaders(Wrath335M2ShaderSortKey? a, Wrath335M2ShaderSortKey? b)
    {
        if (!a.HasValue || !b.HasValue) return 0;
        var result = a.Value.Vertex.CompareTo(b.Value.Vertex);
        return result != 0 ? result : a.Value.Pixel.CompareTo(b.Value.Pixel);
    }

    // 0x81EEA0 dispatcher, 0x81EAD0 meshes, 0x81ED10 ribbons, 0x81EDF0 particles.
    internal static int CompareOpaque(in Wrath335M2ElementSortData a, in Wrath335M2ElementSortData b)
    {
        var result = ((int)a.Kind).CompareTo((int)b.Kind);
        if (result != 0) return result;
        switch (a.Kind)
        {
            case Wrath335M2ElementKind.Mesh:
                result = a.MaterialLayer.CompareTo(b.MaterialLayer);
                if (result != 0) return result;
                result = CompareShaders(a.Shader, b.Shader); // Independent of transparent grouping CVar.
                if (result != 0) return result;
                result = a.SharedModelIdentity.CompareTo(b.SharedModelIdentity);
                if (result != 0) return result;
                result = (a.Flags & 4).CompareTo(b.Flags & 4);
                if (result != 0) return result;
                result = a.ModelIdentity.CompareTo(b.ModelIdentity);
                if (result != 0) return result;
                result = a.SectionBoneComboIndex.CompareTo(b.SectionBoneComboIndex);
                if (result != 0) return result;
                goto case Wrath335M2ElementKind.ProjectedMesh;
            case Wrath335M2ElementKind.ProjectedMesh:
                result = a.BlendMode.CompareTo(b.BlendMode);
                if (result != 0) return result;
                result = (a.MaterialFlags & 0x1F).CompareTo(b.MaterialFlags & 0x1F);
                if (result != 0) return result;
                result = CompareTextures(a.TextureIdentities.Span, b.TextureIdentities.Span);
                return result != 0 ? result : a.BatchIdentity.CompareTo(b.BatchIdentity);
            case Wrath335M2ElementKind.Ribbon:
                result = CompareTextures(a.TextureIdentities.Span, b.TextureIdentities.Span);
                return result != 0 ? result : a.RibbonIndex.CompareTo(b.RibbonIndex);
            case Wrath335M2ElementKind.ParticleRun:
                result = a.ParticleBlendMode.CompareTo(b.ParticleBlendMode);
                if (result != 0) return result;
                result = ParticleMaterialFlags(a.ParticleFlags).CompareTo(ParticleMaterialFlags(b.ParticleFlags));
                return result != 0 ? result : PointerDifference(a.ParticleTextureIdentity, b.ParticleTextureIdentity);
            default:
                return 0; // Doodad runs/callbacks have no opaque tie-breaker here.
        }
    }

    private static uint ParticleMaterialFlags(uint flags) =>
        ((flags & 1) != 0 ? 4u : 5u) |
        ((flags & 2) != 0 ? 0u : 2u) |
        ((flags & 4) != 0 ? 0u : 0x10u);

    private static int CompareTextures(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var result = PointerDifference(a[i], b[i]);
            if (result != 0) return result;
        }
        return a.Length.CompareTo(b.Length);
    }

    // 0x47BF23..0x47BF29: SUB then signed SAR 2, including wrap/truncation.
    private static int PointerDifference(uint a, uint b) => unchecked((int)(a - b)) >> 2;

    /// <summary>
    /// Sorts the caller's retained element-index span with the native max-heap
    /// mechanics (0x83DCF0). Equal children choose the left child; equal elements
    /// are not stable. No insertion-order tie-break or per-sort allocation is added.
    /// Caller supplies valid indices and an unchanged element table.
    /// </summary>
    internal static void Sort(Span<int> indices, ReadOnlySpan<Wrath335M2ElementSortData> elements,
        bool translucent, bool groupShaders = false)
        => SortCore(indices, elements, translucent, groupShaders, false);

    // Base transparent heap first, then the optional material regrouping heap.
    // 0x82300E..0x823027 gates on cache bit 0x80 and the scene-wide admitted
    // resolved-blend-3/10 particle count, not the count in this individual queue.
    internal static void SortTransparentQueue(Span<int> indices,
        Span<Wrath335M2ElementSortData> elements, bool groupShaders,
        bool regroupAdditiveParticles, uint additiveParticleCount, bool forceParticleAdditive)
    {
        Sort(indices, elements, true, groupShaders);
        if (!regroupAdditiveParticles || additiveParticleCount <= 1)
            return;
        uint group = 0;
        var previousAdditive = false;
        foreach (var index in indices)
        {
            var additive = IsAdditiveForRegrouping(elements[index], forceParticleAdditive);
            // 0x81FAB0..0x81FACE: every non-additive entry is a separator;
            // only consecutive additive entries share a group.
            if (!additive || !previousAdditive) group++;
            elements[index] = elements[index] with { AdditiveGroup = group };
            previousAdditive = additive;
        }
        SortCore(indices, elements, true, groupShaders, true);
    }

    // 0x81FA40..0x81FAA5 and transparent blend row 0xA453CC:
    // authored material modes 3/4 resolve to Gx blends 10/3 respectively.
    // Particle +0xD0 is already a resolved Gx blend, not an authored M2 mode.
    internal static bool IsAdditiveForRegrouping(in Wrath335M2ElementSortData element,
        bool forceParticleAdditive) => element.Kind switch
        {
            Wrath335M2ElementKind.Mesh or Wrath335M2ElementKind.ProjectedMesh or
                Wrath335M2ElementKind.DoodadRun or Wrath335M2ElementKind.Ribbon =>
                element.BlendMode is 3 or 4,
            Wrath335M2ElementKind.ParticleRun => forceParticleAdditive ||
                element.ParticleBlendMode is 3 or 10,
            _ => false
        };

    // 0x81F0E0..0x81F1C2. Particle entries precede other kinds within an
    // additive group and ignore distance/priority/model identities there.
    internal static int CompareAdditive(in Wrath335M2ElementSortData a,
        in Wrath335M2ElementSortData b, bool groupShaders)
    {
        var result = a.AdditiveGroup.CompareTo(b.AdditiveGroup);
        if (result != 0) return result;
        if (a.Kind == Wrath335M2ElementKind.ParticleRun || b.Kind == Wrath335M2ElementKind.ParticleRun)
        {
            result = ((int)b.Kind).CompareTo((int)a.Kind);
            if (result != 0) return result;
        }
        if (a.Kind != Wrath335M2ElementKind.ParticleRun)
            return CompareTransparent(a, b, groupShaders);
        result = a.ParticleBlendMode.CompareTo(b.ParticleBlendMode);
        if (result != 0) return result;
        result = ParticleMaterialFlags(a.ParticleFlags).CompareTo(ParticleMaterialFlags(b.ParticleFlags));
        return result != 0 ? result : PointerDifference(a.ParticleTextureIdentity, b.ParticleTextureIdentity);
    }

    private static void SortCore(Span<int> indices, ReadOnlySpan<Wrath335M2ElementSortData> elements,
        bool translucent, bool groupShaders, bool additive)
    {
        for (var start = indices.Length / 2 - 1; start >= 0; start--)
            SiftDown(indices, elements, start, indices.Length, indices[start], translucent, groupShaders, additive);
        for (var end = indices.Length - 1; end > 0; end--)
        {
            var saved = indices[end];
            indices[end] = indices[0];
            SiftDown(indices, elements, 0, end, saved, translucent, groupShaders, additive);
        }
    }

    private static void SiftDown(Span<int> indices, ReadOnlySpan<Wrath335M2ElementSortData> elements,
        int parent, int count, int saved, bool translucent, bool groupShaders, bool additive)
    {
        while (parent < count / 2)
        {
            var child = parent * 2 + 1;
            if (child + 1 < count && CompareIndices(elements, indices[child + 1], indices[child], translucent, groupShaders, additive) > 0) child++;
            if (CompareIndices(elements, indices[child], saved, translucent, groupShaders, additive) <= 0) break;
            indices[parent] = indices[child];
            parent = child;
        }
        indices[parent] = saved;
    }

    private static int CompareIndices(ReadOnlySpan<Wrath335M2ElementSortData> elements,
        int a, int b, bool translucent, bool groupShaders, bool additive) => additive
        ? CompareAdditive(elements[a], elements[b], groupShaders) : translucent
        ? CompareTransparent(elements[a], elements[b], groupShaders)
        : CompareOpaque(elements[a], elements[b]);
}
