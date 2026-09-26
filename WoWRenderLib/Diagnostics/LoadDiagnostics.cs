using System.Diagnostics;
using WoWRenderLib.Services;
using WoWLib;

namespace WoWRenderLib.Diagnostics;

/// <summary>
/// Writes load failures to both the process console and the configured trace
/// listeners.  UI status messages are useful to users, but must not be the only
/// place where the exception and its stack are retained.
/// </summary>
public static class LoadDiagnostics
{
    public static void Info(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var formatted = $"[INFO] {message}";
        Console.WriteLine(formatted);
        Trace.TraceInformation(formatted);
    }

    public static string Asset(string type, uint fileDataId)
    {
        var fileSystem = WowlibFileSystem.TryGetCurrent();
        if (fileSystem?.Kind == StorageKind.Mpq)
        {
            return LegacyAssetIds.TryGetPath(fileSystem, fileDataId, out var path)
                ? $"{type} '{path}'"
                : $"{type} (missing MPQ path registration)";
        }

        return fileSystem?.Kind == StorageKind.Casc && fileDataId != 0
            ? $"{type} FDID {fileDataId}"
            : $"{type} (unknown source)";
    }

    public static void Error(string operation, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(exception);

        var message = $"[ERROR] {operation}: {exception}";
        Console.Error.WriteLine(message);
        Trace.TraceError(message);
    }

    public static void Warning(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var formatted = $"[WARNING] {message}";
        Console.Error.WriteLine(formatted);
        Trace.TraceWarning(formatted);
    }
}
