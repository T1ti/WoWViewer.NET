using WoWFormatLib.FileReaders;
using WoWFormatLib.Structs.WDT;

namespace WoWViewer.NET.Cache
{
    public static class WDTCache
    {
        private static Dictionary<uint, WDT> Cache = [];

        public static WDT GetOrLoad(uint fileDataID)
        {
            if (Cache.TryGetValue(fileDataID, out WDT value))
                return value;

            var wdtReader = new WDTReader();
            wdtReader.LoadWDT(fileDataID);
            Cache.Add(fileDataID, wdtReader.wdtfile);

            return Cache[fileDataID];
        }

        public static void ReleaseWDT(uint fileDataID)
        {
            // TODO: Do we also want to automatically remove ADTs?
            Cache.Remove(fileDataID);
        }
    }
}
