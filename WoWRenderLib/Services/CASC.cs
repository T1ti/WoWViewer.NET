using TACTSharp;

namespace WoWRenderLib.Services
{
    public sealed record CASCInitializationResult(
        BuildInstance BuildInstance,
        string BuildName,
        string LocalStoragePath,
        string Product);

    public static class CASC
    {
        public static BuildInstance buildInstance = null!;
        public static bool IsInitialized { get; private set; } = false;
        public static string BuildName { get; private set; } = "";
        private static readonly object LocalStorageLock = new();
        private static CASCLib.CASCHandler? localStorage;
        private static string localStoragePath = "";
        private static string localStorageProduct = "";

        public static async Task Initialize(string wowProduct, string wowDir = "", string buildConfig = "", string cdnConfig = "")
        {
            var result = await CreateBuildAsync(wowProduct, wowDir, buildConfig, cdnConfig);
            Activate(result);
        }

        public static async Task<CASCInitializationResult> CreateBuildAsync(
            string wowProduct,
            string wowDir = "",
            string buildConfig = "",
            string cdnConfig = "",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(wowProduct) || !wowProduct.StartsWith("wow"))
                throw new Exception("Invalid WoW product");

            cancellationToken.ThrowIfCancellationRequested();

            var candidate = new BuildInstance();
            candidate.Settings.Product = wowProduct;

            candidate.Settings.Locale = RootInstance.LocaleFlags.enUS;
            candidate.Settings.Region = "us";
            candidate.Settings.RootMode = RootInstance.LoadMode.Normal;

            if (string.IsNullOrEmpty(buildConfig) || string.IsNullOrEmpty(cdnConfig))
            {
                var versions = await candidate.cdn.GetPatchServiceFile(wowProduct, "versions");
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var line in versions.Split("\n"))
                {
                    var splitLine = line.Split('|');

                    if (splitLine.Length < 3)
                        continue;

                    if (splitLine[0].StartsWith("Region") || splitLine[0].StartsWith("##"))
                        continue;

                    if (splitLine[0] != candidate.Settings.Region)
                        continue;

                    buildConfig = splitLine[1];
                    cdnConfig = splitLine[2];
                }

                if (string.IsNullOrEmpty(buildConfig) || string.IsNullOrEmpty(cdnConfig))
                {
                    foreach (var line in versions.Split("\n"))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        var splitLine = line.Split('|');

                        if (splitLine.Length < 3)
                            continue;

                        if (splitLine[0].StartsWith("Region") || splitLine[0].StartsWith("##"))
                            continue;

                        buildConfig = splitLine[1];
                        cdnConfig = splitLine[2];
                    }
                }
            }

            if (string.IsNullOrEmpty(buildConfig) || string.IsNullOrEmpty(cdnConfig))
                throw new Exception("No configs specified and was unable to retrieve version information from Ribbit");

            if (!string.IsNullOrEmpty(wowDir) && Directory.Exists(wowDir))
                candidate.Settings.BaseDir = wowDir;

            candidate.Settings.BuildConfig = buildConfig;
            candidate.Settings.CDNConfig = cdnConfig;

            candidate.Settings.AdditionalCDNs = ["archive.wow.tools", "casc.wago.tools", "cdn.arctium.tools"];
            candidate.Settings.BlockedCDNs = ["level3.blizzard.com", "us.cdn.blizzard.com"];

            candidate.LoadConfigs(buildConfig, cdnConfig);
            if (candidate.BuildConfig == null || candidate.CDNConfig == null)
                throw new Exception("Failed to load build configs");

            cancellationToken.ThrowIfCancellationRequested();
            LoadKeys();

            cancellationToken.ThrowIfCancellationRequested();
            candidate.Load();

            if (candidate.Encoding == null || candidate.Root == null || candidate.Install == null || candidate.GroupIndex == null)
                throw new Exception("Failed to load build components");

            cancellationToken.ThrowIfCancellationRequested();

            var fullBuildName = candidate.BuildConfig.Values["build-name"][0];
            var splitName = fullBuildName.Replace("WOW-", "").Split("patch");
            var buildName = splitName[1].Split("_")[0] + "." + splitName[0];

            return new CASCInitializationResult(candidate, buildName, wowDir, wowProduct);
        }

        public static void Activate(CASCInitializationResult result)
        {
            IsInitialized = false;
            buildInstance = result.BuildInstance;
            BuildName = result.BuildName;
            lock (LocalStorageLock)
            {
                localStorage?.Clear();
                localStoragePath = result.LocalStoragePath;
                localStorageProduct = result.Product;
                localStorage = Directory.Exists(localStoragePath)
                    ? CASCLib.CASCHandler.OpenLocalStorage(localStoragePath, localStorageProduct)
                    : null;
            }
            IsInitialized = true;
        }

        public static bool FileExists(uint fileDataID)
        {
            return buildInstance!.Root!.FileExists(fileDataID);
        }

        /// <summary>
        /// Reads an installed payload through the CASCExplorer-compatible local
        /// storage implementation. Local mode is prevented from falling back to
        /// its online reader in CASCHandlerBase.
        /// </summary>
        public static bool TryReadLocalFile(uint fileDataId, out byte[] bytes, out string failureReason)
        {
            bytes = [];
            failureReason = string.Empty;

            if (!IsInitialized)
            {
                failureReason = "the active CASC build is not initialized";
                return false;
            }
            if (string.IsNullOrWhiteSpace(localStoragePath) || !Directory.Exists(localStoragePath))
            {
                failureReason = "the selected client installation path is unavailable";
                return false;
            }

            lock (LocalStorageLock)
            {
                try
                {
                    localStorage ??= CASCLib.CASCHandler.OpenLocalStorage(localStoragePath, localStorageProduct);
                    if (!localStorage.FileExists(checked((int)fileDataId)))
                    {
                        failureReason = "the FileDataID is absent from the selected client's local CASC root";
                        return false;
                    }

                    using var stream = localStorage.OpenFile(checked((int)fileDataId));
                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    bytes = memory.ToArray();
                    return true;
                }
                catch (Exception exception)
                {
                    failureReason = $"the local CASC storage could not read the payload: {exception.Message}";
                    return false;
                }
            }
        }

        public static bool LoadKeys(bool forceRedownload = false)
        {
            var download = forceRedownload;
            if (File.Exists("WoW.txt"))
            {
                var info = new FileInfo("WoW.txt");
                if (info.Length == 0 || DateTime.Now.Subtract(TimeSpan.FromHours(12)) > info.LastWriteTime)
                {
                    Console.WriteLine("TACT Keys outdated, redownloading..");
                    download = true;
                }
            }
            else
            {
                download = true;
            }

            if (download)
            {
                Console.WriteLine("Downloading TACT keys");

                using (var WebClient = new HttpClient())
                using (var s = WebClient.GetStreamAsync("https://raw.githubusercontent.com/wowdev/TACTKeys/refs/heads/master/WoW.txt?=v" + (long)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds).Result)
                using (var fs = new FileStream("WoW.txt", FileMode.Create))
                {
                    s.CopyTo(fs);
                }
            }

            foreach (var line in File.ReadAllLines("WoW.txt"))
            {
                var splitLine = line.Split(' ');
                var lookup = ulong.Parse(splitLine[0], System.Globalization.NumberStyles.HexNumber);
                byte[] key = Convert.FromHexString(splitLine[1].Trim());

                TACTSharp.KeyService.SetKey(lookup, key);
            }

            return true;
        }
    }
}
