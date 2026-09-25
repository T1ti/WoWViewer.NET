using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Structs;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class M2ParticleSmokeTests
{
    [TestMethod]
    public void PausedParticlesReuseLastMeshAcrossFramesAndCameraChanges()
    {
        static M2Container Placement() =>
            (M2Container)RuntimeHelpers.GetUninitializedObject(typeof(M2Container));

        var animation = CreateAnimation();
        animation.Particles = [CreateEmitter()];
        var renderer = new M2EffectRenderer(default, default);
        var instance = Placement();
        var liveFrame = new M2AnimationFrameKey(0, 750);
        var live = renderer.GetParticleMesh(instance, animation, 0,
            liveFrame, Matrix4x4.Identity);
        Assert.IsTrue(live.Vertices.Length > 0);

        var movedCamera = Matrix4x4.CreateRotationY(0.5f);
        var frozen = renderer.GetFrozenParticleMesh(instance, animation, 0,
            new M2AnimationFrameKey(0, 900), movedCamera);
        var frozenAgain = renderer.GetFrozenParticleMesh(instance, animation, 0,
            new M2AnimationFrameKey(0, 950), Matrix4x4.CreateRotationY(1f));
        Assert.AreSame(live.Vertices, frozen.Vertices);
        Assert.AreSame(frozen.Vertices, frozenAgain.Vertices);
        Assert.AreSame(frozen.Indices, frozenAgain.Indices);

        var resumed = renderer.GetParticleMesh(instance, animation, 0,
            new M2AnimationFrameKey(0, 900), movedCamera);
        Assert.AreNotSame(live.Vertices, resumed.Vertices);

        var neverAnimated = Placement();
        var fallback = renderer.GetFrozenParticleMesh(neverAnimated, animation, 0,
            liveFrame, Matrix4x4.Identity);
        Assert.IsTrue(fallback.Vertices.Length > 0);
        Assert.AreSame(fallback.Vertices, renderer.GetFrozenParticleMesh(
            neverAnimated, animation, 0, new M2AnimationFrameKey(0, 900),
            movedCamera).Vertices);
        fallback.Vertices[0] = fallback.Vertices[0] with
        {
            Position = new Vector3(1234f, 1234f, 1234f)
        };
        var restarted = renderer.GetParticleMesh(
            neverAnimated, animation, 0, liveFrame, Matrix4x4.Identity);
        Assert.AreNotEqual(1234f, restarted.Vertices[0].Position.X);
    }

    [TestMethod]
    public void StableParticleCountsReuseBuffersWithoutSteadyFrameAllocations()
    {
        var animation = CreateAnimation();
        var emitter = CreateEmitter();
        var mesh = M2ParticleMeshBuilder.BuildSupported(animation, emitter, 0,
            750, 123u, Matrix4x4.Identity);
        for (var i = 0; i < 20; i++)
            mesh = M2ParticleMeshBuilder.BuildSupported(animation, emitter, 0,
                751, 123u, Matrix4x4.Identity, mesh.Vertices, mesh.Indices);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
            mesh = M2ParticleMeshBuilder.BuildSupported(animation, emitter, 0,
                751, 123u, Matrix4x4.Identity, mesh.Vertices, mesh.Indices);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsTrue(allocated < 32_768L,
            $"Stable emitter allocated {allocated} bytes across 200 updates.");
    }

    [TestMethod]
    public void M2EffectBlendIdsUseClientBlendStates()
    {
        Assert.AreEqual(10, SceneManager.GetM2BlendStateIndex(3));
        Assert.AreEqual(3, SceneManager.GetM2BlendStateIndex(4));
        Assert.AreEqual(4, SceneManager.GetM2BlendStateIndex(5));
        Assert.AreEqual(5, SceneManager.GetM2BlendStateIndex(6));
    }

    [TestMethod]
    public void ConstantRateBirthsUseHalfIntegerCadenceAndLifetimeCurves()
    {
        var animation = CreateAnimation();
        var emitter = CreateEmitter();
        Assert.IsTrue(M2ParticleMeshBuilder.IsSupported(animation, emitter));

        var mesh = M2ParticleMeshBuilder.Build(animation, emitter, 0, 750,
            123u, Matrix4x4.Identity);
        Assert.AreEqual(12, mesh.Vertices.Length);
        Assert.AreEqual(18, mesh.Indices.Length);
        Assert.AreEqual(0.625f, mesh.Vertices[0].Position.Z, 0.0001f);
        Assert.AreEqual(0.375f, mesh.Vertices[4].Position.Z, 0.0001f);
        Assert.AreEqual(0.125f, mesh.Vertices[8].Position.Z, 0.0001f);
        Assert.AreEqual(0.625f, mesh.Vertices[0].Color.W, 0.0001f);
        Assert.AreEqual(0.5f, mesh.Vertices[0].TexCoord.X, 0.0001f);
        Assert.AreEqual(0.5f, mesh.Vertices[0].TexCoord.Y, 0.0001f);
        CollectionAssert.AreEqual(new ushort[] { 0, 1, 2, 0, 2, 3 },
            mesh.Indices[..6]);

        var repeated = M2ParticleMeshBuilder.Build(animation, emitter, 0, 750,
            123u, Matrix4x4.Identity);
        CollectionAssert.AreEqual(mesh.Vertices, repeated.Vertices);
        CollectionAssert.AreEqual(mesh.Indices, repeated.Indices);

        var reused = M2ParticleMeshBuilder.BuildSupported(animation, emitter, 0, 751,
            123u, Matrix4x4.Identity, repeated.Vertices, repeated.Indices);
        Assert.AreSame(repeated.Vertices, reused.Vertices);
        Assert.AreSame(repeated.Indices, reused.Indices);

        var translatedCamera = Matrix4x4.CreateTranslation(0f, 0f, -1f);
        var behindCamera = M2ParticleMeshBuilder.Build(animation, emitter, 0,
            750, 123u, translatedCamera);
        Assert.AreEqual(0.625f, behindCamera.Vertices[0].Position.Z, 0.0001f);
    }

    [TestMethod]
    public void UnsupportedEmitterBehaviourIsRefused()
    {
        var animation = CreateAnimation();
        var emitter = CreateEmitter() with { Flags = 0x20000u | 0x800u };
        Assert.IsFalse(M2ParticleMeshBuilder.IsSupported(animation, emitter));
        Assert.AreEqual(0, M2ParticleMeshBuilder.Build(animation, emitter, 0,
            750, 123u, Matrix4x4.Identity).Vertices.Length);

        emitter = CreateEmitter() with
        {
            EmissionRate = Track(new M2Timeline<float>([500, 0], [4f, 8f]))
        };
        Assert.IsFalse(M2ParticleMeshBuilder.IsSupported(animation, emitter));

        emitter = CreateEmitter() with
        {
            Gravity = Track(new M2Timeline<float>([0, 1000], [0f, 2f]))
        };
        Assert.IsFalse(M2ParticleMeshBuilder.IsSupported(animation, emitter));

        emitter = CreateEmitter() with { Flags = 0x20000u | 0x80u };
        Assert.IsTrue(M2ParticleMeshBuilder.IsSupported(animation, emitter));
        Assert.IsFalse(M2ParticleMeshBuilder.IsSupported(animation,
            emitter with { EmitterType = 2 }));

        // A stationary chain contributes no inherited burst velocity, as in
        // the client. This admits stationary sparkler emitters with flag 0x40.
        Assert.IsTrue(M2ParticleMeshBuilder.IsSupported(animation,
            CreateEmitter() with { Flags = 0x40040u }));
    }

    [TestMethod]
    public void LinearEmissionRateIntegratesBirthsAcrossTheSequence()
    {
        var animation = CreateAnimation();
        var emitter = CreateEmitter() with
        {
            EmissionRate = Track(new M2Timeline<float>([0, 1000], [0f, 8f]))
        };
        Assert.IsTrue(M2ParticleMeshBuilder.IsSupported(animation, emitter));
        var halfway = M2ParticleMeshBuilder.Build(animation, emitter, 0,
            500, 123u, Matrix4x4.Identity);
        Assert.AreEqual(4, halfway.Vertices.Length);
        Assert.AreEqual(0.1464466f, halfway.Vertices[0].Position.Z, 0.0001f);

        var end = M2ParticleMeshBuilder.Build(animation, emitter, 0,
            1000, 123u, Matrix4x4.Identity);
        Assert.AreEqual(16, end.Vertices.Length);
        Assert.AreEqual(0.6464466f, end.Vertices[0].Position.Z, 0.0001f);
    }

    [TestMethod]
    public void StepEmissionRateStartsBirthsAfterTheRateChange()
    {
        var animation = CreateAnimation();
        var emitter = CreateEmitter() with
        {
            EmissionRate = new M2Track<float>
            {
                Interpolation = 0,
                GlobalSequence = -1,
                Timelines = [new M2Timeline<float>([0, 500, 1000],
                    [0f, 4f, 0f])]
            }
        };
        var mesh = M2ParticleMeshBuilder.Build(animation, emitter, 0,
            1000, 123u, Matrix4x4.Identity);
        Assert.AreEqual(8, mesh.Vertices.Length);
        Assert.AreEqual(0.375f, mesh.Vertices[0].Position.Z, 0.0001f);
        Assert.AreEqual(0.125f, mesh.Vertices[4].Position.Z, 0.0001f);

        var stopped = emitter with
        {
            EmissionRate = new M2Track<float>
            {
                Interpolation = 0,
                GlobalSequence = -1,
                Timelines = [new M2Timeline<float>([0, 500, 1000],
                    [4f, 0f, 0f])]
            }
        };
        mesh = M2ParticleMeshBuilder.Build(animation, stopped, 0,
            750, 123u, Matrix4x4.Identity);
        Assert.AreEqual(8, mesh.Vertices.Length);
    }

    [TestMethod]
    public void DuplicateRateKeyTimeCreatesAnInstantaneousChange()
    {
        var animation = CreateAnimation();
        var emitter = CreateEmitter() with
        {
            EmissionRate = Track(new M2Timeline<float>(
                [0, 500, 500, 1000], [4f, 4f, 8f, 8f]))
        };
        Assert.IsTrue(M2ParticleMeshBuilder.IsSupported(animation, emitter));
        var mesh = M2ParticleMeshBuilder.Build(animation, emitter, 0,
            750, 123u, Matrix4x4.Identity);
        Assert.AreEqual(16, mesh.Vertices.Length);
    }

    [TestMethod]
    public void LoopingRateRepeatsItsIntegratedPeriod()
    {
        var animation = new M2Animation
        {
            Sequences = [new M2Sequence(1000, 0)],
            GlobalLoops = [],
            Bones = [new M2Bone(-1, 0, Vector3.Zero,
                Track<Vector3>(), Track<Quaternion>(), Track<Vector3>())]
        };
        var emitter = CreateEmitter() with
        {
            EmissionRate = Track(new M2Timeline<float>([0, 1000], [0f, 8f]))
        };
        var mesh = M2ParticleMeshBuilder.Build(animation, emitter, 0,
            1500, 123u, Matrix4x4.Identity);
        Assert.AreEqual(16, mesh.Vertices.Length);
        Assert.AreEqual(0.1464466f, mesh.Vertices[12].Position.Z, 0.0001f);
    }

    [TestMethod]
    public void NegativeRateIsClampedBeforeIntegratingBirths()
    {
        var animation = CreateAnimation();
        var emitter = CreateEmitter() with
        {
            EmissionRate = Track(new M2Timeline<float>([0, 1000], [-4f, 4f]))
        };
        var mesh = M2ParticleMeshBuilder.Build(animation, emitter, 0,
            1000, 123u, Matrix4x4.Identity);
        Assert.AreEqual(4, mesh.Vertices.Length);
        Assert.AreEqual(0.1464466f, mesh.Vertices[0].Position.Z, 0.0001f);
    }

    [TestMethod]
    public void GlobalLoopRateRepeatsEvenForANonloopingSequence()
    {
        var animation = new M2Animation
        {
            Sequences = [new M2Sequence(2000, 1)],
            GlobalLoops = [1000],
            Bones = [new M2Bone(-1, 0, Vector3.Zero,
                Track<Vector3>(), Track<Quaternion>(), Track<Vector3>())]
        };
        var emitter = CreateEmitter() with
        {
            EmissionRate = new M2Track<float>
            {
                Interpolation = 1,
                GlobalSequence = 0,
                Timelines = [new M2Timeline<float>([0, 1000], [0f, 8f])]
            }
        };
        var mesh = M2ParticleMeshBuilder.Build(animation, emitter, 0,
            1500, 123u, Matrix4x4.Identity);
        Assert.AreEqual(16, mesh.Vertices.Length);
        Assert.AreEqual(0.1464466f, mesh.Vertices[12].Position.Z, 0.0001f);
    }

    [TestMethod]
    public void TailOnlyAndHeadTailEmittersUseTailCellAndAgeClamp()
    {
        var animation = CreateAnimation();
        var tail = CreateEmitter() with
        {
            Flags = 0x40000u | 0x400u,
            TailLength = 1f,
            TailCell = new M2ParticleLifeTrack<float>([0], [2f])
        };
        var camera = Matrix4x4.CreateRotationY(MathF.PI / 4f);
        Assert.IsTrue(M2ParticleMeshBuilder.IsSupported(animation, tail));
        var mesh = M2ParticleMeshBuilder.Build(animation, tail, 0, 750,
            123u, camera);
        Assert.AreEqual(12, mesh.Vertices.Length);
        Assert.AreEqual(18, mesh.Indices.Length);
        Assert.AreEqual(0f, mesh.Vertices[0].TexCoord.X, 0.0001f);
        Assert.AreEqual(1f, mesh.Vertices[0].TexCoord.Y, 0.0001f);
        Assert.AreEqual(-0.625f,
            mesh.Vertices[1].Position.Z - mesh.Vertices[0].Position.Z, 0.0001f);

        var both = tail with { Flags = tail.Flags | 0x20000u };
        mesh = M2ParticleMeshBuilder.Build(animation, both, 0, 750,
            123u, camera);
        Assert.AreEqual(24, mesh.Vertices.Length);
        Assert.AreEqual(36, mesh.Indices.Length);
        Assert.AreEqual(0.5f, mesh.Vertices[0].TexCoord.X, 0.0001f);
        Assert.AreEqual(1f, mesh.Vertices[4].TexCoord.Y, 0.0001f);
    }

    [TestMethod]
    public void VelocityAlignedHeadUsesTravelDirection()
    {
        var animation = CreateAnimation();
        var emitter = CreateEmitter();
        var camera = Matrix4x4.CreateRotationY(MathF.PI / 4f);
        var ordinary = M2ParticleMeshBuilder.Build(animation, emitter, 0, 750,
            123u, camera);
        var aligned = M2ParticleMeshBuilder.Build(animation,
            emitter with { Flags = emitter.Flags | 0x4u }, 0, 750,
            123u, camera);
        Assert.AreEqual(ordinary.Vertices.Length, aligned.Vertices.Length);
        Assert.AreNotEqual(ordinary.Vertices[0].Position.X,
            aligned.Vertices[0].Position.X);
    }

    [TestMethod]
    public void TumbleUsesEmitterPlaneInsteadOfCameraPlane()
    {
        var animation = CreateAnimation();
        var emitter = CreateEmitter();
        var camera = Matrix4x4.CreateRotationX(MathF.PI / 4f);
        var ordinary = M2ParticleMeshBuilder.Build(animation, emitter, 0, 750,
            123u, camera);
        var tumbled = M2ParticleMeshBuilder.Build(animation,
            emitter with { Flags = emitter.Flags | 0x1000u }, 0, 750,
            123u, camera);
        Assert.AreEqual(ordinary.Vertices.Length, tumbled.Vertices.Length);
        Assert.AreNotEqual(ordinary.Vertices[0].Position.Z,
            tumbled.Vertices[0].Position.Z);
    }

    [TestMethod]
    public void WindAccelerationStopsAtAuthoredTimeAndParticleKeepsCoasting()
    {
        var animation = CreateAnimation();
        var emitter = CreateEmitter() with
        {
            Wind = new Vector3(2f, 0f, 0f),
            WindTime = 0.25f,
            LifespanVariation = 0.5f
        };
        Assert.IsTrue(M2ParticleMeshBuilder.IsSupported(animation, emitter));
        var mesh = M2ParticleMeshBuilder.Build(animation, emitter, 0, 750,
            123u, Matrix4x4.Identity);
        Assert.AreEqual(-0.75f, mesh.Vertices[0].Position.X, 0.0001f);
        Assert.AreEqual(-0.984375f, mesh.Vertices[8].Position.X, 0.0001f);
    }

    [TestMethod]
    public void CurrentBoneScaleSizesParticleQuads()
    {
        var animation = new M2Animation
        {
            Sequences = [new M2Sequence(1000, 1)],
            GlobalLoops = [],
            HasAnimatedBones = true,
            Bones = [new M2Bone(-1, 0x200, Vector3.Zero,
                Track<Vector3>(), Track<Quaternion>(),
                Track(new M2Timeline<Vector3>([0], [new Vector3(2f)])))]
        };
        var emitter = CreateEmitter() with { Flags = 0x20000u | 0x20u };
        Assert.IsTrue(M2ParticleMeshBuilder.IsSupported(animation, emitter));
        var mesh = M2ParticleMeshBuilder.Build(animation, emitter, 0, 750,
            123u, Matrix4x4.Identity);
        Assert.AreEqual(4f,
            mesh.Vertices[1].Position.X - mesh.Vertices[0].Position.X, 0.0001f);
    }

    [TestMethod]
    public void MovingEmitterBoneIsEvaluatedAtEachParticleBirth()
    {
        var animation = new M2Animation
        {
            Sequences = [new M2Sequence(1000, 1)],
            GlobalLoops = [],
            HasAnimatedBones = true,
            Bones = [new M2Bone(-1, 0x200, Vector3.Zero,
                Track(new M2Timeline<Vector3>([0, 1000],
                    [Vector3.Zero, Vector3.UnitX])),
                Track<Quaternion>(), Track<Vector3>())]
        };
        var mesh = M2ParticleMeshBuilder.Build(animation, CreateEmitter(), 0,
            750, 123u, Matrix4x4.Identity);
        Assert.AreEqual(12, mesh.Vertices.Length);
        Assert.AreEqual(-0.875f, mesh.Vertices[0].Position.X, 0.0001f);
        Assert.AreEqual(-0.625f, mesh.Vertices[4].Position.X, 0.0001f);
        Assert.AreEqual(-0.375f, mesh.Vertices[8].Position.X, 0.0001f);
        Assert.IsFalse(M2ParticleMeshBuilder.IsSupported(animation,
            CreateEmitter() with { Flags = 0x20040u }));
    }

    [TestMethod]
    public void WorldSpaceEmitterRetainsItsBirthBoneFrame()
    {
        var animation = new M2Animation
        {
            Sequences = [new M2Sequence(1000, 1)],
            GlobalLoops = [],
            HasAnimatedBones = true,
            Bones = [new M2Bone(-1, 0x200, Vector3.Zero,
                Track(new M2Timeline<Vector3>([0, 1000],
                    [Vector3.Zero, Vector3.UnitX])),
                Track<Quaternion>(), Track<Vector3>())]
        };
        var emitter = CreateEmitter() with { Flags = 0x20010u };
        Assert.IsTrue(M2ParticleMeshBuilder.IsSupported(animation, emitter));
        var mesh = M2ParticleMeshBuilder.Build(animation, emitter, 0,
            750, 123u, Matrix4x4.Identity);
        Assert.AreEqual(12, mesh.Vertices.Length);
        Assert.AreEqual(-0.875f, mesh.Vertices[0].Position.X, 0.0001f);
        Assert.AreEqual(-0.625f, mesh.Vertices[4].Position.X, 0.0001f);
        Assert.AreEqual(-0.375f, mesh.Vertices[8].Position.X, 0.0001f);
    }

    [TestMethod]
    public void RotatingWorldSpaceEmitterSpreadsBirthsAcrossThePortalPlane()
    {
        var animation = new M2Animation
        {
            Sequences = [new M2Sequence(1000, 1)],
            GlobalLoops = [],
            HasAnimatedBones = true,
            Bones = [new M2Bone(-1, 0x200, Vector3.Zero,
                Track<Vector3>(),
                Track(new M2Timeline<Quaternion>([0, 1000],
                    [Quaternion.Identity,
                     Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f)])),
                Track<Vector3>())]
        };
        var emitter = CreateEmitter() with { Flags = 0x20010u };
        var mesh = M2ParticleMeshBuilder.Build(animation, emitter, 0,
            750, 123u, Matrix4x4.Identity);
        Assert.AreEqual(12, mesh.Vertices.Length);
        for (var particle = 0; particle < 3; particle++)
        {
            var born = 125d + particle * 250d;
            var age = (float)((750d - born) / 1000d);
            var bone = animation.EvaluateRigidBone(0, 0, born);
            var expectedCenter = Vector3.TransformNormal(Vector3.UnitZ, bone) * age;
            var center = Vector3.Zero;
            for (var corner = 0; corner < 4; corner++)
                center += mesh.Vertices[particle * 4 + corner].Position * 0.25f;
            Assert.AreEqual(expectedCenter.Y, center.Y, 0.0001f);
            Assert.AreEqual(expectedCenter.Z, center.Z, 0.0001f);
        }
    }

    [TestMethod]
    public void ZeroHorizontalRangeSphereCoversThePortalPlane()
    {
        var animation = new M2Animation
        {
            Sequences = [new M2Sequence(1000, 0)],
            GlobalLoops = [],
            HasAnimatedBones = true,
            Bones = [new M2Bone(-1, 0x200, Vector3.Zero,
                Track<Vector3>(),
                Track(new M2Timeline<Quaternion>([0, 1000],
                    [Quaternion.Identity,
                     Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f)])),
                Track<Vector3>())]
        };
        var emitter = CreateEmitter() with
        {
            EmitterType = 2,
            Flags = 0x20010u,
            EmissionSpeed = Track(new M2Timeline<float>([0], [0f])),
            VerticalRange = Track(new M2Timeline<float>([0], [MathF.PI])),
            HorizontalRange = Track(new M2Timeline<float>([0], [0f])),
            AreaWidth = Track(new M2Timeline<float>([0], [4f])),
            AreaLength = Track(new M2Timeline<float>([0], [4f])),
            EmissionRate = Track(new M2Timeline<float>([0], [200f])),
            Lifespan = Track(new M2Timeline<float>([0], [2f]))
        };
        Assert.IsTrue(M2ParticleMeshBuilder.IsSupported(animation, emitter));
        var mesh = M2ParticleMeshBuilder.Build(animation, emitter, 0, 1000,
            17u, Matrix4x4.Identity);
        Assert.AreEqual(200 * 4, mesh.Vertices.Length);
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        var minZ = float.PositiveInfinity;
        var maxZ = float.NegativeInfinity;
        for (var i = 0; i < mesh.Vertices.Length; i += 4)
        {
            var center = (mesh.Vertices[i].Position +
                mesh.Vertices[i + 1].Position + mesh.Vertices[i + 2].Position +
                mesh.Vertices[i + 3].Position) * 0.25f;
            Assert.AreEqual(0f, center.X, 0.0001f);
            minY = MathF.Min(minY, center.Y);
            maxY = MathF.Max(maxY, center.Y);
            minZ = MathF.Min(minZ, center.Z);
            maxZ = MathF.Max(maxZ, center.Z);
        }
        Assert.IsTrue(minY < -3f && maxY > 3f);
        Assert.IsTrue(minZ < -3f && maxZ > 3f);
    }

    [TestMethod]
    public void RigidBoneChainMatchesFullPaletteEvaluation()
    {
        var animation = new M2Animation
        {
            Sequences = [new M2Sequence(1000, 1)],
            GlobalLoops = [],
            HasAnimatedBones = true,
            Bones =
            [
                new M2Bone(-1, 0x200, new Vector3(0.5f, 0f, 0f),
                    Track(new M2Timeline<Vector3>([0, 1000],
                        [Vector3.Zero, Vector3.UnitX])),
                    Track<Quaternion>(), Track<Vector3>()),
                new M2Bone(0, 0x200, Vector3.UnitY,
                    Track<Vector3>(),
                    Track(new M2Timeline<Quaternion>([0],
                        [Quaternion.CreateFromAxisAngle(Vector3.UnitZ,
                            MathF.PI / 4f)])),
                    Track(new M2Timeline<Vector3>([0],
                        [new Vector3(1.5f)])))
            ]
        };
        Assert.IsTrue(animation.HasRigidBoneChain(1));
        Assert.IsTrue(animation.HasTimeDependentBoneChain(1));
        var palette = new Matrix4x4[animation.Bones.Length];
        animation.Evaluate(0, 375, palette);
        var fast = animation.EvaluateRigidBone(1, 0, 375);
        var point = new Vector3(0.3f, -0.5f, 0.7f);
        var expected = Vector3.Transform(point, palette[1]);
        var actual = Vector3.Transform(point, fast);
        Assert.AreEqual(expected.X, actual.X, 0.0001f);
        Assert.AreEqual(expected.Y, actual.Y, 0.0001f);
        Assert.AreEqual(expected.Z, actual.Z, 0.0001f);
    }

    [TestMethod]
    public void TwinkleUsesWotlkLookupTablesLowScale()
    {
        var animation = CreateAnimation();
        var emitter = CreateEmitter() with
        {
            TwinkleSpeed = 5f,
            TwinklePercent = 0.1f,
            TwinkleScale = new Vector2(0.5f, 2f)
        };
        Assert.IsTrue(M2ParticleMeshBuilder.IsSupported(animation, emitter));
        var mesh = M2ParticleMeshBuilder.Build(animation, emitter, 0, 750,
            123u, Matrix4x4.Identity);
        Assert.AreEqual(12, mesh.Vertices.Length);
        Assert.AreEqual(1f,
            mesh.Vertices[1].Position.X - mesh.Vertices[0].Position.X, 0.0001f);
    }

    private static M2Animation CreateAnimation() => new()
    {
        Sequences = [new M2Sequence(1000, 1)],
        GlobalLoops = [],
        Bones = [new M2Bone(-1, 0, Vector3.Zero,
            Track<Vector3>(), Track<Quaternion>(), Track<Vector3>())]
    };

    private static M2ParticleAnimation CreateEmitter() => new(
        SourceIndex: 0,
        BoneIndex: 0,
        Position: Vector3.Zero,
        TextureFileDataId: 1,
        TextureFlags: 0,
        Flags: 0x20000u,
        BlendMode: 2,
        EmitterType: 1,
        ColorIndex: 0,
        PriorityPlane: 0,
        Rows: 2,
        Columns: 2,
        HasChildModels: false,
        LifespanVariation: 0f,
        EmissionRateVariation: 0f,
        ScaleVariation: Vector2.Zero,
        Drag: 0f,
        BaseSpin: 0f,
        BaseSpinVariation: 0f,
        SpinSpeed: 0f,
        SpinSpeedVariation: 0f,
        TwinkleSpeed: 0f,
        TwinklePercent: 1f,
        TwinkleScale: Vector2.One,
        TailLength: 0f,
        Wind: Vector3.Zero,
        WindTime: 0f,
        EmissionSpeed: Track(new M2Timeline<float>([0], [1f])),
        SpeedVariation: Track<float>(),
        VerticalRange: Track<float>(),
        HorizontalRange: Track<float>(),
        Gravity: Track<float>(),
        Lifespan: Track(new M2Timeline<float>([0], [1f])),
        EmissionRate: Track(new M2Timeline<float>([0], [4f])),
        AreaWidth: Track<float>(),
        AreaLength: Track<float>(),
        ZSource: Track<float>(),
        Enabled: Track<float>(),
        Color: new M2ParticleLifeTrack<Vector3>([0], [Vector3.One]),
        Alpha: new M2ParticleLifeTrack<float>([0, 32767], [0f, 1f]),
        Scale: new M2ParticleLifeTrack<Vector2>([0], [Vector2.One]),
        HeadCell: new M2ParticleLifeTrack<float>([0], [1f]),
        TailCell: new M2ParticleLifeTrack<float>([0], [0f]));

    private static M2Track<T> Track<T>(params M2Timeline<T>[] timelines) => new()
    {
        Interpolation = 1,
        GlobalSequence = -1,
        Timelines = timelines
    };
}
