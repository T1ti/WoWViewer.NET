using WoWLib;
using Fs = WoWLib.Filesystem;

namespace WoWRenderLib.Services;

/// <summary>
/// Owns the wowlib file system used by the renderer.  wowlib's detected
/// ClientVersion is intentionally kept with the file system so every format
/// loader uses the same version lineage (including modern Classic clients).
/// </summary>
public static class WowlibFileSystem
{
    private static readonly object Sync = new();
    private static Fs.FileSystem? current;

    public static Fs.FileSystem Current
    {
        get
        {
            lock (Sync)
                return current ?? throw new InvalidOperationException("The wowlib file system has not been opened.");
        }
    }

    public static ClientVersion Version => Current.Version;

    public static Fs.FileSystem OpenForClient(string clientPath, string cascProduct)
    {
        var installPath = ResolveInstallPath(clientPath, cascProduct);
        var projectDirectory = GetProjectDirectory(cascProduct);
        var listfilePath = GetListfilePath();
        using var settings = Fs.FileSystemSettings.Detect(
            installPath,
            Locale.enUS,
            projectDirectory,
            listfilePath,
            new FileDataId());
        var next = Fs.FileSystem.Open(settings);

        lock (Sync)
        {
            current?.Dispose();
            current = next;
            return current;
        }
    }

    public static void Close()
    {
        lock (Sync)
        {
            current?.Dispose();
            current = null;
        }
    }

    private static string ResolveInstallPath(string clientPath, string cascProduct)
    {
        if (string.IsNullOrWhiteSpace(clientPath))
            throw new ArgumentException("A WoW client path is required.", nameof(clientPath));

        var candidates = new List<string>();
        AddCandidate(candidates, clientPath);

        var parent = Directory.GetParent(clientPath)?.FullName;
        if (parent != null)
            AddCandidate(candidates, parent);

        foreach (var name in new[] { "_retail_", "_classic_era_", "_classic_", "_classic_anniversary_" })
            AddCandidate(candidates, Path.Combine(clientPath, name));

        if (Directory.Exists(clientPath))
        {
            foreach (var child in Directory.EnumerateDirectories(clientPath))
                AddCandidate(candidates, child);
        }

        foreach (var candidate in candidates)
        {
            try
            {
                using var install = Fs.ClientInstall.Detect(candidate);
                if (string.IsNullOrWhiteSpace(cascProduct) ||
                    string.Equals(install.CascProduct, cascProduct, StringComparison.OrdinalIgnoreCase))
                    return install.Path;
            }
            catch
            {
                // A multi-product root or unrelated child is expected while
                // probing a Battle.net installation.
            }
        }

        throw new InvalidOperationException(
            $"Could not detect a wowlib client installation for '{cascProduct}' below '{clientPath}'.");
    }

    internal static string GetProjectDirectory(string cascProduct)
    {
        var product = string.IsNullOrWhiteSpace(cascProduct) ? "client" : cascProduct;
        var path = Path.Combine(
            Path.GetTempPath(),
            "WoWViewer.NET",
            "wowlib-overlay",
            SanitizePathPart(product));
        Directory.CreateDirectory(path);
        return path;
    }

    internal static string GetListfilePath()
    {
        if (File.Exists(Listfile.DefaultPath))
            return Listfile.DefaultPath;

        var fallbackDirectory = Path.Combine(Path.GetTempPath(), "WoWViewer.NET");
        Directory.CreateDirectory(fallbackDirectory);
        var fallbackPath = Path.Combine(fallbackDirectory, "empty-listfile.csv");
        if (!File.Exists(fallbackPath))
            File.WriteAllText(fallbackPath, string.Empty);
        return fallbackPath;
    }

    private static string SanitizePathPart(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(Array.IndexOf(invalidCharacters, character) >= 0 ? '_' : character);
        return builder.Length == 0 ? "client" : builder.ToString();
    }

    private static void AddCandidate(List<string> candidates, string path)
    {
        if (Directory.Exists(path) && !candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
            candidates.Add(path);
    }
}
