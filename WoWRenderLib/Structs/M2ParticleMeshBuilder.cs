using System.Numerics;
using System.Runtime.CompilerServices;

namespace WoWRenderLib.Structs;

/// <summary>
/// Bounded, cadence-independent reconstruction of ordinary WotLK plane/sphere
/// head and tail particles. Emitters requiring client state replay or world queries are
/// explicitly refused until those paths are implemented.
/// </summary>
public static class M2ParticleMeshBuilder
{
    [ThreadStatic] private static List<Point>? _pointScratch;
    private sealed class EmissionScheduleCache
    {
        public readonly Dictionary<M2ParticleAnimation, Dictionary<int, M2ParticleEmissionSchedule?>>
            ByEmitter = new(ReferenceEqualityComparer.Instance);
    }

    private static readonly ConditionalWeakTable<M2Animation, EmissionScheduleCache>
        EmissionSchedules = new();

    private const int MaxParticles = 1024;
    private const uint Unlit = 0x1;
    private const uint DepthSort = 0x2;
    private const uint VelocityAligned = 0x4;
    private const uint Unfogged = 0x8;
    private const uint WorldSpace = 0x10;
    private const uint InheritScale = 0x20;
    private const uint BurstVelocity = 0x40;
    private const uint InwardOnly = 0x80;
    private const uint ForceZUp = 0x100;
    private const uint NegateSpinRandom = 0x200;
    private const uint ClampTailAge = 0x400;
    private const uint Tumble = 0x1000;
    private const uint RandomTexture = 0x10000;
    private const uint HasHead = 0x20000;
    private const uint HasTail = 0x40000;
    private const uint IndependentSize = 0x80000;
    private const uint SupportedFlags = Unlit | DepthSort | VelocityAligned | Unfogged |
        WorldSpace |
        InheritScale | BurstVelocity | InwardOnly | ForceZUp |
        NegateSpinRandom | ClampTailAge | Tumble | RandomTexture | HasHead | HasTail |
        IndependentSize;
    private static readonly (float X, float Y, float U, float V)[] Corners =
    [
        (-1f, -1f, 0f, 1f), (1f, -1f, 1f, 1f),
        (1f, 1f, 1f, 0f), (-1f, 1f, 0f, 0f)
    ];

    private readonly record struct Point(
        Vector3 Center, Vector2 Scale, Vector4 Color, float Spin,
        Vector2 UvOrigin, Vector2 TailUvOrigin, Vector2 UvTile,
        Vector3 Velocity, Vector3 TailVector, float Depth);

    public static bool IsSupported(M2Animation animation, M2ParticleAnimation emitter)
    {
        if ((uint)emitter.BoneIndex >= animation.Bones.Length ||
            emitter.HasChildModels || emitter.ColorIndex != 0 ||
            emitter.EmitterType is not (1 or 2) || emitter.BlendMode > 6 ||
            (emitter.EmitterType == 2 && (emitter.Flags & InwardOnly) != 0) ||
            (emitter.Flags & (HasHead | HasTail)) == 0 ||
            (emitter.Flags & ~SupportedFlags) != 0 ||
            // Inherited burst velocity is zero on a stationary bone chain.
            // Moving chains need historical emitter velocity before admission.
            ((emitter.Flags & BurstVelocity) != 0 &&
             animation.HasTimeDependentBoneChain(emitter.BoneIndex)) ||
            emitter.Rows == 0 || emitter.Columns == 0 ||
            !IsPowerOfTwo(emitter.Rows) || !IsPowerOfTwo(emitter.Columns) ||
            emitter.Rows * emitter.Columns > 1024 ||
            !float.IsFinite(emitter.LifespanVariation) ||
            emitter.LifespanVariation < 0f ||
            emitter.EmissionRateVariation != 0f ||
            !IsFinite(emitter.Wind) ||
            !float.IsFinite(emitter.WindTime) ||
            !float.IsFinite(emitter.Drag) || MathF.Abs(emitter.Drag) > 1000f ||
            !float.IsFinite(emitter.BaseSpin) || !float.IsFinite(emitter.SpinSpeed) ||
            !float.IsFinite(emitter.BaseSpinVariation) ||
            !float.IsFinite(emitter.SpinSpeedVariation) ||
            !float.IsFinite(emitter.TailLength) ||
            !float.IsFinite(emitter.ScaleVariation.X) ||
            !float.IsFinite(emitter.ScaleVariation.Y) ||
            !emitter.Color.HasKeys || !emitter.Alpha.HasKeys ||
            !emitter.Scale.HasKeys ||
            !M2ParticleEmissionSchedule.HasSupportedKeys(emitter.EmissionRate) ||
            !HasOnlyConstantKeys(emitter.Lifespan) ||
            !HasOnlyConstantKeys(emitter.Gravity) ||
            !HasOnlyConstantKeys(emitter.Enabled) ||
            !HasAnyPositiveKey(emitter.EmissionRate) ||
            !HasAnyPositiveKey(emitter.Lifespan) ||
            !animation.HasRigidBoneChain(emitter.BoneIndex))
            return false;
        return true;
    }

    public static M2RibbonMesh Build(
        M2Animation animation,
        M2ParticleAnimation emitter,
        int sequenceIndex,
        double timeMilliseconds,
        uint stableSeed,
        Matrix4x4 modelToView)
    {
        if (!IsSupported(animation, emitter))
            return M2RibbonMesh.Empty;
        return BuildSupported(animation, emitter, sequenceIndex,
            timeMilliseconds, stableSeed, modelToView);
    }

    /// <summary>
    /// Builds an emitter already accepted by <see cref="IsSupported"/> at load
    /// time, avoiding repeated track validation for every visible placement.
    /// </summary>
    public static M2RibbonMesh BuildSupported(
        M2Animation animation,
        M2ParticleAnimation emitter,
        int sequenceIndex,
        double timeMilliseconds,
        uint stableSeed,
        Matrix4x4 modelToView,
        M2RibbonVertex[]? reusableVertices = null,
        ushort[]? reusableIndices = null)
    {
        if (!double.IsFinite(timeMilliseconds) || timeMilliseconds < 0d)
            return M2RibbonMesh.Empty;

        sequenceIndex = animation.ResolveSequenceIndex(sequenceIndex);
        var sequence = (uint)sequenceIndex < animation.Sequences.Length
            ? animation.Sequences[sequenceIndex] : default;
        if (Sample(emitter.Enabled, 1f, timeMilliseconds) == 0f)
            return M2RibbonMesh.Empty;
        var schedule = GetEmissionSchedule(animation, emitter, sequenceIndex, sequence);
        if (schedule is null)
            return M2RibbonMesh.Empty;
        var life = Sample(emitter.Lifespan, 0f, timeMilliseconds);
        if (!float.IsFinite(life) || life <= 0f || life > 3600f)
            return M2RibbonMesh.Empty;

        var first = Math.Max(0d, Math.Floor(
            schedule.BirthCountBy(Math.Max(0d,
                timeMilliseconds - life * 1000d)) + 0.5d));
        var last = Math.Floor(schedule.BirthCountBy(timeMilliseconds) + 0.5d) - 1d;
        if (!double.IsFinite(first) || !double.IsFinite(last) || last < first ||
            last >= long.MaxValue)
            return M2RibbonMesh.Empty;
        var newest = (long)last;
        var oldest = Math.Max((long)first, newest - MaxParticles + 1);
        if (!Matrix4x4.Invert(modelToView, out var viewToModel))
            return M2RibbonMesh.Empty;
        var right = Normalize(Vector3.TransformNormal(Vector3.UnitX, viewToModel));
        var up = Normalize(Vector3.TransformNormal(Vector3.UnitY, viewToModel));
        if (right == Vector3.Zero || up == Vector3.Zero)
            return M2RibbonMesh.Empty;

        var currentBone = animation.EvaluateRigidBone(emitter.BoneIndex,
            sequenceIndex, animation.HasAnimatedBones ? timeMilliseconds : 0d);
        // Analytic reconstruction has no retained birth state. Even a
        // WorldSpace particle must keep the bone orientation it had at birth;
        // using today's rotating bone collapses portal rings into a beam.
        var movingBoneChain = animation.HasAnimatedBones &&
            animation.HasTimeDependentBoneChain(emitter.BoneIndex);
        var boneScale = Vector3.TransformNormal(Vector3.UnitX, currentBone).Length();
        if (!float.IsFinite(boneScale))
            return M2RibbonMesh.Empty;
        var twinkle = float.IsFinite(emitter.TwinkleScale.X) &&
            emitter.TwinkleScale.X > 0f ? emitter.TwinkleScale.X : 1f;
        var sortParticlesByDepth = emitter.BlendMode == 2;
        if ((emitter.Flags & Tumble) != 0)
        {
            right = Normalize(Vector3.TransformNormal(Vector3.UnitX, currentBone));
            up = Normalize(Vector3.TransformNormal(Vector3.UnitY, currentBone));
            if (right.LengthSquared() < 1e-12f || up.LengthSquared() < 1e-12f)
                return M2RibbonMesh.Empty;
        }
        var points = _pointScratch ??= new List<Point>();
        points.Clear();
        var possiblePointCount = (int)(newest - oldest + 1);
        if (points.Capacity < possiblePointCount)
            points.Capacity = possiblePointCount;
        for (var n = oldest; n <= newest; n++)
        {
            var born = schedule.BirthTime(n);
            var age = (float)((timeMilliseconds - born) / 1000d);
            if (!double.IsFinite(born) || born < 0d || age < 0f || age >= life)
                continue;
            var seed = Hash(stableSeed ^ ((uint)emitter.SourceIndex * 0x85EBCA6Bu) ^ (uint)n);
            var bone = movingBoneChain
                ? animation.EvaluateRigidBone(emitter.BoneIndex,
                    sequenceIndex, born)
                : currentBone;
            var width = Sample(emitter.AreaWidth, 0f, born);
            var length = Sample(emitter.AreaLength, 0f, born);
            var horizontal = Sample(emitter.HorizontalRange, 0f, born);
            var vertical = Sample(emitter.VerticalRange, 0f, born);
            var speed = Sample(emitter.EmissionSpeed, 0f, born);
            var speedVariation = Sample(emitter.SpeedVariation, 0f, born);
            var zSource = Sample(emitter.ZSource, 0f, born);
            var gravity = Sample(emitter.Gravity, 0f, born);
            if (!float.IsFinite(width) || !float.IsFinite(length) ||
                !float.IsFinite(horizontal) || !float.IsFinite(vertical) ||
                !float.IsFinite(speed) || !float.IsFinite(speedVariation) ||
                !float.IsFinite(zSource) || !float.IsFinite(gravity) ||
                MathF.Abs(width) > 10000f || MathF.Abs(length) > 10000f)
                continue;

            var azimuth = (Random01(seed, 2) * 2f - 1f) * horizontal;
            var polar = (Random01(seed, 3) * 2f - 1f) * vertical;
            Vector3 spawn;
            Vector3 direction;
            if (emitter.EmitterType == 2)
            {
                // A zero horizontal range leaves the sphere's latitude ring
                // in the emitter's YZ plane. Portal emitters rotate this
                // plane about X; putting the ring in XZ instead turns the
                // portal into a narrow beam viewed along its axis.
                direction = new Vector3(
                    MathF.Sin(azimuth) * MathF.Cos(polar),
                    MathF.Cos(azimuth) * MathF.Cos(polar),
                    MathF.Sin(polar));
                spawn = direction * (width + Random01(seed, 0) * (length - width));
            }
            else
            {
                direction = new Vector3(
                    MathF.Sin(polar) * MathF.Cos(azimuth),
                    MathF.Sin(polar) * MathF.Sin(azimuth),
                    MathF.Cos(polar));
                spawn = new Vector3(
                    (Random01(seed, 0) * 2f - 1f) * width * 0.5f,
                    (Random01(seed, 1) * 2f - 1f) * length * 0.5f, 0f);
            }
            if (zSource != 0f)
            {
                var difference = spawn - new Vector3(0f, 0f, zSource);
                direction = difference.LengthSquared() > 1.19209e-7f
                    ? Vector3.Normalize(difference) : difference;
            }
            else if (emitter.EmitterType == 2 && (emitter.Flags & ForceZUp) != 0)
                direction = Vector3.UnitZ;
            var initialVelocity = Vector3.TransformNormal(direction, bone) *
                (speed * (1f + (Random01(seed, 4) * 2f - 1f) * speedVariation));
            var origin = Vector3.Transform(emitter.Position, bone) +
                Vector3.TransformNormal(spawn, bone);
            var drag = emitter.Drag;
            var decay = drag == 0f ? 1f : MathF.Exp(Math.Clamp(-drag * age, -88f, 88f));
            var travel = drag == 0f ? age : (1f - decay) / drag;
            var gravityTravel = drag == 0f
                ? 0.5f * age * age
                : age / drag - travel / drag;
            var (windTravel, windSpeed) = emitter.WindTime > 0f &&
                emitter.Wind != Vector3.Zero
                ? AccelerationResponse(drag, age, emitter.WindTime)
                : (0f, 0f);
            var center = origin + initialVelocity * travel -
                Vector3.UnitZ * (gravity * gravityTravel) +
                emitter.Wind * windTravel;
            var gravitySpeed = drag == 0f ? age : travel;
            var velocity = initialVelocity * decay -
                Vector3.UnitZ * (gravity * gravitySpeed) +
                emitter.Wind * windSpeed;
            var tailLength = emitter.TailLength;
            if ((emitter.Flags & ClampTailAge) != 0 && tailLength > age)
                tailLength = age;
            var tailVector = -velocity * tailLength;
            var ratio = age / life;
            var color = new Vector4(
                emitter.Color.Sample(ratio, Vector3.One, Vector3.Lerp),
                emitter.Alpha.Sample(ratio, 1f, float.Lerp));
            var size = emitter.Scale.Sample(ratio, Vector2.One, Vector2.Lerp);
            var scaleX = MathF.Max(0.0001f,
                1f + (Random01(seed, 5) * 2f - 1f) * emitter.ScaleVariation.X);
            var scaleY = (emitter.Flags & IndependentSize) != 0
                ? MathF.Max(0.0001f,
                    1f + (Random01(seed, 6) * 2f - 1f) * emitter.ScaleVariation.Y)
                : scaleX;
            size *= new Vector2(scaleX, scaleY) * (boneScale * twinkle);
            var spin = 0f;
            if (emitter.SpinSpeed != 0f || emitter.SpinSpeedVariation != 0f)
            {
                spin = emitter.BaseSpin +
                    (Random01(seed, 7) * 2f - 1f) * emitter.BaseSpinVariation +
                    (emitter.SpinSpeed +
                     (Random01(seed, 8) * 2f - 1f) * emitter.SpinSpeedVariation) * age;
                if ((emitter.Flags & NegateSpinRandom) != 0 &&
                    (Hash((uint)n ^ 0x5BD1E995u) & 0x20u) != 0)
                    spin = -spin;
            }
            var cell = emitter.HeadCell.HasKeys
                ? Math.Max(0, (int)MathF.Round(emitter.HeadCell.Sample(
                    ratio, 0f, float.Lerp)))
                : (emitter.Flags & RandomTexture) != 0
                    ? (int)(Hash(seed ^ 0x2545F491u) %
                        (uint)(emitter.Rows * emitter.Columns))
                    : 0;
            var tile = new Vector2(1f / emitter.Columns, 1f / emitter.Rows);
            var uv = new Vector2(cell % emitter.Columns * tile.X,
                (cell / emitter.Columns) % emitter.Rows * tile.Y);
            var tailCell = emitter.TailCell.HasKeys
                ? Math.Max(0, (int)MathF.Round(emitter.TailCell.Sample(
                    ratio, 0f, float.Lerp)))
                : 0;
            var tailUv = new Vector2(tailCell % emitter.Columns * tile.X,
                (tailCell / emitter.Columns) % emitter.Rows * tile.Y);
            var depth = sortParticlesByDepth
                ? Vector3.Transform(center, modelToView).Z : 0f;
            if (IsFinite(center) && IsFinite(size) && IsFinite(color) &&
                IsFinite(velocity) && IsFinite(tailVector) &&
                float.IsFinite(depth) && float.IsFinite(spin))
                points.Add(new Point(center, size, color, spin, uv, tailUv,
                    tile, velocity, tailVector, depth));
        }

        // Additive blending is order independent; only alpha-blended heads
        // need the per-particle depth sort.
        if (sortParticlesByDepth)
            points.Sort(static (a, b) => b.Depth.CompareTo(a.Depth));
        var hasHead = (emitter.Flags & HasHead) != 0;
        var hasTail = (emitter.Flags & HasTail) != 0;
        var quadsPerPoint = (hasHead ? 1 : 0) + (hasTail ? 1 : 0);
        var vertexCount = points.Count * quadsPerPoint * 4;
        var indexCount = points.Count * quadsPerPoint * 6;
        var vertices = reusableVertices is { Length: var vertexLength } && vertexLength == vertexCount
            ? reusableVertices : new M2RibbonVertex[vertexCount];
        var indices = reusableIndices is { Length: var indexLength } && indexLength == indexCount
            ? reusableIndices : new ushort[indexCount];
        var quad = 0;
        foreach (var point in points)
        {
            if (hasHead)
                EmitQuad(point, false, quad++);
            if (hasTail)
                EmitQuad(point, true, quad++);
        }
        return new M2RibbonMesh(vertices, indices);

        void EmitQuad(Point point, bool tail, int quadIndex)
        {
            var cs = MathF.Cos(point.Spin);
            var sn = MathF.Sin(point.Spin);
            var tx = Vector3.Dot(point.TailVector, right);
            var ty = Vector3.Dot(point.TailVector, up);
            var planar = tx * tx + ty * ty;
            var sx = -Vector3.Dot(point.Velocity, right);
            var sy = -Vector3.Dot(point.Velocity, up);
            var speedPlanar = sx * sx + sy * sy;
            var speedTotal = point.Velocity.LengthSquared();
            var aligned = !tail && (emitter.Flags & VelocityAligned) != 0 &&
                speedPlanar > 1.19209e-7f && speedTotal > 1.19209e-7f;
            var alignInv = aligned ? 1f / MathF.Sqrt(speedPlanar) : 0f;
            var squash = aligned ? MathF.Sqrt(speedPlanar / speedTotal) : 1f;
            for (var corner = 0; corner < 4; corner++)
            {
                var c = Corners[corner];
                float rx, ry, along;
                if (tail && planar >= 0.00077160494f)
                {
                    var inv = 1f / MathF.Sqrt(planar);
                    var px = tx * inv * point.Scale.X;
                    var py = ty * inv * point.Scale.Y;
                    rx = c.Y < 0f ? -py : py;
                    ry = c.Y < 0f ? px : -px;
                    along = c.X > 0f ? 1f : 0f;
                }
                else
                {
                    var x = c.X * point.Scale.X;
                    var y = c.Y * point.Scale.Y;
                    if (aligned)
                    {
                        var alongX = sx * alignInv;
                        var alongY = sy * alignInv;
                        var a = x * squash;
                        rx = a * alongX - y * alongY;
                        ry = a * alongY + y * alongX;
                    }
                    else
                    {
                        rx = x * cs - y * sn;
                        ry = x * sn + y * cs;
                    }
                    along = 0f;
                }
                vertices[quadIndex * 4 + corner] = new M2RibbonVertex(
                    point.Center + right * rx + up * ry + point.TailVector * along,
                    (tail ? point.TailUvOrigin : point.UvOrigin) +
                        point.UvTile * new Vector2(c.U, c.V),
                    point.Color);
            }
            var baseVertex = quadIndex * 4;
            var baseIndex = quadIndex * 6;
            indices[baseIndex] = (ushort)baseVertex;
            indices[baseIndex + 1] = (ushort)(baseVertex + 1);
            indices[baseIndex + 2] = (ushort)(baseVertex + 2);
            indices[baseIndex + 3] = (ushort)baseVertex;
            indices[baseIndex + 4] = (ushort)(baseVertex + 2);
            indices[baseIndex + 5] = (ushort)(baseVertex + 3);
        }

        float Sample(M2Track<float> track, float fallback, double time) =>
            track.Sample(sequenceIndex, sequence, animation.GlobalLoops,
                time, fallback, float.Lerp);
    }

    private static (float Position, float Velocity) AccelerationResponse(
        float drag, float age, float until)
    {
        var driven = MathF.Min(age, until);
        var coasting = age - driven;
        float position, velocity;
        if (drag != 0f)
        {
            var decay = MathF.Exp(Math.Clamp(-drag * driven, -88f, 88f));
            velocity = (1f - decay) / drag;
            position = driven / drag - velocity / drag;
        }
        else
        {
            velocity = driven;
            position = 0.5f * driven * driven;
        }
        if (coasting > 0f)
        {
            if (drag != 0f)
            {
                var decay = MathF.Exp(Math.Clamp(-drag * coasting, -88f, 88f));
                position += velocity * (1f - decay) / drag;
                velocity *= decay;
            }
            else
            {
                position += velocity * coasting;
            }
        }
        return (position, velocity);
    }

    private static M2ParticleEmissionSchedule? GetEmissionSchedule(
        M2Animation animation,
        M2ParticleAnimation emitter,
        int sequenceIndex,
        M2Sequence sequence)
    {
        var cache = EmissionSchedules.GetValue(animation,
            static _ => new EmissionScheduleCache());
        lock (cache)
        {
            if (!cache.ByEmitter.TryGetValue(emitter, out var bySequence))
            {
                bySequence = [];
                cache.ByEmitter.Add(emitter, bySequence);
            }
            if (!bySequence.TryGetValue(sequenceIndex, out var schedule))
            {
                schedule = M2ParticleEmissionSchedule.Create(
                    emitter.EmissionRate, sequenceIndex, sequence,
                    animation.GlobalLoops);
                bySequence.Add(sequenceIndex, schedule);
            }
            return schedule;
        }
    }

    private static bool HasOnlyConstantKeys(M2Track<float> track) =>
        track.Timelines.All(timeline =>
            Math.Min(timeline.Times.Length, timeline.Values.Length) <= 1);

    private static bool HasAnyPositiveKey(M2Track<float> track) =>
        track.Timelines.Any(timeline =>
            timeline.Values.Any(value => float.IsFinite(value) && value > 0f));

    private static bool IsPowerOfTwo(ushort value) => (value & (value - 1)) == 0;
    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);
    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W);
    private static Vector3 Normalize(Vector3 vector) =>
        vector.LengthSquared() > 1e-12f ? Vector3.Normalize(vector) : Vector3.Zero;

    private static uint Hash(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        return value ^ (value >> 16);
    }

    private static float Random01(uint seed, uint lane) =>
        (Hash(seed ^ (0x9E3779B9u * (lane + 1u))) >> 8) *
        (1f / 16777216f);
}
