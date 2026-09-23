namespace WoWRenderLib.DX11.Structs;

internal sealed record WorldNavigationTarget(
    int MapId,
    uint WdtFileDataId,
    double TileX,
    double TileY,
    bool IsGlobalWmo);

/// <summary>
/// Retains the latest UI navigation request until the render thread can apply
/// it after client content initialization. Publishing another request replaces
/// the obsolete target instead of replaying intermediate map selections.
/// </summary>
internal sealed class WorldNavigationPublication
{
    private WorldNavigationTarget? _pending;

    public void Publish(WorldNavigationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Interlocked.Exchange(ref _pending, target);
    }

    public void PublishIfEmpty(WorldNavigationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Interlocked.CompareExchange(ref _pending, target, null);
    }

    public WorldNavigationTarget? ConsumeWhenReady(bool isReady) =>
        isReady ? Interlocked.Exchange(ref _pending, null) : null;
}
