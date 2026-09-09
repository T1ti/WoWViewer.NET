using WoWRenderLib.Services;
using WoWLib;

namespace WTEditor.Avalonia.Services;

public readonly record struct ClientFileCatalogEntry(string Path, uint? FileDataId);

public interface IClientFileCatalogService
{
    Task<IReadOnlyList<ClientFileCatalogEntry>> GetFilesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Caches the authoritative named-file inventory exposed by the active client filesystem.
/// Consumers can filter this snapshot without repeatedly probing storage with Exists.
/// </summary>
public sealed class ClientFileCatalogService : IClientFileCatalogService
{
    private readonly object _gate = new();
    private WoWLib.Filesystem.FileSystem? _fileSystem;
    private Task<IReadOnlyList<ClientFileCatalogEntry>>? _loadTask;

    public async Task<IReadOnlyList<ClientFileCatalogEntry>> GetFilesAsync(
        CancellationToken cancellationToken = default)
    {
        var fileSystem = WowlibFileSystem.Current;
        Task<IReadOnlyList<ClientFileCatalogEntry>> loadTask;
        lock (_gate)
        {
            if (!ReferenceEquals(_fileSystem, fileSystem) || _loadTask == null)
            {
                _fileSystem = fileSystem;
                _loadTask = Task.Run<IReadOnlyList<ClientFileCatalogEntry>>(
                    () => BuildCatalog(fileSystem));
            }
            loadTask = _loadTask;
        }

        try
        {
            return await loadTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (loadTask.IsFaulted)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_loadTask, loadTask))
                    _loadTask = null;
            }
            throw;
        }
    }

    private static IReadOnlyList<ClientFileCatalogEntry> BuildCatalog(
        WoWLib.Filesystem.FileSystem fileSystem) =>
        fileSystem.EnumeratePaths()
            .Select(path =>
            {
                var normalized = path.Replace('\\', '/');
                var fileDataId = fileSystem.Resolve(new FileKey(normalized)).Fdid;
                return new ClientFileCatalogEntry(
                    normalized,
                    fileDataId?.Value);
            })
            .ToArray();
}
