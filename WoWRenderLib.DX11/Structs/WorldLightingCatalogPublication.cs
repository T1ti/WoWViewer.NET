using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Structs;

/// <summary>
/// Publishes the asynchronously loaded lighting catalog to the render thread
/// and retains a one-shot refresh request until that thread consumes it.
/// </summary>
internal sealed class WorldLightingCatalogPublication
{
    private WorldLightingCatalog? _catalog;
    private int _refreshPending;

    public WorldLightingCatalog? Current => Volatile.Read(ref _catalog);

    public void Publish(WorldLightingCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Volatile.Write(ref _catalog, catalog);
        Interlocked.Exchange(ref _refreshPending, 1);
    }

    public bool ConsumeRefreshRequest() =>
        Interlocked.Exchange(ref _refreshPending, 0) != 0;
}
