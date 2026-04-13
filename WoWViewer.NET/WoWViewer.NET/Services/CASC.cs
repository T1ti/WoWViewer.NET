using TACTSharp;

namespace WoWViewer.NET.Services
{
    public static class CASC
    {
        public static BuildInstance buildInstance;
        public static bool IsInitialized { get; private set; } = false;
        public static string BuildName { get; private set; } = "";

        public static async Task Initialize(string wowDir, string wowProduct, string buildConfig, string cdnConfig)
        {
            if(string.IsNullOrEmpty(wowDir) || !Directory.Exists(wowDir))
                throw new Exception("Invalid WoW directory");

            if(string.IsNullOrEmpty(wowProduct) || !wowProduct.StartsWith("wow"))
                throw new Exception("Invalid WoW product");

            buildInstance = new BuildInstance();
            buildInstance.Settings.Product = wowProduct;

            buildInstance.Settings.Locale = RootInstance.LocaleFlags.enUS;
            buildInstance.Settings.Region = "us";
            buildInstance.Settings.RootMode = RootInstance.LoadMode.Normal;

            if(string.IsNullOrEmpty(buildConfig) || string.IsNullOrEmpty(cdnConfig))
            {
                // TODO: Retrieve from Ribbit
            }

            if (!string.IsNullOrEmpty(wowDir) && Directory.Exists(wowDir))
            {
                buildInstance.Settings.BaseDir = wowDir;
                buildInstance.Settings.BuildConfig = buildConfig;
                buildInstance.Settings.CDNConfig = cdnConfig;
            }

            buildInstance.Settings.AdditionalCDNs = ["archive.wow.tools", "casc.wago.tools", "cdn.arctium.tools"];
            buildInstance.Settings.BlockedCDNs = ["level3.blizzard.com", "us.cdn.blizzard.com"];

            buildInstance.LoadConfigs(buildConfig, cdnConfig);
            if (buildInstance.BuildConfig == null || buildInstance.CDNConfig == null)
                throw new Exception("Failed to load build configs");

            LoadKeys();

            buildInstance.Load();

            if (buildInstance.Encoding == null || buildInstance.Root == null || buildInstance.Install == null || buildInstance.GroupIndex == null)
                throw new Exception("Failed to load build components");

            var fullBuildName = buildInstance.BuildConfig.Values["build-name"][0];
            var splitName = fullBuildName.Replace("WOW-", "").Split("patch");
            BuildName = splitName[1].Split("_")[0] + "." + splitName[0];
            IsInitialized = true;
        }

        public static bool FileExists(uint fileDataID)
        {
            return buildInstance!.Root!.FileExists(fileDataID);
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
