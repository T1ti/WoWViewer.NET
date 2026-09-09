using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTEditor.Avalonia.Services;

namespace WTEditor.Avalonia.ViewModels;

internal readonly record struct TextureBrowserCatalogEntry(uint FileDataId, string Path);

public sealed class TextureBrowserFolderViewModel
{
    internal TextureBrowserFolderViewModel(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
    }

    public string Name { get; }
    public string FullPath { get; }
    public ObservableCollection<TextureBrowserFolderViewModel> Children { get; } = [];
    internal List<TextureBrowserCatalogEntry> Textures { get; } = [];
}

public partial class TextureBrowserViewModel : ViewModelBase, IDisposable
{
    private const int MaximumResults = 80;
    private readonly ITerrainTextureThumbnailService _thumbnailService;
    private readonly IClientFileCatalogService _fileCatalog;
    private readonly Action<TexturePaletteItemViewModel>? _configureTexture;
    private TextureBrowserCatalogEntry[] _textures = [];
    private CancellationTokenSource? _catalogCancellation;
    private CancellationTokenSource? _thumbnailCancellation;
    private bool _isActivated;
    private bool _isRebuildingFolders;
    private IReadOnlyList<ClientFileCatalogEntry>? _activeCatalog;

    public TextureBrowserViewModel(
        ITerrainTextureThumbnailService thumbnailService,
        IClientFileCatalogService fileCatalog,
        Action<TexturePaletteItemViewModel>? configureTexture = null)
    {
        _thumbnailService = thumbnailService;
        _fileCatalog = fileCatalog;
        _configureTexture = configureTexture;
    }

    public ObservableCollection<TexturePaletteItemViewModel> Results { get; } = [];
    public ObservableCollection<TextureBrowserFolderViewModel> RootFolders { get; } = [];
    public ObservableCollection<TextureBrowserFolderViewModel> CurrentFolders { get; } = [];
    public ObservableCollection<TexturePaletteItemViewModel> FolderResults { get; } = [];
    public bool IsSimpleMode => !IsExplorerMode;
    public string CurrentFolderPath => SelectedFolder?.FullPath ?? "tileset";

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private TexturePaletteItemViewModel? _selectedTexture;
    [ObservableProperty] private TexturePaletteItemViewModel? _selectedSimpleTexture;
    [ObservableProperty] private TexturePaletteItemViewModel? _selectedExplorerTexture;
    [ObservableProperty] private TextureBrowserFolderViewModel? _selectedFolder;
    [ObservableProperty] private bool _includeSpecularTextures;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSimpleMode))]
    private bool _isExplorerMode;

    partial void OnSearchTextChanged(string value)
    {
        if (_isActivated)
            RefreshResults();
    }

    partial void OnIncludeSpecularTexturesChanged(bool value)
    {
        if (_isActivated)
        {
            RebuildFolderTree();
            RefreshResults();
        }
    }

    partial void OnIsExplorerModeChanged(bool value)
    {
        if (_isActivated)
            RefreshResults();
    }

    partial void OnSelectedFolderChanged(TextureBrowserFolderViewModel? value)
    {
        OnPropertyChanged(nameof(CurrentFolderPath));
        if (_isActivated && value != null && IsExplorerMode && !_isRebuildingFolders)
            RefreshResults();
    }

    partial void OnSelectedSimpleTextureChanged(TexturePaletteItemViewModel? value)
    {
        if (value != null)
            SelectedTexture = value;
    }

    partial void OnSelectedExplorerTextureChanged(TexturePaletteItemViewModel? value)
    {
        if (value != null)
            SelectedTexture = value;
    }

    [RelayCommand]
    private void NavigateFolder(TextureBrowserFolderViewModel? folder)
    {
        if (folder != null)
            SelectedFolder = folder;
    }

    public void Activate() => _ = ActivateAsync();

    internal static bool IsBrowsableTerrainTexture(string path, bool includeSpecular = false)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("tileset/", StringComparison.OrdinalIgnoreCase) &&
               normalized.EndsWith(".blp", StringComparison.OrdinalIgnoreCase) &&
               !normalized.EndsWith("_h.blp", StringComparison.OrdinalIgnoreCase) &&
               (includeSpecular || !normalized.EndsWith("_s.blp", StringComparison.OrdinalIgnoreCase));
    }

    private async Task ActivateAsync()
    {
        CancelCatalogLoad();
        CancelThumbnailLoad();
        _catalogCancellation = new CancellationTokenSource();
        var token = _catalogCancellation.Token;
        IsLoading = true;
        try
        {
            var catalog = await _fileCatalog.GetFilesAsync(token);
            if (token.IsCancellationRequested)
                return;
            if (_isActivated && ReferenceEquals(catalog, _activeCatalog))
                return;

            _textures = catalog
                .Where(entry => entry.FileDataId.HasValue &&
                                IsBrowsableTerrainTexture(entry.Path, includeSpecular: true))
                .Select(entry => new TextureBrowserCatalogEntry(entry.FileDataId!.Value, entry.Path))
                .OrderBy(texture => texture.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _activeCatalog = catalog;
            _isActivated = true;
            RebuildFolderTree();
            RefreshResults();
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
            _textures = [];
            Results.Clear();
            RootFolders.Clear();
            CurrentFolders.Clear();
            FolderResults.Clear();
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsLoading = false;
        }
    }

    private void RefreshResults()
    {
        CancelThumbnailLoad();
        _thumbnailCancellation = new CancellationTokenSource();
        var token = _thumbnailCancellation.Token;

        if (IsExplorerMode)
        {
            RefreshExplorerResults(token);
            return;
        }

        var words = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Results.Clear();
        foreach (var texture in _textures.Where(texture =>
                     IsBrowsableTerrainTexture(texture.Path, IncludeSpecularTextures) &&
                     words.All(word => texture.Path.Contains(word, StringComparison.OrdinalIgnoreCase)))
                 .Take(MaximumResults))
        {
            var item = new TexturePaletteItemViewModel(
                $"fdid:{texture.FileDataId}",
                TexturePaletteNaming.FromPath(texture.Path),
                FileDataId: texture.FileDataId,
                FullPath: texture.Path);
            _configureTexture?.Invoke(item);
            Results.Add(item);
        }

        _ = LoadThumbnailsAsync(Results.ToArray(), token);
    }

    private void RebuildFolderTree()
    {
        var previousPath = SelectedFolder?.FullPath;
        var root = BuildFolderTree(_textures.Where(texture =>
            IsBrowsableTerrainTexture(texture.Path, IncludeSpecularTextures)));

        _isRebuildingFolders = true;
        try
        {
            RootFolders.Clear();
            RootFolders.Add(root);
            SelectedFolder = FindFolder(root, previousPath) ?? root;
        }
        finally
        {
            _isRebuildingFolders = false;
        }
    }

    internal static TextureBrowserFolderViewModel BuildFolderTree(
        IEnumerable<TextureBrowserCatalogEntry> textures)
    {
        var root = new TextureBrowserFolderViewModel("tileset", "tileset");
        var folders = new Dictionary<string, TextureBrowserFolderViewModel>(
            StringComparer.OrdinalIgnoreCase)
        {
            [root.FullPath] = root
        };

        foreach (var texture in textures.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            var normalized = texture.Path.Replace('\\', '/').Trim('/');
            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[0].Equals("tileset", StringComparison.OrdinalIgnoreCase))
                continue;

            var parent = root;
            var path = "tileset";
            for (var index = 1; index < parts.Length - 1; index++)
            {
                path += $"/{parts[index]}";
                if (!folders.TryGetValue(path, out var folder))
                {
                    folder = new TextureBrowserFolderViewModel(parts[index], path);
                    folders.Add(path, folder);
                    parent.Children.Add(folder);
                }
                parent = folder;
            }
            parent.Textures.Add(texture with { Path = normalized });
        }

        return root;
    }

    private static TextureBrowserFolderViewModel? FindFolder(
        TextureBrowserFolderViewModel folder,
        string? fullPath)
    {
        if (fullPath == null)
            return null;
        if (folder.FullPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
            return folder;
        foreach (var child in folder.Children)
        {
            var match = FindFolder(child, fullPath);
            if (match != null)
                return match;
        }
        return null;
    }

    private void RefreshExplorerResults(CancellationToken token)
    {
        CurrentFolders.Clear();
        FolderResults.Clear();
        if (SelectedFolder is not { } folder)
            return;

        foreach (var child in folder.Children)
            CurrentFolders.Add(child);
        foreach (var texture in folder.Textures)
        {
            var item = new TexturePaletteItemViewModel(
                $"fdid:{texture.FileDataId}",
                TexturePaletteNaming.FromPath(texture.Path),
                FileDataId: texture.FileDataId,
                FullPath: texture.Path);
            _configureTexture?.Invoke(item);
            FolderResults.Add(item);
        }
        _ = LoadThumbnailsAsync(FolderResults.ToArray(), token);
    }

    private async Task LoadThumbnailsAsync(
        IReadOnlyList<TexturePaletteItemViewModel> textures,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var texture in textures)
            {
                if (texture.FileDataId is { } id)
                    texture.Thumbnail = await _thumbnailService.LoadAsync(id, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelCatalogLoad()
    {
        _catalogCancellation?.Cancel();
        _catalogCancellation?.Dispose();
        _catalogCancellation = null;
    }

    private void CancelThumbnailLoad()
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
    }

    public void Dispose()
    {
        CancelCatalogLoad();
        CancelThumbnailLoad();
    }
}
