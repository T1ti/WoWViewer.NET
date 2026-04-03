using TACTSharp;

namespace WoWViewer.NET.Services
{
    public static class CASC
    {
        public static BuildInstance buildInstance;
        public static bool IsInitialized { get; private set; } = false;

        public static async Task Initialize()
        {
            buildInstance = new BuildInstance();
            buildInstance.Settings.Product = "wowt";

            buildInstance.Settings.Locale = RootInstance.LocaleFlags.enUS;
            buildInstance.Settings.Region = "us";
            buildInstance.Settings.RootMode = RootInstance.LoadMode.Normal;

            var buildConfig = "f8891ed6ab01b319b43b9aa0ceeb5f58";
            var cdnConfig = "99cd3ee53f0b63144232eef9ff25fc06";

            var basedir = @"C:\World of Warcraft";
            if (Directory.Exists(basedir))
            {
                buildInstance.Settings.BaseDir = basedir;
                buildInstance.Settings.BuildConfig = buildConfig;
                buildInstance.Settings.CDNConfig = cdnConfig;
            }
            buildInstance.Settings.AdditionalCDNs = ["casc.wago.tools", "cdn.arctium.tools"];
            buildInstance.LoadConfigs(buildConfig, cdnConfig);
            if (buildInstance.BuildConfig == null || buildInstance.CDNConfig == null)
                throw new Exception("Failed to load build configs");

            buildInstance.Load();

            if (buildInstance.Encoding == null || buildInstance.Root == null || buildInstance.Install == null || buildInstance.GroupIndex == null)
                throw new Exception("Failed to load build components");

            IsInitialized = true;
        }
    }
}
