using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Services;

namespace WTEditor.Avalonia.ViewModels;

public partial class ProjectSelectionViewModel : ViewModelBase
{
    private readonly IProjectService _projectService;
    private readonly IFileExplorerService _fileExplorerService;

    public ObservableCollection<ProjectEntryViewModel> Projects { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteProjectCommand))]
    private ProjectEntryViewModel? _selectedProject;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _autoLoadLastProject;

    public event EventHandler<ProjectDefinition>? ProjectOpened;
    public event EventHandler? AddProjectRequested;
    public event EventHandler<string>? ErrorRaised;

    public ProjectSelectionViewModel(
        IProjectService projectService,
        IFileExplorerService fileExplorerService)
    {
        _projectService = projectService;
        _fileExplorerService = fileExplorerService;
        Projects = new ObservableCollection<ProjectEntryViewModel>(
            projectService.Projects.Select(project =>
                new ProjectEntryViewModel(project, projectService, fileExplorerService)));
        AutoLoadLastProject = projectService.AutoLoadLastProject;
        SelectedProject = Projects.FirstOrDefault(project => project.Project.Id == projectService.LastProjectId)
            ?? Projects.FirstOrDefault();
    }

    partial void OnAutoLoadLastProjectChanged(bool value) => _projectService.SetAutoLoadLastProject(value);

    [RelayCommand(CanExecute = nameof(CanOpenProject))]
    private void OpenProject()
        => OpenProject(SelectedProject);

    public void OpenProject(ProjectEntryViewModel? project)
    {
        if (project == null)
            return;

        try
        {
            SelectedProject = project;
            if (!_projectService.SelectProject(project.Project.Id))
            {
                ReportError("The selected project could not be found.");
                return;
            }

            ErrorMessage = null;
            ProjectOpened?.Invoke(this, project.Project);
        }
        catch (Exception exception)
        {
            ReportError(exception.Message);
        }
    }

    [RelayCommand]
    private void RequestAddProject() => AddProjectRequested?.Invoke(this, EventArgs.Empty);

    public (string Name, string Folder, string ClientFolder, string ProductType) GetNewProjectDefaults()
    {
        var suffix = 1;
        string name;
        do
        {
            name = $"project{suffix++}";
        }
        while (Projects.Any(project => string.Equals(project.Name, name, StringComparison.OrdinalIgnoreCase))
            || Directory.Exists(Path.Combine(AppContext.BaseDirectory, name)));

        return (
            name,
            Path.Combine(AppContext.BaseDirectory, name),
            new ClientConfiguration().WowDirectory,
            new ClientConfiguration().WowProduct);
    }

    public string? ValidateProject(
        string name,
        string folderPath,
        string clientFolderPath,
        string productType) =>
        _projectService.ValidateProject(name, folderPath, clientFolderPath, productType);

    [RelayCommand(CanExecute = nameof(CanDeleteProject))]
    private void DeleteProject()
    {
        if (SelectedProject == null)
            return;

        var deleted = _projectService.RemoveProject(SelectedProject.Project.Id);
        if (!deleted)
            return;

        Projects.Remove(SelectedProject);
        SelectedProject = Projects.FirstOrDefault();
        ErrorMessage = null;
    }

    public void AddProject(
        string name,
        string folderPath,
        string clientFolderPath,
        string productType)
    {
        try
        {
            var project = _projectService.AddProject(name, folderPath, clientFolderPath, productType);
            var entry = new ProjectEntryViewModel(project, _projectService, _fileExplorerService);
            Projects.Add(entry);
            SelectedProject = entry;
            ErrorMessage = null;
        }
        catch (Exception exception)
        {
            ReportError(exception.Message);
        }
    }

    private void ReportError(string message)
    {
        ErrorMessage = message;
        ErrorRaised?.Invoke(this, message);
    }

    private bool CanOpenProject() => SelectedProject != null;
    private bool CanDeleteProject() => SelectedProject != null;
}
