using System.Diagnostics;

namespace WoWRenderLib.Diagnostics;

/// <summary>
/// Writes load failures to both the process console and the configured trace
/// listeners.  UI status messages are useful to users, but must not be the only
/// place where the exception and its stack are retained.
/// </summary>
public static class LoadDiagnostics
{
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
