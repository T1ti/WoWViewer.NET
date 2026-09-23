using WoWLib;
using WoWLib.Filesystem;
using WoWRenderLib.Services;

namespace WTEditor.Avalonia.Services;

public interface IObjectAssetCatalogService
{
    Task<ObjectAssetIndex> GetIndexAsync(CancellationToken cancellationToken = default);
}

public sealed class ObjectAssetEntry(string name, string path, uint? fileDataId, string kind, ObjectAssetFolder folder)
{
    public string Name { get; } = name;
    public string Path { get; } = path;
    public uint? FileDataId { get; } = fileDataId;
    public string Kind { get; } = kind;
    public ObjectAssetFolder Folder { get; } = folder;
}

public sealed class ObjectAssetFolder(string name, string path, ObjectAssetFolder? parent = null)
{
    public string Name { get; } = name;
    public string Path { get; } = path;
    public ObjectAssetFolder? Parent { get; } = parent;
    public List<ObjectAssetFolder> Children { get; } = [];
    public List<ObjectAssetEntry> Files { get; } = [];
}

public sealed class ObjectAssetFilterResult(
    HashSet<ObjectAssetFolder>? visibleFolders,
    Dictionary<ObjectAssetFolder, List<ObjectAssetEntry>>? filesByFolder)
{
    public bool Includes(ObjectAssetFolder folder) =>
        visibleFolders == null || visibleFolders.Contains(folder);

    public IReadOnlyList<ObjectAssetEntry> FilesIn(ObjectAssetFolder folder) =>
        filesByFolder == null
            ? folder.Files
            : filesByFolder.TryGetValue(folder, out var files) ? files : [];
}

/// <summary>Compact model-only index built once per active client.</summary>
public sealed class ObjectAssetIndex
{
    private readonly IReadOnlyList<ObjectAssetEntry> _files;
    public ObjectAssetFolder Root { get; }

    private ObjectAssetIndex(ObjectAssetFolder root, IReadOnlyList<ObjectAssetEntry> files)
    {
        Root = root;
        _files = files;
    }

    public static ObjectAssetIndex Build(
        IEnumerable<ClientFileCatalogEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var root = new ObjectAssetFolder("Objects", string.Empty);
        var folders = new Dictionary<string, ObjectAssetFolder>(StringComparer.OrdinalIgnoreCase);
        var files = new List<ObjectAssetEntry>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = entry.Path.Replace('\\', '/').Trim('/');
            var kind = GetKind(path);
            if (kind == null)
                continue;

            var parent = root;
            var slash = 0;
            while ((slash = path.IndexOf('/', slash)) >= 0)
            {
                var folderPath = path[..slash];
                if (!folders.TryGetValue(folderPath, out var folder))
                {
                    var previousSlash = folderPath.LastIndexOf('/');
                    folder = new ObjectAssetFolder(folderPath[(previousSlash + 1)..], folderPath, parent);
                    folders.Add(folderPath, folder);
                    parent.Children.Add(folder);
                }
                parent = folder;
                slash++;
            }

            var name = path[(path.LastIndexOf('/') + 1)..];
            var file = new ObjectAssetEntry(name, path, entry.FileDataId, kind, parent);
            parent.Files.Add(file);
            files.Add(file);
        }

        foreach (var folder in folders.Values.Append(root))
        {
            folder.Children.Sort((left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
            folder.Files.Sort((left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        }

        return new ObjectAssetIndex(root, files);
    }

    public ObjectAssetFilterResult Filter(
        string searchText,
        bool showWmo,
        bool showM2,
        CancellationToken cancellationToken = default)
    {
        var words = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0 && showWmo && showM2)
            return new ObjectAssetFilterResult(null, null);

        var visibleFolders = new HashSet<ObjectAssetFolder> { Root };
        var filesByFolder = new Dictionary<ObjectAssetFolder, List<ObjectAssetEntry>>();
        foreach (var file in _files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!(file.Kind == "WMO" ? showWmo : showM2) ||
                !words.All(word => file.Path.Contains(word, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (!filesByFolder.TryGetValue(file.Folder, out var matches))
                filesByFolder.Add(file.Folder, matches = []);
            matches.Add(file);
            for (var folder = file.Folder; folder != null; folder = folder.Parent)
                visibleFolders.Add(folder);
        }

        return new ObjectAssetFilterResult(visibleFolders, filesByFolder);
    }

    public static string? GetKind(string path)
    {
        if (path.EndsWith(".m2", StringComparison.OrdinalIgnoreCase))
            return "M2";
        if (!path.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase))
            return null;
        // WMO group files are resources of their root WMO, not browser objects.
        if (path.Length >= 8 && path[^8] == '_' &&
            char.IsAsciiDigit(path[^7]) && char.IsAsciiDigit(path[^6]) && char.IsAsciiDigit(path[^5]))
            return null;
        return "WMO";
    }
}

/// <summary>Enumerates the large client listfile in the background, resolving IDs only for models.</summary>
public sealed class ObjectAssetCatalogService : IObjectAssetCatalogService
{
    private readonly object _gate = new();
    private FileSystem? _fileSystem;
    private Task<ObjectAssetIndex>? _loadTask;

    public async Task<ObjectAssetIndex> GetIndexAsync(CancellationToken cancellationToken = default)
    {
        var fileSystem = WowlibFileSystem.Current;
        Task<ObjectAssetIndex> task;
        lock (_gate)
        {
            if (!ReferenceEquals(_fileSystem, fileSystem) || _loadTask == null)
            {
                _fileSystem = fileSystem;
                _loadTask = Task.Run(() => BuildIndex(fileSystem));
            }
            task = _loadTask;
        }

        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (task.IsFaulted)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_loadTask, task))
                    _loadTask = null;
            }
            throw;
        }
    }

    private static ObjectAssetIndex BuildIndex(FileSystem fileSystem)
    {
        IEnumerable<ClientFileCatalogEntry> Models()
        {
            foreach (var rawPath in fileSystem.EnumeratePaths())
            {
                if (ObjectAssetIndex.GetKind(rawPath) == null)
                    continue;
                var path = rawPath.Replace('\\', '/');
                var id = WowlibFileSystem.ResolveAssetId(fileSystem, path);
                yield return new ClientFileCatalogEntry(path, id == 0 ? null : id);
            }
        }

        return ObjectAssetIndex.Build(Models());
    }
}
