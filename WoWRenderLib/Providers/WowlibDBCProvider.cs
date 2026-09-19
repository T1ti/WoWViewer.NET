using DBCD.Providers;
using Fs = WoWLib.Filesystem;
using WoWRenderLib.Database;

namespace WoWRenderLib.Providers;

/// <summary>
/// Supplies DBCD with DB2 payloads from the active WowLib filesystem. DBCD is
/// used by the world-lighting catalogue because it exposes build-specific
/// WoWDBDefs columns by their semantic names instead of binding a positional
/// managed row layout.
/// </summary>
internal sealed class WowlibDBCProvider(Fs.FileSystem fileSystem) : IDBCProvider
{
    public Stream StreamForTableName(string tableName, string build)
    {
        var bytes = Db2TableLoader.TryReadBytes(fileSystem, tableName, out var diagnostic);
        if (bytes == null)
            throw new FileNotFoundException(diagnostic, tableName);

        return new MemoryStream(bytes, writable: false);
    }
}
