using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTEditor.Avalonia.Services;

namespace WTEditor.Avalonia.ViewModels;

internal readonly record struct TextureBrowserCatalogEntry(
    uint FileDataId,
    string Path,
    bool HasSpecularVariant = false,
    bool HasHeightVariant = false);

internal readonly record struct TextureVariantAvailability(bool HasSpecular, bool HasHeight);

public sealed class TextureBrowserFolderViewModel
{
    internal TextureBrowserFolderViewModel(
        string name,
        string fullPath,
        TextureBrowserFolderViewModel? parent = null)
    {
        Name = name;
        FullPath = fullPath;
        Parent = parent;
    }

    public string Name { get; }
    public string FullPath { get; }
    public TextureBrowserFolderViewModel? Parent { get; }
    public bool HasDirectTextures => Textures.Count > 0;
    public ObservableCollection<TextureBrowserFolderViewModel> Children { get; } = [];
    internal List<TextureBrowserCatalogEntry> Textures { get; } = [];
}

public partial class TextureBrowserViewModel : ViewModelBase, IDisposable
{
    private readonly ITerrainTextureThumbnailService _thumbnailService;
    private readonly IClientFileCatalogService _fileCatalog;
    private readonly Action<TexturePaletteItemViewModel>? _configureTexture;
    private readonly Action<IReadOnlyList<TexturePaletteItemViewModel>>? _replaceFavorites;
    private TextureBrowserCatalogEntry[] _textures = [];
    private CancellationTokenSource? _catalogCancellation;
    private CancellationTokenSource? _thumbnailCancellation;
    private bool _isActivated;
    private bool _isRebuildingFolders;
    private bool _isDisposed;
    private IReadOnlyList<ClientFileCatalogEntry>? _activeCatalog;

    public TextureBrowserViewModel(
        ITerrainTextureThumbnailService thumbnailService,
        IClientFileCatalogService fileCatalog,
        Action<TexturePaletteItemViewModel>? configureTexture = null,
        Action<IReadOnlyList<TexturePaletteItemViewModel>>? replaceFavorites = null)
    {
        _thumbnailService = thumbnailService;
        _fileCatalog = fileCatalog;
        _configureTexture = configureTexture;
        _replaceFavorites = replaceFavorites;
    }

    public ObservableCollection<TexturePaletteItemViewModel> Results { get; } = [];
    public ObservableCollection<TextureBrowserFolderViewModel> RootFolders { get; } = [];
    public ObservableCollection<TextureBrowserFolderViewModel> CurrentFolders { get; } = [];
    public ObservableCollection<TexturePaletteItemViewModel> FolderResults { get; } = [];
    public ObservableCollection<object> CurrentEntries { get; } = [];
    public bool IsSimpleMode => !IsExplorerMode;
    public string CurrentFolderPath => SelectedFolder?.FullPath ?? "tileset";
    public bool CanNavigateToParent => SelectedFolder?.Parent != null;

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
        OnPropertyChanged(nameof(CanNavigateToParent));
        NavigateToParentCommand.NotifyCanExecuteChanged();
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

    [RelayCommand(CanExecute = nameof(CanNavigateToParent))]
    private void NavigateToParent()
    {
        if (SelectedFolder?.Parent is { } parent)
            SelectedFolder = parent;
    }

    [RelayCommand]
    private void SetFolderAsFavorites(TextureBrowserFolderViewModel? folder)
    {
        if (folder == null || _replaceFavorites == null)
            return;

        _replaceFavorites(folder.Textures.Select(CreateTextureItem).ToArray());
    }

    public void Activate()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _ = ActivateAsync();
    }

    internal static bool IsBrowsableTerrainTexture(string path, bool includeSpecular = false)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("tileset/", StringComparison.OrdinalIgnoreCase) &&
               normalized.EndsWith(".blp", StringComparison.OrdinalIgnoreCase) &&
               !normalized.EndsWith("_h.blp", StringComparison.OrdinalIgnoreCase) &&
               (includeSpecular || !normalized.EndsWith("_s.blp", StringComparison.OrdinalIgnoreCase));
    }

    internal static TextureVariantAvailability GetVariantAvailability(
        string path,
        IReadOnlySet<string> clientPaths)
    {
        var normalized = path.Replace('\\', '/');
        if (!normalized.EndsWith(".blp", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("_s.blp", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("_h.blp", StringComparison.OrdinalIgnoreCase))
            return default;

        var basePath = normalized[..^4];
        return new TextureVariantAvailability(
            clientPaths.Contains(basePath + "_s.blp"),
            clientPaths.Contains(basePath + "_h.blp"));
    }

    internal async Task ActivateAsync()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        CancelCatalogLoad();
        CancelThumbnailLoad();
        var cancellation = new CancellationTokenSource();
        _catalogCancellation = cancellation;
        var token = cancellation.Token;
        IsLoading = true;
        try
        {
            var catalog = await _fileCatalog.GetFilesAsync(token);
            if (_isDisposed || token.IsCancellationRequested)
                return;
            if (_isActivated && ReferenceEquals(catalog, _activeCatalog))
                return;

            var clientPaths = catalog
                .Select(entry => entry.Path.Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _textures = catalog
                .Where(entry => entry.FileDataId.HasValue &&
                                IsBrowsableTerrainTexture(entry.Path, includeSpecular: true))
                .Select(entry =>
                {
                    var variants = GetVariantAvailability(entry.Path, clientPaths);
                    return new TextureBrowserCatalogEntry(
                        entry.FileDataId!.Value,
                        entry.Path,
                        variants.HasSpecular,
                        variants.HasHeight);
                })
                .OrderBy(texture => texture.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _activeCatalog = catalog;
            _isActivated = true;
            RebuildFolderTree();
            RefreshResults();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!_isDisposed)
            {
                Trace.TraceError($"Unable to load the terrain texture catalog: {exception}");
                _textures = [];
                Results.Clear();
                RootFolders.Clear();
                CurrentFolders.Clear();
                FolderResults.Clear();
                CurrentEntries.Clear();
            }
        }
        finally
        {
            if (ReferenceEquals(_catalogCancellation, cancellation))
            {
                _catalogCancellation = null;
                if (!_isDisposed)
                    IsLoading = false;
            }

            cancellation.Dispose();
        }
    }

    private void RefreshResults()
    {
        CancelThumbnailLoad();

        if (IsExplorerMode)
        {
            RefreshExplorerResults();
            return;
        }

        var words = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Results.Clear();
        foreach (var texture in _textures.Where(texture =>
                     IsBrowsableTerrainTexture(texture.Path, IncludeSpecularTextures) &&
                     words.All(word => texture.Path.Contains(word, StringComparison.OrdinalIgnoreCase))))
        {
            Results.Add(CreateTextureItem(texture));
        }

        StartThumbnailLoad(Results.ToArray());
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
                    folder = new TextureBrowserFolderViewModel(parts[index], path, parent);
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

    private void RefreshExplorerResults()
    {
        CurrentFolders.Clear();
        FolderResults.Clear();
        CurrentEntries.Clear();
        if (SelectedFolder is not { } folder)
            return;

        foreach (var child in folder.Children)
        {
            CurrentFolders.Add(child);
            CurrentEntries.Add(child);
        }
        foreach (var texture in folder.Textures)
        {
            var item = CreateTextureItem(texture);
            FolderResults.Add(item);
            CurrentEntries.Add(item);
        }
        StartThumbnailLoad(FolderResults.ToArray());
    }

    private TexturePaletteItemViewModel CreateTextureItem(TextureBrowserCatalogEntry texture)
    {
        var item = new TexturePaletteItemViewModel(
            $"fdid:{texture.FileDataId}",
            TexturePaletteNaming.FromPath(texture.Path),
            FileDataId: texture.FileDataId,
            FullPath: texture.Path,
            HasSpecularVariant: texture.HasSpecularVariant,
            HasHeightVariant: texture.HasHeightVariant);
        _configureTexture?.Invoke(item);
        return item;
    }

    private void StartThumbnailLoad(IReadOnlyList<TexturePaletteItemViewModel> textures)
    {
        if (_isDisposed || textures.Count == 0)
            return;

        var cancellation = new CancellationTokenSource();
        _thumbnailCancellation = cancellation;
        _ = LoadThumbnailsAsync(textures, cancellation);
    }

    private async Task LoadThumbnailsAsync(
        IReadOnlyList<TexturePaletteItemViewModel> textures,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            foreach (var texture in textures)
            {
                if (texture.FileDataId is { } id)
                {
                    var thumbnail = await _thumbnailService.LoadAsync(id, cancellationToken);
                    if (_isDisposed || cancellationToken.IsCancellationRequested)
                        return;
                    texture.Thumbnail = thumbnail;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!_isDisposed)
                Trace.TraceError($"Unable to load a terrain texture thumbnail: {exception}");
        }
        finally
        {
            if (ReferenceEquals(_thumbnailCancellation, cancellation))
                _thumbnailCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelCatalogLoad()
    {
        var cancellation = _catalogCancellation;
        _catalogCancellation = null;
        cancellation?.Cancel();
    }

    private void CancelThumbnailLoad()
    {
        var cancellation = _thumbnailCancellation;
        _thumbnailCancellation = null;
        cancellation?.Cancel();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        CancelCatalogLoad();
        CancelThumbnailLoad();
        GC.SuppressFinalize(this);
    }
}
