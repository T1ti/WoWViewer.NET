using Avalonia.Controls;
using Avalonia.Input;
using WTEditor.Application.Models;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Views;

public partial class ProjectSelectionWindow : Window
{
    public event EventHandler? ProjectOpened;

    public ProjectSelectionWindow()
    {
        InitializeComponent();
    }

    public ProjectSelectionWindow(ProjectSelectionViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.AddProjectRequested += OnAddProjectRequested;
        viewModel.ProjectOpened += OnProjectOpened;
        viewModel.ErrorRaised += OnErrorRaised;
        Closed += (_, _) =>
        {
            viewModel.AddProjectRequested -= OnAddProjectRequested;
            viewModel.ProjectOpened -= OnProjectOpened;
            viewModel.ErrorRaised -= OnErrorRaised;
        };
    }

    private async void OnAddProjectRequested(object? sender, EventArgs e)
    {
        if (DataContext is not ProjectSelectionViewModel viewModel)
            return;

        var defaults = viewModel.GetNewProjectDefaults();
        var dialog = new NewProjectWindow(
            new NewProjectViewModel(defaults.Name, defaults.Folder, defaults.ClientFolder, defaults.ProductType))
        {
            ValidateProject = viewModel.ValidateProject
        };
        var accepted = await dialog.ShowDialog<bool>(this);
        if (accepted)
            viewModel.AddProject(dialog.ProjectName, dialog.ProjectFolder, dialog.ClientFolder, dialog.ProductType);
    }

    private void OnProjectOpened(object? sender, ProjectDefinition e) => ProjectOpened?.Invoke(this, EventArgs.Empty);

    private async void OnErrorRaised(object? sender, string message) =>
        await new ErrorMessageWindow("Unable to load project", message).ShowDialog<bool>(this);

    private void ProjectEntry_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2)
            return;

        if (DataContext is not ProjectSelectionViewModel viewModel)
            return;

        var project = (sender as Control)?.DataContext as ProjectEntryViewModel;
        if (project == null && e.Source is Control source)
        {
            for (var control = source; control != null; control = control.Parent as Control)
            {
                if (control.DataContext is ProjectEntryViewModel entry)
                {
                    project = entry;
                    break;
                }
            }
        }

        if (project == null)
            return;

        viewModel.OpenProject(project);
        e.Handled = true;
    }

    private void ProjectSelectionWindow_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ProjectSelectionViewModel viewModel)
            return;

        viewModel.OpenProject(viewModel.SelectedProject);
        e.Handled = true;
    }
}
