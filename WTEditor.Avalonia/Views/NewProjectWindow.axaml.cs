using Avalonia.Controls;
using Avalonia.Platform.Storage;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Views;

public partial class NewProjectWindow : Window
{
    private readonly NewProjectViewModel _viewModel;

    public Func<string, string, string, string, string?>? ValidateProject { get; init; }

    public string ProjectName => _viewModel.ProjectName;
    public string ProjectFolder => _viewModel.ProjectFolder;
    public string ClientFolder => _viewModel.ClientFolder;
    public string ProductType => _viewModel.ProductType;

    public NewProjectWindow()
        : this(new NewProjectViewModel(
            "project1",
            Path.Combine(AppContext.BaseDirectory, "project1"),
            "",
            "wow_classic_era"))
    {
    }

    public NewProjectWindow(NewProjectViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void BrowseProjectFolder_OnClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) =>
        await BrowseFolderAsync("Select project folder", folder => _viewModel.ProjectFolder = folder);

    private async void BrowseClientFolder_OnClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) =>
        await BrowseFolderAsync("Select WoW client folder", folder => _viewModel.ClientFolder = folder);

    private async Task BrowseFolderAsync(string title, Action<string> setFolder)
    {
        if (!StorageProvider.CanPickFolder)
            return;

        var currentPath = title.Contains("project", StringComparison.OrdinalIgnoreCase)
            ? _viewModel.ProjectFolder
            : _viewModel.ClientFolder;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await GetSuggestedStartLocationAsync(currentPath)
        });
        var folder = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(folder))
            setFolder(folder);
    }

    private async Task<IStorageFolder?> GetSuggestedStartLocationAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var candidate = Path.GetFullPath(path.Trim());
            while (!Directory.Exists(candidate))
            {
                var parent = Directory.GetParent(candidate)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
                    return null;
                candidate = parent;
            }

            return await StorageProvider.TryGetFolderFromPathAsync(candidate);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async void Add_OnClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var error = ValidateProject?.Invoke(ProjectName, ProjectFolder, ClientFolder, ProductType);
        if (error != null)
        {
            await new ErrorMessageWindow("Unable to add project", error).ShowDialog<bool>(this);
            return;
        }

        Close(true);
    }

    private void Cancel_OnClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => Close(false);
}
