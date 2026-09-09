using System.Collections.ObjectModel;

namespace WoWRenderLib;

public static class Listfile
{
    public const string DownloadUrl =
        "https://github.com/wowdev/wow-listfile/releases/latest/download/community-listfile-withcapitals.csv";
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "community-listfile.csv");

    private static readonly SemaphoreSlim LoadGate = new(1, 1);
    private static IReadOnlyDictionary<uint, string> _fileNames =
        new ReadOnlyDictionary<uint, string>(new Dictionary<uint, string>());
    private static IReadOnlyDictionary<string, uint> _fileDataIds =
        new ReadOnlyDictionary<string, uint>(new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase));

    public static bool IsLoaded { get; private set; }
    public static string? LastError { get; private set; }

    public static async Task EnsureLoadedAsync(
        string? path = null,
        HttpClient? client = null,
        CancellationToken cancellationToken = default)
    {
        path ??= DefaultPath;
        await LoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsLoaded)
                return;

            if (!File.Exists(path))
                await DownloadAsync(path, client, cancellationToken).ConfigureAwait(false);

            Load(path);
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            Console.WriteLine($"Listfile unavailable: {exception.Message}");
        }
        finally
        {
            LoadGate.Release();
        }
    }

    public static void Load(string? path = null)
    {
        path ??= DefaultPath;
        var fileNames = new Dictionary<uint, string>();
        var fileDataIds = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(path))
        {
            var separator = line.IndexOf(';');
            if (separator <= 0 || separator == line.Length - 1 ||
                !uint.TryParse(line.AsSpan(0, separator), out var fileDataId))
                continue;

            var fileName = line[(separator + 1)..].Trim();
            if (fileName.Length == 0)
                continue;

            fileNames[fileDataId] = fileName;
            fileDataIds.TryAdd(Normalize(fileName), fileDataId);
        }

        _fileNames = new ReadOnlyDictionary<uint, string>(fileNames);
        _fileDataIds = new ReadOnlyDictionary<string, uint>(fileDataIds);
        IsLoaded = true;
        LastError = null;
    }

    public static bool TryGetFileDataID(string filename, out uint fileDataId) =>
        _fileDataIds.TryGetValue(Normalize(filename), out fileDataId);

    public static bool TryGetFilename(uint fileDataId, out string filename) =>
        _fileNames.TryGetValue(fileDataId, out filename!);

    public static string GetDisplayName(uint fileDataId) =>
        TryGetFilename(fileDataId, out var filename) ? filename : $"FDID {fileDataId}";

    /// <summary>Returns the immutable file-name snapshot currently loaded from the listfile.</summary>
    public static IReadOnlyDictionary<uint, string> GetFiles() => _fileNames;

    private static async Task DownloadAsync(
        string path,
        HttpClient? suppliedClient,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var ownsClient = suppliedClient == null;
        var client = suppliedClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var temporaryPath = path + ".download";
        try
        {
            await using var source = await client.GetStreamAsync(DownloadUrl, cancellationToken).ConfigureAwait(false);
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            if (ownsClient)
                client.Dispose();
        }
    }

    private static string Normalize(string filename) =>
        filename.Trim().Replace('\\', '/');
}
