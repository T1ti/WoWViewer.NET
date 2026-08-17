using CommunityToolkit.Mvvm.ComponentModel;

namespace WTEditor.Avalonia.ViewModels;

public partial class NewProjectViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _projectName;

    [ObservableProperty]
    private string _projectFolder;

    [ObservableProperty]
    private string _clientFolder;

    [ObservableProperty]
    private string _productType;

    public NewProjectViewModel(
        string projectName,
        string projectFolder,
        string clientFolder,
        string productType = "wow_classic_era")
    {
        _projectName = projectName;
        _projectFolder = projectFolder;
        _clientFolder = clientFolder;
        _productType = productType;
    }
}
