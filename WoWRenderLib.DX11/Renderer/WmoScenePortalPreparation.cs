using System.Numerics;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>Prepares the primary viewer's masks and global view unions before sky submission.</summary>
internal sealed class WmoScenePortalPreparation
{
    public Wrath335PortalSceneViews Views { get; } = new();
    public bool UsesWrath335Rules { get; private set; }
    private WMOContainer? _preparedPlacement;
    private bool _preparedVisibility;
    private int _traversedReferences;

    public void Prepare(in WmoSceneViewerResult viewer, bool portalCulling,
        Vector3 eyeWorld, Vector3 cameraForwardWorld, in Matrix4x4 viewProjection)
    {
        _preparedPlacement = null;
        _preparedVisibility = false;
        _traversedReferences = 0;
        UsesWrath335Rules = portalCulling && viewer.UsesWrath335Rules;
        var primary = viewer.Primary;
        if (!UsesWrath335Rules || primary.Instance == null)
        {
            Views.Reset(false);
            return;
        }
        var model = primary.Model;
        var flags = RootFlags(model, primary.PrimaryGroupIndex) |
                    RootFlags(model, primary.SecondaryGroupIndex);
        Views.Reset(true, flags, viewer.Secondary.Instance != null);
        primary.Instance.GetPortalVisibilityBuffers(model,
            out var groups, out var doodads, out var batches, out var scratch);
        _preparedPlacement = primary.Instance;
        _preparedVisibility = WmoPortalVisibility.TryComputeViewerScene(model,
            primary.Instance.GetModelMatrix(), viewProjection, eyeWorld, cameraForwardWorld,
            primary.Instance.EnabledGroups, groups, doodads, batches, scratch,
            out _traversedReferences,
            new(primary.PrimaryGroupIndex, primary.SecondaryGroupIndex), Views);
        if (!_preparedVisibility)
            Views.Reset(false); // Invalid/incomplete graphs retain the ordinary visible scene.
    }

    public bool TryGetPreparedVisibility(WMOContainer instance, out bool applied,
        out int traversedReferences)
    {
        applied = _preparedVisibility;
        traversedReferences = _traversedReferences;
        return ReferenceEquals(instance, _preparedPlacement);
    }

    private static uint RootFlags(in WorldModel model, int index) =>
        model.groupBatches != null && (uint)index < (uint)model.groupBatches.Length
            ? model.groupBatches[index].mogiFlags : 0;
}
