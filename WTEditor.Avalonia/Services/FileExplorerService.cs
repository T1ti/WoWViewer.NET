using System.Diagnostics;

namespace WTEditor.Avalonia.Services;

public interface IFileExplorerService
{
    void OpenDirectory(string path);
}

public sealed class FileExplorerService : IFileExplorerService
{
    public void OpenDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A directory path is required.", nameof(path));

        var directory = Path.GetFullPath(path.Trim());
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory not found: {directory}");

        Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        });
    }
}
