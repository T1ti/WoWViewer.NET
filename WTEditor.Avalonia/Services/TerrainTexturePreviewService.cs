using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using WTEditor.Avalonia.Views;

namespace WTEditor.Avalonia.Services;

public interface ITerrainTexturePreviewService
{
    Task ShowAsync(uint fileDataId, string displayName);
}

public sealed class TerrainTexturePreviewService : ITerrainTexturePreviewService
{
    public async Task ShowAsync(uint fileDataId, string displayName)
    {
        TerrainTexturePreviewImages images;
        try
        {
            images = await Task.Run(() => TerrainTextureImageLoader.LoadPreview(fileDataId));
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Terrain texture preview {fileDataId}: {exception.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
                ShowError(displayName, fileDataId, exception.Message));
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => ShowPreview(displayName, fileDataId, images));
    }

    private static void ShowPreview(string displayName, uint fileDataId, TerrainTexturePreviewImages images)
    {
        try
        {
            var window = new TexturePreviewWindow(displayName, fileDataId, images);
            ShowOwned(window);
        }
        catch
        {
            images.Dispose();
            throw;
        }
    }

    private static void ShowError(string displayName, uint fileDataId, string error) =>
        ShowOwned(new ErrorMessageWindow(
            "Unable to preview texture",
            $"{displayName} (File data ID {fileDataId}) could not be decoded.\n\n{error}"));

    private static void ShowOwned(Window window)
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            window.Show(owner);
        else
            window.Show();
    }
}
