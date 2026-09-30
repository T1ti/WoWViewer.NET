namespace WoWRenderLib.DX11.Renderer;

internal readonly record struct WmoViewerSlot(
    int PlacementIndex, int PrimaryGroupIndex, int SecondaryGroupIndex, float MaximumDistance)
{
    public bool HasPlacement => PlacementIndex >= 0;
}

/// <summary>
/// Two running query pools verified at 0x7D59B0. Addresses identify the evidence;
/// pre-existing IDB names and field types are unverified hypotheses.
/// </summary>
internal struct Wrath335ViewerPlacementSelection
{
    internal const uint SkipQuery = 0x20;
    internal const uint UpdatedTransform = 0x400;
    private WmoViewerSlot _normal;
    private WmoViewerSlot _updated;

    public Wrath335ViewerPlacementSelection(float terrainDistance)
    {
        var distance = float.IsNaN(terrainDistance) ? WmoPortalVisibility.ClientViewerRayLength
            : Math.Clamp(terrainDistance, 0f, WmoPortalVisibility.ClientViewerRayLength);
        _normal = _updated = new(-1, -1, -1, distance);
    }

    public static bool CanQuery(uint runtimeFlags) => (runtimeFlags & SkipQuery) == 0;

    public readonly float GetMaximumDistance(uint runtimeFlags) =>
        (runtimeFlags & UpdatedTransform) == 0 ? _normal.MaximumDistance : _updated.MaximumDistance;

    /// <summary>
    /// Consume an accepted per-placement query, including its exterior result.
    /// Portal acceptance was already checked against this pool's running cap;
    /// it can move that cap slightly farther. A query with no hit must not call this.
    /// </summary>
    public void AcceptQueryHit(uint runtimeFlags, int placementIndex, float distance,
        int primaryGroupIndex, int secondaryGroupIndex)
    {
        if (!CanQuery(runtimeFlags) || !float.IsFinite(distance) || distance < 0f)
            return;
        var hit = new WmoViewerSlot(primaryGroupIndex >= 0 ? placementIndex : -1,
            primaryGroupIndex, secondaryGroupIndex, distance);
        if ((runtimeFlags & UpdatedTransform) == 0)
            _normal = hit;
        else
            _updated = hit;
    }

    public readonly void GetResults(out WmoViewerSlot primary, out WmoViewerSlot secondary)
    {
        primary = _normal.HasPlacement ? _normal : _updated;
        secondary = _normal.HasPlacement ? _updated
            : new(-1, -1, -1, _normal.MaximumDistance);
    }
}
