using System.Numerics;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Renderer;

internal enum Wrath335DoodadFog : byte { Unresolved, Scene, Staged, Current, Rejected }

/// <summary>Retains callback frusta and snapshots the 0x799B70 consumers in submission order.</summary>
internal sealed class Wrath335WmoDoodadVisibility
{
    private WorldModelGroupBatches[]? sourceGroups;
    private int[][] owners = [];
    private int[] heads = [];
    private int[] consumerHeads = [];
    private Wrath335DoodadFog[] results = [];
    private Wrath335DoodadFog[] fogHistory = [];
    private BoundingSphere?[] exteriorSpheres = [];
    private bool[] exteriorQueued = [], exteriorConsumed = [];
    private byte[] sizeCategories = [], groupMinimumCategories = [];
    private float[] opacities = [];
    private Wrath335DoodadFade? fade;
    private Vector3 eye, forward;
    private readonly List<int> callbackOrder = [];
    private readonly List<Node> frusta = [];
    private readonly List<Consumer> consumers = [];
    private Matrix4x4 viewProjection;
    public bool Prepared { get; private set; }

    private readonly record struct Node(Wrath335DoodadFrustum Frustum, int Previous);
    private readonly record struct Consumer(int Head, Wrath335DoodadFog Fog, byte MinimumCategory, int Previous);

    public void Clear()
    {
        Prepared = false;
        callbackOrder.Clear();
        frusta.Clear();
        consumers.Clear();
        results.AsSpan().Clear();
        exteriorSpheres.AsSpan().Clear();
        exteriorQueued.AsSpan().Clear();
        exteriorConsumed.AsSpan().Clear();
        Array.Fill(sizeCategories, byte.MaxValue); // Unknown bounds retain the existing adapter behavior.
        Array.Fill(opacities, 1f);
        groupMinimumCategories.AsSpan().Clear();
        fade = null;
    }

    public void Begin(WorldModelGroupBatches[] groups, int doodadCount, in Matrix4x4 projection)
    {
        Clear();
        viewProjection = projection;
        if (!ReferenceEquals(groups, sourceGroups) || owners.Length != doodadCount)
        {
            // Resource identity is stable after upload; reverse MODR links are built once.
            sourceGroups = groups;
            var links = new List<int>[doodadCount];
            for (var group = 0; group < groups.Length; group++)
                foreach (var index in groups[group].doodadReferences ?? [])
                    if (index < doodadCount)
                    {
                        var list = links[index] ??= [];
                        if (!list.Contains(group)) list.Add(group);
                    }
            owners = new int[doodadCount][];
            for (var index = 0; index < doodadCount; index++)
                owners[index] = links[index]?.ToArray() ?? [];
            heads = new int[groups.Length];
            consumerHeads = new int[groups.Length];
            results = new Wrath335DoodadFog[doodadCount];
            fogHistory = new Wrath335DoodadFog[doodadCount];
            // 0x7BF005/0x7BF012 assigns fresh MODD flags 1/0x1001: bit 0x8000 is clear.
            Array.Fill(fogHistory, Wrath335DoodadFog.Staged);
            exteriorSpheres = new BoundingSphere?[doodadCount];
            exteriorQueued = new bool[doodadCount];
            exteriorConsumed = new bool[doodadCount];
            sizeCategories = new byte[doodadCount];
            Array.Fill(sizeCategories, byte.MaxValue);
            groupMinimumCategories = new byte[groups.Length];
            opacities = new float[doodadCount];
            Array.Fill(opacities, 1f);
        }
        Array.Fill(heads, -1);
        Array.Fill(consumerHeads, -1);
    }

    public void ConfigureFade(Wrath335DoodadFade? profile, Vector3 cameraEye, Vector3 cameraForward)
    {
        fade = profile;
        eye = cameraEye;
        forward = cameraForward;
    }

    public void RecordCallback(int group, WmoPortalRect view, BoundingBox? worldBounds = null)
    {
        if (heads[group] < 0)
        {
            callbackOrder.Add(group); // 0x6DED60 appends, despite the guessed IDB name.
            // 0x7993CF writes the full-direction depth only on first callback.
            if (fade is { } profile && worldBounds is { } bounds)
                groupMinimumCategories[group] = profile.MinimumCategory(Wrath335DoodadFade.GroupDistance(bounds, eye, forward));
        }
        frusta.Add(new(Wrath335DoodadFrustum.Create(viewProjection, view), heads[group]));
        heads[group] = frusta.Count - 1;
    }

    public void ConsumeGroup(int group, bool propagated)
    {
        if (heads[group] >= 0)
            AddConsumer(group, heads[group], propagated ? Wrath335DoodadFog.Current : Wrath335DoodadFog.Staged);
    }

    public void SetExteriorSphere(int doodad, in BoundingSphere sphere, BoundingBox? worldBounds = null)
    {
        if ((uint)doodad < (uint)exteriorSpheres.Length)
        {
            exteriorSpheres[doodad] = sphere;
            sizeCategories[doodad] = worldBounds is { } bounds ? Wrath335DoodadFade.SizeCategory(bounds) : byte.MaxValue;
        }
    }

    internal bool TryGetExteriorSphere(int doodad, out BoundingSphere sphere)
    {
        sphere = default;
        if ((uint)doodad >= (uint)exteriorSpheres.Length || exteriorQueued[doodad] ||
            exteriorSpheres[doodad] is not { } bounds)
            return false;
        sphere = bounds;
        return true;
    }

    internal void SetExteriorQueued(int doodad, bool queued) => exteriorQueued[doodad] = queued;

    internal void AdmitExterior(int doodad, in BoundingSphere sphere,
        in Wrath335DoodadFrustum frustum, Wrath335ClipVolumes volumes, Wrath335TerrainClipBuffer terrain,
        float bandDistance = -1f)
    {
        // 0x798821 rejects size categories BEFORE restamping pending. This gate
        // applies even with objectFade disabled; it is not the submission fade gate.
        if (fade is { } profile && sizeCategories[doodad] < profile.MinimumCategory(bandDistance))
            return;
        // 0x798859/0x798862 restamps pending on each bucket visit. A previously
        // submitted model stays submitted even if another owner enlists it later.
        var submitted = exteriorConsumed[doodad] && results[doodad] != Wrath335DoodadFog.Rejected;
        if (!frustum.Intersects(sphere) || volumes.ContainsSphere(new(sphere.Center, sphere.Radius)))
        {
            if (!submitted)
            {
                exteriorConsumed[doodad] = false;
                results[doodad] = Wrath335DoodadFog.Unresolved;
            }
            return; // Still pending for portal consumers, unlike terrain rejection.
        }
        exteriorConsumed[doodad] = true; // 0x79889E clears pending BEFORE 0x78FC40.
        if (!submitted)
            results[doodad] = terrain.ContainsSphere(sphere, 16) ? Wrath335DoodadFog.Rejected :
                fogHistory[doodad]; // Bucket admission does not write runtime fog bit 0x8000.
    }

    private void AddConsumer(int group, int head, Wrath335DoodadFog fog)
    {
        consumers.Add(new(head, fog, groupMinimumCategories[group], consumerHeads[group]));
        consumerHeads[group] = consumers.Count - 1;
    }

    public void Finish(ReadOnlySpan<WorldModelGroupBatches> groups, ReadOnlySpan<bool> propagated,
        bool primaryPlacement)
    {
        // 0x79A260 consumes first callback arrival order, after unbucketed consumers.
        foreach (var group in callbackOrder)
            if (primaryPlacement || (groups[group].mogiFlags & 0x10008) == 0)
                ConsumeGroup(group, propagated[group]);
        Prepared = true;
        // Loaded static spheres let portal fog writes occur during CPU preparation,
        // even when a later GPU tile/distance gate drops the M2 draw.
        for (var doodad = 0; doodad < exteriorSpheres.Length; doodad++)
            if (exteriorSpheres[doodad] is { } sphere)
                Accept(doodad, sphere);
    }

    public bool Accept(int doodad, in BoundingSphere sphere)
    {
        if ((uint)doodad >= (uint)owners.Length)
            return true;
        if (exteriorConsumed[doodad])
            return results[doodad] != Wrath335DoodadFog.Rejected && Submit(doodad, sphere);
        var fog = Wrath335DoodadFog.Rejected;
        var firstAccepted = consumers.Count;
        // Walk only this definition's owners, while comparing global consumer
        // ordinals. The MODR reverse index avoids scanning every visible group.
        foreach (var group in owners[doodad])
        {
            for (var index = consumerHeads[group]; index >= 0; index = consumers[index].Previous)
            {
                if (index >= firstAccepted)
                    continue;
                var consumer = consumers[index];
                if (sizeCategories[doodad] < consumer.MinimumCategory)
                    continue;
                for (var node = consumer.Head; node >= 0; node = frusta[node].Previous)
                    if (frusta[node].Frustum.Intersects(sphere))
                    {
                        firstAccepted = index;
                        fog = consumer.Fog;
                        break;
                    }
            }
        }
        // A failed owner leaves the definition pending; only the first accepted
        // consumer writes fog (0x799C36..0x799CA8). Re-evaluate if bounds change.
        results[doodad] = fog;
        if (fog == Wrath335DoodadFog.Current || fog == Wrath335DoodadFog.Staged)
            fogHistory[doodad] = fog;
        return fog != Wrath335DoodadFog.Rejected && Submit(doodad, sphere);
    }

    private bool Submit(int doodad, in BoundingSphere sphere)
    {
        opacities[doodad] = 1f;
        // Fog/pending writes precede 0x791CB0; distance rejection must not undo them.
        return fade is not { } profile || profile.TryGetOpacity(sizeCategories[doodad],
            Wrath335DoodadFade.DistanceSquared(sphere.Center, eye), out opacities[doodad]);
    }

    internal float GetSubmissionOpacity(int doodad) =>
        Prepared && (uint)doodad < (uint)opacities.Length ? opacities[doodad] : 1f;

    internal bool TryGetSubmissionOpacity(int doodad, out float opacity)
    {
        opacity = 1f;
        if (!Prepared || fade is null || (uint)doodad >= (uint)opacities.Length ||
            results[doodad] is Wrath335DoodadFog.Unresolved or Wrath335DoodadFog.Rejected)
            return false;
        opacity = opacities[doodad];
        return true;
    }

    public bool TryGetCurrentFog(int doodad, out bool current)
    {
        var fog = (uint)doodad < (uint)results.Length ? results[doodad] : Wrath335DoodadFog.Unresolved;
        current = fog == Wrath335DoodadFog.Current;
        return Prepared && (fog == Wrath335DoodadFog.Current || fog == Wrath335DoodadFog.Staged);
    }
}

/// <summary>Six normalized world planes. Portal polygon clipping's omitted near plane does not apply.</summary>
internal readonly record struct Wrath335DoodadFrustum(
    Vector4 Left, Vector4 Right, Vector4 Bottom, Vector4 Top, Vector4 Near, Vector4 Far)
{
    public static Wrath335DoodadFrustum Create(in Matrix4x4 matrix, WmoPortalRect view)
    {
        var x = new Vector4(matrix.M11, matrix.M21, matrix.M31, matrix.M41);
        var y = new Vector4(matrix.M12, matrix.M22, matrix.M32, matrix.M42);
        var z = new Vector4(matrix.M13, matrix.M23, matrix.M33, matrix.M43);
        var w = new Vector4(matrix.M14, matrix.M24, matrix.M34, matrix.M44);
        // 0x790E20 crops the base near/far corners to the callback rectangle;
        // DX11's equivalent depth interval is [0,1], not OpenGL's [-1,1].
        return new(Normalize(x - view.MinX * w), Normalize(view.MaxX * w - x),
            Normalize(y - view.MinY * w), Normalize(view.MaxY * w - y), Normalize(z), Normalize(w - z));
    }

    public bool Intersects(in BoundingSphere sphere) => Inside(Left, sphere) && Inside(Right, sphere) &&
        Inside(Bottom, sphere) && Inside(Top, sphere) && Inside(Near, sphere) && Inside(Far, sphere);

    private static Vector4 Normalize(Vector4 plane)
    {
        var length = new Vector3(plane.X, plane.Y, plane.Z).Length();
        return length > 0f ? plane / length : Vector4.Zero;
    }

    private static bool Inside(Vector4 plane, in BoundingSphere sphere) =>
        (double)plane.X * sphere.Center.X + (double)plane.Z * sphere.Center.Z +
        (double)plane.Y * sphere.Center.Y + plane.W >= -(double)sphere.Radius; // 0x983D20, inclusive.
}
