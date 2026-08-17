using CommunityToolkit.Mvvm.Input;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Services;

namespace WTEditor.Avalonia.ViewModels;

public sealed partial class ProjectEntryViewModel : ViewModelBase
{
    private readonly IFileExplorerService _fileExplorerService;

    public ProjectDefinition Project { get; }
    public string Name => Project.Name;
    public string FolderPath => Project.FolderPath;
    public string ClientFolderPath { get; }
    public string ProductType { get; }
    public string BuildVersion { get; }

    public ProjectEntryViewModel(
        ProjectDefinition project,
        IProjectService projectService,
        IFileExplorerService fileExplorerService)
    {
        Project = project;
        var settings = projectService.LoadSettings(project);
        ClientFolderPath = settings.Client.WowDirectory;
        var buildInfo = projectService.GetClientBuildInfo(project);
        ProductType = buildInfo.Product;
        BuildVersion = buildInfo.Version;
        _fileExplorerService = fileExplorerService;
    }

    [RelayCommand]
    private void OpenProjectDirectory() => _fileExplorerService.OpenDirectory(FolderPath);

    [RelayCommand]
    private void OpenWowClientDirectory() => _fileExplorerService.OpenDirectory(ClientFolderPath);
}
