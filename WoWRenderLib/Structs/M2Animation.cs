using System.Numerics;

namespace WoWRenderLib.Structs;

/// <summary>Managed animation data copied from WowLib before the native M2 is disposed.</summary>
public sealed class M2Animation
{
    public const int MaxGpuBones = 256;

    public required M2Bone[] Bones { get; init; }
    public required M2Sequence[] Sequences { get; init; }
    public required uint[] GlobalLoops { get; init; }
    public bool HasAnimatedBones { get; init; }
    public bool HasBillboardBones { get; init; }
    public bool HasMaterialTracks { get; init; }
    public M2ColorAnimation[] Colors { get; init; } = [];
    public M2Track<float>[] TextureWeights { get; init; } = [];
    public M2TextureAnimation[] TextureTransforms { get; init; } = [];
    public M2RibbonAnimation[] Ribbons { get; internal set; } = [];
    public M2ParticleAnimation[] Particles { get; internal set; } = [];
    public int[] RenderableParticleIndices { get; internal set; } = [];

    /// <summary>The first Stand sequence, or sequence zero when Stand is absent.</summary>
    public int DefaultSequenceIndex
    {
        get
        {
            for (var i = 0; i < Sequences.Length; i++)
            {
                if (Sequences[i].AnimationId == 0)
                    return i;
            }
            return 0;
        }
    }

    public uint DefaultSequenceDuration => Sequences.Length == 0
        ? 0
        : Sequences[ResolveSequenceIndex(DefaultSequenceIndex)].Duration;

    public M2AnimatedMaterial EvaluateMaterial(Submesh batch, int sequenceIndex, double elapsedMilliseconds)
    {
        sequenceIndex = ResolveSequenceIndex(sequenceIndex);
        var sequence = (uint)sequenceIndex < Sequences.Length ? Sequences[sequenceIndex] : default;
        var color = Vector4.One;
        if ((uint)batch.colorIndex < Colors.Length)
        {
            var track = Colors[batch.colorIndex];
            var tint = batch.blendType is 5 or 6
                ? Vector3.One
                : track.Color.Sample(sequenceIndex, sequence, GlobalLoops,
                    elapsedMilliseconds, Vector3.One, Vector3.Lerp);
            var alpha = track.Alpha.Sample(sequenceIndex, sequence, GlobalLoops,
                elapsedMilliseconds, 1f, float.Lerp);
            color = new Vector4(tint, alpha);
        }
        if ((uint)batch.textureWeightIndex < TextureWeights.Length)
        {
            color.W *= TextureWeights[batch.textureWeightIndex].Sample(
                sequenceIndex, sequence, GlobalLoops, elapsedMilliseconds, 1f, float.Lerp);
        }

        var first = EvaluateTextureTransform(batch.textureTransformIndex1, sequenceIndex,
            sequence, elapsedMilliseconds);
        var second = EvaluateTextureTransform(batch.textureTransformIndex2, sequenceIndex,
            sequence, elapsedMilliseconds);
        return new M2AnimatedMaterial(color, first, second,
            (uint)batch.textureTransformIndex1 < TextureTransforms.Length,
            (uint)batch.textureTransformIndex2 < TextureTransforms.Length);
    }

    private Matrix4x4 EvaluateTextureTransform(
        int index, int sequenceIndex, M2Sequence sequence, double elapsedMilliseconds)
    {
        if ((uint)index >= TextureTransforms.Length)
            return Matrix4x4.Identity;

        var animation = TextureTransforms[index];
        var matrix = Matrix4x4.Identity;
        var pivot = new Vector3(0.5f, 0.5f, 0f);
        if (animation.Rotation.Timelines.Length > 0)
        {
            var rotation = animation.Rotation.Sample(sequenceIndex, sequence, GlobalLoops,
                elapsedMilliseconds, Quaternion.Identity, InterpolateRotation);
            matrix = Matrix4x4.CreateTranslation(pivot) * matrix;
            matrix = Matrix4x4.CreateFromQuaternion(rotation) * matrix;
            matrix = Matrix4x4.CreateTranslation(-pivot) * matrix;
        }
        if (animation.Scale.Timelines.Length > 0)
        {
            var scale = animation.Scale.Sample(sequenceIndex, sequence, GlobalLoops,
                elapsedMilliseconds, Vector3.One, Vector3.Lerp);
            matrix = Matrix4x4.CreateTranslation(pivot) * matrix;
            matrix = Matrix4x4.CreateScale(scale) * matrix;
            matrix = Matrix4x4.CreateTranslation(-pivot) * matrix;
        }
        if (animation.Translation.Timelines.Length > 0)
        {
            var translation = animation.Translation.Sample(sequenceIndex, sequence, GlobalLoops,
                elapsedMilliseconds, Vector3.Zero, Vector3.Lerp);
            matrix = Matrix4x4.CreateTranslation(translation) * matrix;
        }
        return matrix;
    }

    public void Evaluate(int sequenceIndex, double elapsedMilliseconds, Span<Matrix4x4> palette) =>
        EvaluateCore(sequenceIndex, elapsedMilliseconds, palette, Matrix4x4.Identity, false);

    /// <summary>
    /// Evaluates one ordinary bone and its ancestors without traversing the
    /// rest of the skeleton. Call only after <see cref="HasRigidBoneChain"/>.
    /// </summary>
    internal Matrix4x4 EvaluateRigidBone(
        int boneIndex, int sequenceIndex, double elapsedMilliseconds)
    {
        Span<int> chain = stackalloc int[MaxGpuBones];
        var count = 0;
        for (var index = boneIndex; index >= 0;)
        {
            if ((uint)index >= Bones.Length || count >= chain.Length)
                throw new ArgumentOutOfRangeException(nameof(boneIndex));
            chain[count++] = index;
            var parent = Bones[index].Parent;
            if (parent < 0)
                break;
            if (parent >= index)
                throw new ArgumentException("The bone chain is not ordered.", nameof(boneIndex));
            index = parent;
        }

        sequenceIndex = ResolveSequenceIndex(sequenceIndex);
        var sequence = (uint)sequenceIndex < Sequences.Length
            ? Sequences[sequenceIndex] : default;
        var result = Matrix4x4.Identity;
        for (var i = count - 1; i >= 0; i--)
        {
            var bone = Bones[chain[i]];
            if ((bone.Flags & 0x7F) != 0)
                throw new ArgumentException("The bone chain needs a full palette.", nameof(boneIndex));
            if ((bone.Flags & 0x280) == 0)
                continue;
            var translation = bone.Translation.Sample(sequenceIndex, sequence,
                GlobalLoops, elapsedMilliseconds, Vector3.Zero, Vector3.Lerp);
            var rotation = bone.Rotation.Sample(sequenceIndex, sequence,
                GlobalLoops, elapsedMilliseconds, Quaternion.Identity,
                InterpolateRotation);
            var scale = bone.Scale.Sample(sequenceIndex, sequence,
                GlobalLoops, elapsedMilliseconds, Vector3.One, Vector3.Lerp);
            result = Matrix4x4.CreateTranslation(-bone.Pivot)
                * Matrix4x4.CreateScale(scale)
                * Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(bone.Pivot + translation)
                * result;
        }
        return result;
    }

    internal bool HasRigidBoneChain(int boneIndex)
    {
        for (var steps = 0; steps < Bones.Length; steps++)
        {
            if ((uint)boneIndex >= Bones.Length)
                return false;
            var bone = Bones[boneIndex];
            if ((bone.Flags & 0x7F) != 0)
                return false;
            if (bone.Parent < 0)
                return true;
            if (bone.Parent >= boneIndex)
                return false;
            boneIndex = bone.Parent;
        }
        return false;
    }

    internal bool HasTimeDependentBoneChain(int boneIndex)
    {
        for (var steps = 0; steps < Bones.Length; steps++)
        {
            var bone = Bones[boneIndex];
            if ((bone.Flags & 0x280) != 0 &&
                (HasMultipleKeys(bone.Translation) ||
                 HasMultipleKeys(bone.Rotation) ||
                 HasMultipleKeys(bone.Scale)))
                return true;
            if (bone.Parent < 0 || bone.Parent >= boneIndex)
                return false;
            boneIndex = bone.Parent;
        }
        return false;
    }

    private static bool HasMultipleKeys<T>(M2Track<T> track) =>
        track.Timelines.Any(timeline =>
            Math.Min(timeline.Times.Length, timeline.Values.Length) > 1);

    /// <summary>
    /// Evaluate camera-facing bones using the rigid model-to-view orientation.
    /// Scale and translation must be excluded from <paramref name="modelToView"/>.
    /// </summary>
    public void Evaluate(
        int sequenceIndex,
        double elapsedMilliseconds,
        Span<Matrix4x4> palette,
        Matrix4x4 modelToView) =>
        EvaluateCore(sequenceIndex, elapsedMilliseconds, palette, modelToView, true);

    private void EvaluateCore(
        int sequenceIndex,
        double elapsedMilliseconds,
        Span<Matrix4x4> palette,
        Matrix4x4 root,
        bool cameraFacing)
    {
        if (palette.Length < Bones.Length)
            throw new ArgumentException("The bone palette is smaller than the model skeleton.", nameof(palette));

        sequenceIndex = ResolveSequenceIndex(sequenceIndex);
        var sequence = (uint)sequenceIndex < Sequences.Length ? Sequences[sequenceIndex] : default;
        for (var i = 0; i < Bones.Length; i++)
        {
            var bone = Bones[i];
            var parent = bone.Parent >= 0 && bone.Parent < i
                ? palette[bone.Parent]
                : root;
            if ((bone.Flags & 7) != 0)
                parent = AdjustParent(parent, root, bone.Pivot, bone.Flags);

            // In 3.3.5 the animated gate is bit 0x200 or 0x80. An unanimated
            // bone inherits its parent matrix even if unused track descriptors exist.
            var animated = (bone.Flags & 0x280) != 0;
            Matrix4x4 local = Matrix4x4.Identity;
            if (!animated)
            {
                palette[i] = parent;
            }
            else
            {
                var translation = bone.Translation.Sample(sequenceIndex, sequence, GlobalLoops,
                    elapsedMilliseconds, Vector3.Zero, Vector3.Lerp);
                var rotation = bone.Rotation.Sample(sequenceIndex, sequence, GlobalLoops,
                    elapsedMilliseconds, Quaternion.Identity, InterpolateRotation);
                var scale = bone.Scale.Sample(sequenceIndex, sequence, GlobalLoops,
                    elapsedMilliseconds, Vector3.One, Vector3.Lerp);

                local = Matrix4x4.CreateTranslation(-bone.Pivot)
                    * Matrix4x4.CreateScale(scale)
                    * Matrix4x4.CreateFromQuaternion(rotation)
                    * Matrix4x4.CreateTranslation(bone.Pivot + translation);
                palette[i] = local * parent;
            }

            if (cameraFacing && (bone.Flags & 0x78) != 0)
                palette[i] = ApplyBillboard(palette[i], local, bone.Pivot,
                    bone.Flags & 0x78, animated);
        }

        if (cameraFacing)
        {
            if (!Matrix4x4.Invert(root, out var inverseRoot))
                throw new ArgumentException("The model-to-view orientation is not invertible.", nameof(root));
            for (var i = 0; i < Bones.Length; i++)
                palette[i] *= inverseRoot;
        }
    }

    private static Quaternion InterpolateRotation(Quaternion a, Quaternion b, float amount)
    {
        if (Quaternion.Dot(a, b) < 0)
            b = new Quaternion(-b.X, -b.Y, -b.Z, -b.W);
        return Quaternion.Normalize(Quaternion.Lerp(a, b, amount));
    }

    internal int ResolveSequenceIndex(int index)
    {
        if ((uint)index >= Sequences.Length)
            return index;
        for (var traversed = 0; traversed < Sequences.Length; traversed++)
        {
            var sequence = Sequences[index];
            if ((sequence.Flags & 0x40) == 0)
                return index;
            if ((uint)sequence.AliasNext >= Sequences.Length)
                throw new InvalidDataException($"M2 animation sequence {index} has an invalid alias target.");
            index = sequence.AliasNext;
        }
        throw new InvalidDataException("M2 animation sequence aliases form a cycle.");
    }

    private static Matrix4x4 AdjustParent(Matrix4x4 original, Matrix4x4 root, Vector3 pivot, uint flags)
    {
        var adjusted = original;
        for (var axis = 0; axis < 3; axis++)
        {
            var basis = axis switch
            {
                0 => new Vector3(adjusted.M11, adjusted.M12, adjusted.M13),
                1 => new Vector3(adjusted.M21, adjusted.M22, adjusted.M23),
                _ => new Vector3(adjusted.M31, adjusted.M32, adjusted.M33)
            };
            if ((flags & 4) != 0)
            {
                var rootBasis = GetBasis(root, axis);
                if (rootBasis.LengthSquared() > 1e-10f)
                    basis = rootBasis * (basis.Length() / rootBasis.Length());
            }
            if ((flags & 2) != 0 && basis.LengthSquared() > 1e-10f)
                basis = Vector3.Normalize(basis) * GetBasis(root, axis).Length();
            switch (axis)
            {
                case 0:
                    adjusted.M11 = basis.X; adjusted.M12 = basis.Y; adjusted.M13 = basis.Z;
                    break;
                case 1:
                    adjusted.M21 = basis.X; adjusted.M22 = basis.Y; adjusted.M23 = basis.Z;
                    break;
                default:
                    adjusted.M31 = basis.X; adjusted.M32 = basis.Y; adjusted.M33 = basis.Z;
                    break;
            }
        }

        if ((flags & 1) != 0)
        {
            adjusted.M41 = root.M41;
            adjusted.M42 = root.M42;
            adjusted.M43 = root.M43;
        }
        else
        {
            var originalPivot = Vector3.Transform(pivot, original);
            var adjustedPivot = Vector3.TransformNormal(pivot, adjusted);
            var translation = originalPivot - adjustedPivot;
            adjusted.M41 = translation.X;
            adjusted.M42 = translation.Y;
            adjusted.M43 = translation.Z;
        }
        return adjusted;
    }

    private static Matrix4x4 ApplyBillboard(
        Matrix4x4 matrix, Matrix4x4 local, Vector3 pivot, uint mask, bool animated)
    {
        var first = GetBasis(matrix, 0);
        var second = GetBasis(matrix, 1);
        var third = GetBasis(matrix, 2);
        var scales = new Vector3(first.Length(), second.Length(), third.Length());
        var pinnedPivot = Vector3.Transform(pivot, matrix);

        switch (mask)
        {
            case 0x08:
                if (animated)
                {
                    first = SwizzleAndNormalize(GetBasis(local, 0));
                    second = SwizzleAndNormalize(GetBasis(local, 1));
                    third = SwizzleAndNormalize(GetBasis(local, 2));
                }
                else
                {
                    first = -Vector3.UnitZ;
                    second = Vector3.UnitX;
                    third = Vector3.UnitY;
                }
                break;
            case 0x10:
                first = NormalizeIfPossible(first);
                second = NormalizeIfPossible(new Vector3(first.Y, -first.X, 0));
                third = Vector3.Cross(second, first);
                break;
            case 0x20:
                second = NormalizeIfPossible(second);
                first = NormalizeIfPossible(new Vector3(-second.Y, second.X, 0));
                third = Vector3.Cross(second, first);
                break;
            case 0x40:
                third = NormalizeIfPossible(third);
                second = NormalizeIfPossible(new Vector3(third.Y, -third.X, 0));
                first = Vector3.Cross(third, second);
                break;
        }

        SetBasis(ref matrix, 0, first * scales.X);
        SetBasis(ref matrix, 1, second * scales.Y);
        SetBasis(ref matrix, 2, third * scales.Z);
        var translation = pinnedPivot - Vector3.TransformNormal(pivot, matrix);
        matrix.M41 = translation.X;
        matrix.M42 = translation.Y;
        matrix.M43 = translation.Z;
        matrix.M14 = matrix.M24 = matrix.M34 = 0;
        matrix.M44 = 1;
        return matrix;
    }

    private static Vector3 SwizzleAndNormalize(Vector3 value) =>
        NormalizeIfPossible(new Vector3(value.Y, value.Z, -value.X));

    private static Vector3 NormalizeIfPossible(Vector3 value) =>
        value.Length() > 1e-5f ? Vector3.Normalize(value) : value;

    private static Vector3 GetBasis(in Matrix4x4 matrix, int index) => index switch
    {
        0 => new(matrix.M11, matrix.M12, matrix.M13),
        1 => new(matrix.M21, matrix.M22, matrix.M23),
        _ => new(matrix.M31, matrix.M32, matrix.M33)
    };

    private static void SetBasis(ref Matrix4x4 matrix, int index, Vector3 basis)
    {
        switch (index)
        {
            case 0:
                matrix.M11 = basis.X; matrix.M12 = basis.Y; matrix.M13 = basis.Z;
                break;
            case 1:
                matrix.M21 = basis.X; matrix.M22 = basis.Y; matrix.M23 = basis.Z;
                break;
            default:
                matrix.M31 = basis.X; matrix.M32 = basis.Y; matrix.M33 = basis.Z;
                break;
        }
    }
}

public readonly record struct M2Sequence(
    uint Duration, uint Flags, int AliasNext = -1, ushort AnimationId = 0);

public readonly record struct M2AnimatedMaterial(
    Vector4 Color, Matrix4x4 TextureMatrix1, Matrix4x4 TextureMatrix2,
    bool HasTextureMatrix1, bool HasTextureMatrix2);

public sealed record M2ColorAnimation(M2Track<Vector3> Color, M2Track<float> Alpha);

public sealed record M2TextureAnimation(
    M2Track<Vector3> Translation, M2Track<Quaternion> Rotation, M2Track<Vector3> Scale);

public sealed record M2Bone(
    int Parent,
    uint Flags,
    Vector3 Pivot,
    M2Track<Vector3> Translation,
    M2Track<Quaternion> Rotation,
    M2Track<Vector3> Scale);

public sealed class M2Track<T>
{
    public required ushort Interpolation { get; init; }
    public required int GlobalSequence { get; init; }
    public required M2Timeline<T>[] Timelines { get; init; }

    public T Sample(
        int sequenceIndex,
        M2Sequence sequence,
        ReadOnlySpan<uint> globalLoops,
        double elapsedMilliseconds,
        T fallback,
        Func<T, T, float, T> interpolate)
    {
        var timelineIndex = GlobalSequence >= 0 ? 0 : sequenceIndex;
        if ((uint)timelineIndex >= Timelines.Length)
            return fallback;

        var timeline = Timelines[timelineIndex];
        var count = Math.Min(timeline.Times.Length, timeline.Values.Length);
        if (count == 0)
            return fallback;
        if (count == 1)
            return timeline.Values[0];

        var period = GlobalSequence >= 0
            ? GlobalSequence < globalLoops.Length ? globalLoops[GlobalSequence] : 0u
            : sequence.Duration;
        if (period == 0)
            period = timeline.Times[count - 1];
        var loop = GlobalSequence >= 0 || (sequence.Flags & 1) == 0;
        var time = period == 0 ? 0d
            : loop ? ((elapsedMilliseconds % period) + period) % period
            : Math.Clamp(elapsedMilliseconds, 0d, period);

        if (time <= timeline.Times[0])
            return timeline.Values[0];
        for (var i = 1; i < count; i++)
        {
            if (time >= timeline.Times[i])
                continue;
            var left = i - 1;
            var span = timeline.Times[i] - timeline.Times[left];
            if (Interpolation == 0 || span == 0)
                return timeline.Values[left];
            var amount = (float)((time - timeline.Times[left]) / span);
            return interpolate(timeline.Values[left], timeline.Values[i], amount);
        }
        return timeline.Values[count - 1];
    }
}

public readonly record struct M2Timeline<T>(uint[] Times, T[] Values);
