using System.Numerics;
using System.Runtime.InteropServices;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>Prepares 12340 scene masks and global portal lists independently of GPU asset buckets.</summary>
internal sealed class WmoScenePortalPreparation
{
    public Wrath335PortalSceneViews Views { get; } = new();
    public bool UsesWrath335Rules { get; private set; }
    private readonly List<Placement> placements = [];
    private readonly Dictionary<WMOContainer, int> placementIndices = [];
    private readonly List<BoundingBox> visibleBounds = [];
    private readonly Wrath335SceneExteriorGroups exteriorGroups = new();
    private readonly Wrath335ExteriorDoodads exteriorDoodads = new();
    private readonly Wrath335PortalPlacementCache cache = new();
    internal Wrath335ClipVolumes ClipVolumes { get; } = new();
    internal Wrath335SceneTerrainOcclusion TerrainOcclusion { get; } = new();
    private int activeCount;

    public void Prepare(in WmoSceneViewerResult viewer, bool portalCulling,
        Vector3 eyeWorld, Vector3 cameraForwardWorld, in Matrix4x4 viewProjection,
        IReadOnlyList<Container3D> sceneObjects, int mapId = -1,
        Matrix4x4? cameraProjection = null, float farClip = 1000f, Wrath335DoodadFade? doodadFade = null)
    {
        for (var index = 0; index < activeCount; index++)
            placements[index].Release();
        activeCount = 0;
        placementIndices.Clear();
        visibleBounds.Clear();
        cache.Reset();
        ClipVolumes.Clear(); // 0x79A96F: interior passes precede static volume construction.
        TerrainOcclusion.Clear();
        exteriorDoodads.Clear();
        Views.Reset(false);
        UsesWrath335Rules = portalCulling && viewer.UsesWrath335Rules;
        if (!UsesWrath335Rules)
            return;

        exteriorGroups.Begin(eyeWorld, cameraForwardWorld, viewProjection);
        // 0x7B6110 visits the placement list, then ascending root group links.
        for (var index = 0; index < sceneObjects.Count; index++)
        {
            if (sceneObjects[index] is not WMOContainer instance || placementIndices.ContainsKey(instance))
                continue;
            var model = ReferenceEquals(instance, viewer.Primary.Instance) ? viewer.Primary.Model :
                ReferenceEquals(instance, viewer.Secondary.Instance) ? viewer.Secondary.Model : instance.GetWMO();
            if (!model.wrath335 || !model.legacyLighting || model.rootWMOFileDataID != instance.FileDataId ||
                model.groupBatches is not { Length: > 0 })
                continue;
            if (activeCount == placements.Count)
                placements.Add(new());
            var placementIndex = activeCount++;
            var placement = placements[placementIndex];
            placementIndices.Add(instance, placementIndex);
            placement.Model = model;
            placement.Primary = ReferenceEquals(instance, viewer.Primary.Instance);
            placement.Enabled = instance.EnabledGroups;
            instance.GetPortalVisibilityBuffers(model, out placement.Groups, out placement.Doodads,
                out placement.Batches, out var scratch);
            placement.Scratch = scratch;
            placement.Applied = WmoPortalVisibility.TryBeginWrath335Scene(model,
                instance.GetModelMatrix(), viewProjection, eyeWorld, cameraForwardWorld,
                placement.Enabled, placement.Groups, placement.Batches, scratch, visibleBounds, ClipVolumes);
            if (placement.Applied)
            {
                scratch.DoodadVisibility.ConfigureFade(doodadFade, eyeWorld, cameraForwardWorld);
                foreach (var doodad in instance.ActiveDoodads)
                    if (doodad.GetBoundingSphere() is { } sphere)
                        scratch.DoodadVisibility.SetExteriorSphere(doodad.WmoDoodadIndex, sphere, doodad.GetBoundingBox());
            }
            exteriorGroups.AddPlacement(placementIndex, model.groupBatches, placement.Enabled,
                instance.GetModelMatrix(), instance.ViewerRuntimeFlags, placement.Applied);
        }

        // Secondary callbacks survive the list reset before the primary interior pass.
        VisitInterior(viewer.Secondary, null);
        var hasPrimary = viewer.Primary.Instance != null &&
            placementIndices.TryGetValue(viewer.Primary.Instance, out var primaryIndex) &&
            placements[primaryIndex].Applied;
        if (hasPrimary)
        {
            Views.Reset(true, RootFlags(viewer.Primary.Model, viewer.Primary.PrimaryGroupIndex) |
                RootFlags(viewer.Primary.Model, viewer.Primary.SecondaryGroupIndex),
                viewer.Secondary.Instance != null);
            VisitInterior(viewer.Primary, Views);
            Views.BuildComplement();
        }

        // 0x79A7A5 builds volumes only when the exterior bucket consumer runs.
        if (Views.HasExteriorView)
        {
            ClipVolumes.Prepare(mapId, eyeWorld, cameraForwardWorld, viewProjection, Views.ExteriorDistance);
            if (cameraProjection is { } projection)
                TerrainOcclusion.Begin(sceneObjects, eyeWorld, cameraForwardWorld, projection,
                    viewProjection, Views.ExteriorRect, Views.ExteriorDistance, hasPrimary, farClip, ClipVolumes);
        }
        exteriorGroups.Complete(hasPrimary, Views.HasExteriorView);
        exteriorDoodads.Begin(eyeWorld, cameraForwardWorld, viewProjection, Views.ExteriorRect);
        var seeds = exteriorGroups.Seeds;
        var seedIndex = 0;
        for (var bucket = 0; bucket < 64; bucket++)
        {
            TerrainOcclusion.BeginBucket(bucket);
            while (seedIndex < seeds.Length && seeds[seedIndex].Bucket == bucket)
                VisitExterior(seeds[seedIndex++]);
            // 0x79A830: definitions consume the earlier-band horizon before 0x79A836 updates it.
            exteriorDoodads.Consume(bucket, ClipVolumes, TerrainOcclusion.Buffer);
            TerrainOcclusion.EndBucket(bucket);
        }
        while (seedIndex < seeds.Length)
            VisitExterior(seeds[seedIndex++]);
        for (var index = 0; index < activeCount; index++)
        {
            var placement = placements[index];
            if (placement.Applied)
                WmoPortalVisibility.FinishWrath335Scene(placement.Model, placement.Groups, placement.Doodads,
                    placement.Scratch!, placement.Primary);
        }
    }

    private void VisitExterior(in Wrath335SceneExteriorSeed seed)
    {
        var unbucketed = seed.Bucket < 0;
        if (unbucketed && !Wrath335SceneExteriorGroups.AcceptUnbucketed(seed.Bounds,
            Views.HasExteriorView, CollectionsMarshal.AsSpan(visibleBounds)))
            return;
        var placement = placements[seed.PlacementIndex];
        // 0x79A221/0x7B3A76 use flags=1 at both terrain gates. Unbucketed a4=1
        // bypasses sphere and terrain tests, while keeping the full camera test.
        if (!unbucketed && (!WmoPortalVisibility.IntersectsRect(seed.Bounds,
                placement.Scratch!.SceneViewProjection, Views.ExteriorRect) ||
            ClipVolumes.ContainsSphere(Wrath335ClipVolumes.GroupSphere(
                placement.Model.groupBatches[seed.GroupIndex].mogiBoundingBox,
                placement.Scratch!.ModelToWorld)) || TerrainOcclusion.Buffer.ContainsBox(seed.Bounds, 1)))
            return;
        var scratch = placement.Scratch!;
        WmoPortalVisibility.VisitWrath335Exterior(placement.Model, seed.GroupIndex, seed.Bounds,
            unbucketed ? WmoPortalRect.Full : Views.ExteriorRect,
            placement.Enabled, placement.Groups, placement.Batches, scratch, cache,
            ref placement.References);
        Views.RenderViews.Append(scratch.ExteriorPortalViews.Forwarded);
        if (unbucketed)
            scratch.DoodadVisibility.ConsumeGroup(seed.GroupIndex, scratch.PropagatedGroups[seed.GroupIndex]);
        else
            // 0x79A242 enlists after the SOURCE gates, independently of callback visibility.
            exteriorDoodads.Enlist(scratch.DoodadVisibility,
                placement.Model.groupBatches[seed.GroupIndex].doodadReferences ?? [], seed.Bucket);
    }

    private void VisitInterior(in WmoViewerPlacement viewer, Wrath335PortalSceneViews? views)
    {
        if (viewer.Instance == null || !placementIndices.TryGetValue(viewer.Instance, out var index))
            return;
        var placement = placements[index];
        if (!placement.Applied)
            return;
        cache.Enter(placement.Scratch!);
        WmoPortalVisibility.VisitWrath335Interior(placement.Model,
            new(viewer.PrimaryGroupIndex, viewer.SecondaryGroupIndex), placement.Enabled,
            placement.Groups, placement.Batches, placement.Scratch!, views, ref placement.References);
    }

    public bool TryGetPreparedVisibility(WMOContainer instance, out bool applied,
        out int traversedReferences)
    {
        if (placementIndices.TryGetValue(instance, out var index))
        {
            applied = placements[index].Applied;
            traversedReferences = placements[index].References;
            return true;
        }
        applied = false;
        traversedReferences = 0;
        return false;
    }

    private static uint RootFlags(in WorldModel model, int index) =>
        model.groupBatches != null && (uint)index < (uint)model.groupBatches.Length
            ? model.groupBatches[index].mogiFlags : 0;

    // Reuse frame records; masks/projection buffers remain owned by each placement.
    private sealed class Placement
    {
        public WorldModel Model;
        public bool[] Enabled = [], Groups = [], Doodads = [], Batches = [];
        public WmoPortalVisibilityScratch? Scratch;
        public bool Applied;
        public bool Primary;
        public int References;
        public void Release()
        {
            if (Scratch != null)
            {
                Scratch.SceneVisibleBounds = null;
                Scratch.WrathProjection.ClipVolumes = null;
                Scratch.DoodadVisibility.Clear();
            }
            Model = default;
            Enabled = Groups = Doodads = Batches = [];
            Scratch = null;
            Applied = false;
            Primary = false;
            References = 0;
        }
    }
}
