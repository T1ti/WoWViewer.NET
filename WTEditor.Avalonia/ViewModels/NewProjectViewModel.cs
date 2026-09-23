using CommunityToolkit.Mvvm.ComponentModel;
using WTEditor.Application.Services;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProductSelectionEnabled))]
    private IReadOnlyList<string> _availableProducts = [];

    public bool IsProductSelectionEnabled => AvailableProducts.Count > 0;

    private readonly string _preferredProduct;

    public NewProjectViewModel(
        string projectName,
        string projectFolder,
        string clientFolder,
        string productType = "wow_classic_era")
    {
        _projectName = projectName;
        _projectFolder = projectFolder;
        _clientFolder = clientFolder;
        _preferredProduct = productType;
        _productType = "";
        UpdateProducts();
    }

    partial void OnClientFolderChanged(string value) => UpdateProducts();

    private void UpdateProducts()
    {
        AvailableProducts = ProjectService.IsValidClientFolder(ClientFolder)
            ? ClientProductCatalog.GetProducts(ClientFolder)
            : [];
        ProductType = AvailableProducts.FirstOrDefault(product =>
                string.Equals(product, _preferredProduct, StringComparison.OrdinalIgnoreCase))
            ?? AvailableProducts.FirstOrDefault()
            ?? "";
    }
}
