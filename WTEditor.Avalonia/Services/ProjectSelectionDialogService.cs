using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using WTEditor.Application.Services;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Views;

namespace WTEditor.Avalonia.Services;

public interface IProjectSelectionDialogService
{
    Task ShowAsync();
}

public sealed class ProjectSelectionDialogService(
    IProjectService projectService,
    IFileExplorerService fileExplorerService) : IProjectSelectionDialogService
{
    public async Task ShowAsync()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow == null)
        {
            return;
        }

        var window = new ProjectSelectionWindow(
            new ProjectSelectionViewModel(projectService, fileExplorerService));
        EventHandler onProjectOpened = (_, _) => window.Close(true);
        window.ProjectOpened += onProjectOpened;
        try
        {
            await window.ShowDialog<bool>(desktop.MainWindow);
        }
        catch (Exception exception)
        {
            await new ErrorMessageWindow(
                "Unable to open project selection",
                exception.Message).ShowDialog<bool>(desktop.MainWindow);
        }
        finally
        {
            window.ProjectOpened -= onProjectOpened;
        }
    }
}
