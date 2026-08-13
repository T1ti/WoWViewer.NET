using WoWRenderLib.Cache;

namespace WoWRenderLib.DX11.Cache;

public static class Dx11CacheLifecycle
{
    public static async Task ResetAsync()
    {
        await Task.WhenAll(
            ADTCache.StopWorkerAsync(),
            M2Cache.StopWorkerAsync(),
            WMOCache.StopWorkerAsync(),
            BLPCache.StopWorkerAsync());

        // GPU-backed cache resources are released only after all CPU workers have
        // stopped, so no obsolete upload can target a recreated D3D device.
        ADTCache.ReleaseAll();
        WMOCache.ReleaseAll();
        M2Cache.ReleaseAll();
        BLPCache.ReleaseAll();
        TEXCache.ReleaseAll();
        WDTCache.ReleaseAll();
    }
}
