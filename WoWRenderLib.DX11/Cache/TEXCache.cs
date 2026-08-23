namespace WoWRenderLib.DX11.Cache;

public static class TEXCache
{
    // wowlib currently supports BLP directly; standalone TEX blobs are not
    // exposed until that format is implemented there.
    public static void Preload(uint fileDataId) { }
    public static void ReleaseAll() { }
}
