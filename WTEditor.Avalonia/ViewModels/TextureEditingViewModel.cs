using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTEditor.Avalonia.Presentation;
using WTEditor.Avalonia.Services;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Editing;

namespace WTEditor.Avalonia.ViewModels;

public partial class TexturePaletteItemViewModel : ViewModelBase
{
    private Func<TexturePaletteItemViewModel, Task>? _previewHandler;

    public TexturePaletteItemViewModel(
        string Id,
        string DisplayName,
        IImage? Thumbnail = null,
        uint? FileDataId = null,
        string? FullPath = null)
    {
        this.Id = Id;
        this.DisplayName = DisplayName;
        _thumbnail = Thumbnail;
        this.FileDataId = FileDataId;
        this.FullPath = FullPath;
        PreviewCommand = new AsyncRelayCommand(
            () => _previewHandler?.Invoke(this) ?? Task.CompletedTask,
            () => FileDataId.HasValue && _previewHandler != null);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public uint? FileDataId { get; }
    public string? FullPath { get; }
    public IAsyncRelayCommand PreviewCommand { get; }
    [ObservableProperty] private IImage? _thumbnail;
    public string Tooltip
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(FullPath) ? DisplayName : FullPath;
            return FileDataId is { } id ? $"{name}\nFile data ID: {id}" : name;
        }
    }

    internal void SetPreviewHandler(Func<TexturePaletteItemViewModel, Task>? handler)
    {
        _previewHandler = handler;
        PreviewCommand.NotifyCanExecuteChanged();
    }
}

internal static class TexturePaletteNaming
{
    public static string FromFileDataId(uint fileDataId) =>
        FromPath(WoWRenderLib.Listfile.GetDisplayName(fileDataId));

    public static string FromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        return Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
    }
}

/// <summary>Texture-specific operation state composed with shared brush settings.</summary>
public partial class TextureEditingViewModel : BrushToolViewModelBase, IDisposable
{
    public event EventHandler? CurrentTerrainTileTexturesRequested;
    private readonly ITerrainTextureThumbnailService? _thumbnailService;
    private readonly ITerrainTexturePreviewService? _previewService;
    private CancellationTokenSource? _chunkTextureLoadCancellation;
    private double _expandedBrowserHeight = 500d;
    private static readonly IReadOnlyDictionary<TextureBrushMode, ToolSubModeViewModel> ModeDefinitions =
        new Dictionary<TextureBrushMode, ToolSubModeViewModel>
        {
            [TextureBrushMode.Paint] = new("paint", "Paint", "Paint the selected terrain texture inside the brush.", EditorIcons.Paint, true),
            [TextureBrushMode.Smooth] = new("smooth", "Smooth", "Soften transitions between all terrain texture layers inside the brush.", EditorIcons.Smooth, true),
            [TextureBrushMode.Colour] = new("colour", "Colour", "Tint terrain colour inside the brush.", EditorIcons.Colour, true)
        };

    public TextureEditingViewModel(
        ITerrainTextureThumbnailService? thumbnailService = null,
        ITerrainTexturePreviewService? previewService = null,
        IClientFileCatalogService? fileCatalog = null)
        : base(ModeDefinitions.Values, BuiltInBrushPresets.All)
    {
        _thumbnailService = thumbnailService;
        _previewService = previewService;
        if (thumbnailService != null)
        {
            Browser = new TextureBrowserViewModel(
                thumbnailService,
                fileCatalog ?? new ClientFileCatalogService(),
                ConfigureTexture);
            Browser.PropertyChanged += OnBrowserPropertyChanged;
        }
    }

    public TextureBrushMode ToolMode => ModeDefinitions.First(pair => pair.Value == SelectedSubMode).Key;
    public double OpacityMinimum => 0d;
    public double OpacityMaximum => 255d;
    public ObservableCollection<TexturePaletteItemViewModel> Favorites { get; } = [];
    // Compatibility alias for persisted callers created before the palette was named Favorites.
    public ObservableCollection<TexturePaletteItemViewModel> UserTextures => Favorites;
    public ObservableCollection<TexturePaletteItemViewModel> ChunkTextures { get; } = [];
    public TextureBrowserViewModel? Browser { get; }
    public bool IsOpacityVisible => ToolMode != TextureBrushMode.Smooth;
    public bool HasSelectedTexture => SelectedTexture != null;
    public bool HasNoSelectedTexture => SelectedTexture == null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTexture))]
    [NotifyPropertyChangedFor(nameof(HasNoSelectedTexture))]
    private TexturePaletteItemViewModel? _selectedTexture;

    // Kept separate from SelectedTexture because Avalonia clears a ListBox selection
    // when the active texture is not part of its ItemsSource. That must not clear the
    // editor's active texture (for example after selecting one from a terrain chunk).
    [ObservableProperty]
    private TexturePaletteItemViewModel? _selectedFavorite;

    [ObservableProperty]
    private bool _isChunkPickerOpen;

    [ObservableProperty]
    private double _strength = 1d;

    [ObservableProperty]
    private double _opacity = 255d;

    [ObservableProperty]
    private bool _isPickerModeActive;

    [ObservableProperty]
    private bool _isBrowserVisible;

    [ObservableProperty]
    private bool _isBrowserExpanded = true;

    [ObservableProperty]
    private double _browserHeight = 500d;

    [RelayCommand]
    private void SelectTexture(TexturePaletteItemViewModel? texture)
    {
        if (texture != null)
            SelectedTexture = texture;
        IsChunkPickerOpen = false;
    }

    [RelayCommand]
    private void AddFavorite(TexturePaletteItemViewModel? texture)
    {
        if (texture == null || FindFavorite(texture) != null)
            return;
        ConfigureTexture(texture);
        Favorites.Add(texture);
        if (ReferenceEquals(texture, SelectedTexture))
            SelectedFavorite = texture;
    }

    [RelayCommand]
    private void RemoveFavorite(TexturePaletteItemViewModel? texture)
    {
        if (texture != null)
        {
            Favorites.Remove(texture);
            if (ReferenceEquals(SelectedFavorite, texture))
                SelectedFavorite = null;
        }
    }

    [RelayCommand]
    private Task PreviewTextureAsync(TexturePaletteItemViewModel? texture) =>
        texture?.FileDataId is { } fileDataId && _previewService != null
            ? _previewService.ShowAsync(fileDataId, texture.DisplayName)
            : Task.CompletedTask;

    [RelayCommand]
    private void ShowTextureBrowser()
    {
        Browser?.Activate();
        IsBrowserVisible = true;
        if (!IsBrowserExpanded)
            ToggleBrowserExpanded();
    }

    [RelayCommand]
    private void HideTextureBrowser() => IsBrowserVisible = false;

    [RelayCommand]
    private void ToggleBrowserExpanded()
    {
        if (IsBrowserExpanded)
        {
            _expandedBrowserHeight = Math.Max(260d, BrowserHeight);
            IsBrowserExpanded = false;
            BrowserHeight = 44d;
        }
        else
        {
            IsBrowserExpanded = true;
            BrowserHeight = _expandedBrowserHeight;
        }
    }

    private void OnBrowserPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextureBrowserViewModel.SelectedTexture) &&
            Browser?.SelectedTexture is { } texture)
            SelectTexture(texture);
    }

    private void ConfigureTexture(TexturePaletteItemViewModel texture) =>
        texture.SetPreviewHandler(PreviewTextureAsync);

    public void ShowChunkTextures(IReadOnlyList<TerrainChunkTextureLayer> layers)
    {
        _chunkTextureLoadCancellation?.Cancel();
        _chunkTextureLoadCancellation?.Dispose();
        _chunkTextureLoadCancellation = new CancellationTokenSource();

        ChunkTextures.Clear();
        foreach (var layer in layers.OrderBy(layer => layer.LayerIndex))
        {
            var displayName = TexturePaletteNaming.FromFileDataId(layer.FileDataId);
            var item = new TexturePaletteItemViewModel(
                $"fdid:{layer.FileDataId}",
                displayName,
                FileDataId: layer.FileDataId);
            ConfigureTexture(item);
            ChunkTextures.Add(item);
        }

        IsChunkPickerOpen = ChunkTextures.Count > 0;
        if (IsChunkPickerOpen && _thumbnailService != null)
            _ = LoadChunkThumbnailsAsync(ChunkTextures.ToArray(), _chunkTextureLoadCancellation.Token);
    }

    public void SelectTerrainTexture(TerrainChunkTextureLayer texture)
    {
        IsPickerModeActive = false;
        IsChunkPickerOpen = false;
        var id = $"fdid:{texture.FileDataId}";
        var item = Favorites.FirstOrDefault(candidate =>
                       candidate.Id == id || candidate.FileDataId == texture.FileDataId) ??
            new TexturePaletteItemViewModel(
                id,
                TexturePaletteNaming.FromFileDataId(texture.FileDataId),
                FileDataId: texture.FileDataId);
        ConfigureTexture(item);
        SelectedTexture = item;

        if (item.Thumbnail == null && _thumbnailService != null)
            _ = LoadSelectedThumbnailAsync(item);
    }

    [RelayCommand]
    private void AddFromCurrentPositionTile() =>
        CurrentTerrainTileTexturesRequested?.Invoke(this, EventArgs.Empty);

    public void AddFavorites(IReadOnlyList<TerrainChunkTextureLayer> textures)
    {
        foreach (var texture in textures)
        {
            var item = new TexturePaletteItemViewModel(
                $"fdid:{texture.FileDataId}",
                TexturePaletteNaming.FromFileDataId(texture.FileDataId),
                FileDataId: texture.FileDataId);
            ConfigureTexture(item);
            if (FindFavorite(item) != null)
                continue;
            Favorites.Add(item);
            if (_thumbnailService != null)
                _ = LoadSelectedThumbnailAsync(item);
        }
    }

    private TexturePaletteItemViewModel? FindFavorite(TexturePaletteItemViewModel texture) =>
        Favorites.FirstOrDefault(item =>
            item.Id == texture.Id ||
            (texture.FileDataId.HasValue && item.FileDataId == texture.FileDataId));

    private async Task LoadSelectedThumbnailAsync(TexturePaletteItemViewModel texture)
    {
        if (texture.FileDataId is not { } fileDataId)
            return;
        texture.Thumbnail = await _thumbnailService!.LoadAsync(fileDataId);
    }

    private async Task LoadChunkThumbnailsAsync(
        IReadOnlyList<TexturePaletteItemViewModel> textures,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(textures.Select(async texture =>
            {
                if (texture.FileDataId is { } fileDataId)
                    texture.Thumbnail = await _thumbnailService!.LoadAsync(fileDataId, cancellationToken);
            }));
        }
        catch (OperationCanceledException)
        {
        }
    }

    protected override void OnSubModeChanged()
    {
        OnPropertyChanged(nameof(ToolMode));
        OnPropertyChanged(nameof(IsOpacityVisible));
    }

    public void AddOrSelectTexture(TexturePaletteItemViewModel texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        var existing = FindFavorite(texture);
        if (existing == null)
        {
            ConfigureTexture(texture);
            Favorites.Add(texture);
            existing = texture;
        }
        else
        {
            ConfigureTexture(existing);
        }
        SelectedTexture = existing;
    }

    partial void OnSelectedFavoriteChanged(TexturePaletteItemViewModel? value)
    {
        // A null value is ListBox bookkeeping, not a request to clear the active tool.
        if (value != null && !ReferenceEquals(value, SelectedTexture))
            SelectedTexture = value;
    }

    partial void OnSelectedTextureChanged(TexturePaletteItemViewModel? value)
    {
        ConfigureTextureIfPresent(value);
        var favorite = value == null ? null : FindFavorite(value);
        if (!ReferenceEquals(SelectedFavorite, favorite))
            SelectedFavorite = favorite;
    }

    private void ConfigureTextureIfPresent(TexturePaletteItemViewModel? texture)
    {
        if (texture != null)
            ConfigureTexture(texture);
    }

    partial void OnOpacityChanged(double value) => Opacity = Math.Clamp(value, OpacityMinimum, OpacityMaximum);
    partial void OnStrengthChanged(double value) => Strength = Math.Clamp(value, 0d, 1d);

    public void Dispose()
    {
        _chunkTextureLoadCancellation?.Cancel();
        _chunkTextureLoadCancellation?.Dispose();
        _chunkTextureLoadCancellation = null;
        if (Browser != null)
        {
            Browser.PropertyChanged -= OnBrowserPropertyChanged;
            Browser.Dispose();
        }
    }
}
