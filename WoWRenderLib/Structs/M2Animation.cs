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
    public M2ColorAnimation[] Colors { get; init; } = [];
    public M2Track<float>[] TextureWeights { get; init; } = [];
    public M2TextureAnimation[] TextureTransforms { get; init; } = [];

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

    public void Evaluate(int sequenceIndex, double elapsedMilliseconds, Span<Matrix4x4> palette)
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
                : Matrix4x4.Identity;
            if ((bone.Flags & 7) != 0)
                parent = AdjustParent(parent, bone.Pivot, bone.Flags);

            // In 3.3.5 the animated gate is bit 0x200 or 0x80. An unanimated
            // bone inherits its parent matrix even if unused track descriptors exist.
            if ((bone.Flags & 0x280) == 0)
            {
                palette[i] = parent;
                continue;
            }

            var translation = bone.Translation.Sample(sequenceIndex, sequence, GlobalLoops,
                elapsedMilliseconds, Vector3.Zero, Vector3.Lerp);
            var rotation = bone.Rotation.Sample(sequenceIndex, sequence, GlobalLoops,
                elapsedMilliseconds, Quaternion.Identity, InterpolateRotation);
            var scale = bone.Scale.Sample(sequenceIndex, sequence, GlobalLoops,
                elapsedMilliseconds, Vector3.One, Vector3.Lerp);

            var local = Matrix4x4.CreateTranslation(-bone.Pivot)
                * Matrix4x4.CreateScale(scale)
                * Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(bone.Pivot + translation);
            palette[i] = local * parent;
        }
    }

    private static Quaternion InterpolateRotation(Quaternion a, Quaternion b, float amount)
    {
        if (Quaternion.Dot(a, b) < 0)
            b = new Quaternion(-b.X, -b.Y, -b.Z, -b.W);
        return Quaternion.Normalize(Quaternion.Lerp(a, b, amount));
    }

    private int ResolveSequenceIndex(int index)
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

    private static Matrix4x4 AdjustParent(Matrix4x4 original, Vector3 pivot, uint flags)
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
                basis = Vector3.UnitX * (axis == 0 ? basis.Length() : 0)
                    + Vector3.UnitY * (axis == 1 ? basis.Length() : 0)
                    + Vector3.UnitZ * (axis == 2 ? basis.Length() : 0);
            if ((flags & 2) != 0 && basis.LengthSquared() > 1e-10f)
                basis = Vector3.Normalize(basis);
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
            adjusted.M41 = adjusted.M42 = adjusted.M43 = 0;
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

        var period = GlobalSequence >= 0 && GlobalSequence < globalLoops.Length
            ? globalLoops[GlobalSequence]
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
            if (time > timeline.Times[i])
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
