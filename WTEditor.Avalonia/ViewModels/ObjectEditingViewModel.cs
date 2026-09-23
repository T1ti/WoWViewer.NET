using System.Diagnostics;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTEditor.Application.Models;
using WTEditor.Avalonia.Services;

namespace WTEditor.Avalonia.ViewModels;

public partial class ObjectBrowserItemViewModel : ViewModelBase
{
    public ObjectBrowserItemViewModel(string filename, string path, uint? fileDataId, string kind)
    {
        Filename = filename;
        Path = path;
        FileDataId = fileDataId;
        Kind = kind;
    }

    public string Filename { get; }
    public string Path { get; }
    public uint? FileDataId { get; }
    public string Kind { get; }
    [ObservableProperty] private bool _isFavorite;
}

public sealed class ObjectBrowserFolderViewModel(ObjectAssetFolder folder, ObjectBrowserFolderViewModel? parent)
{
    public string Name => Folder.Name;
    public string Path => Folder.Path;
    public ObjectBrowserFolderViewModel? Parent { get; } = parent;
    public IReadOnlyList<ObjectBrowserFolderViewModel> Children { get; internal set; } = [];
    internal ObjectAssetFolder Folder { get; } = folder;
}

public partial class ObjectEditingViewModel : ViewModelBase, IDisposable
{
    private const int FolderPageSize = 200;
    private static readonly TimeSpan SearchDelay = TimeSpan.FromSeconds(1);
    private readonly IObjectAssetCatalogService _catalog;
    private readonly ObjectClipboardService _clipboard;
    private readonly List<ObjectBrowserItemViewModel> _favorites = [];
    private readonly HashSet<string> _favoritePaths = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<EditorObjectSnapshot> _worldSelection = [];
    private ObjectAssetIndex? _index;
    private ObjectAssetFilterResult? _activeFilter;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _filterCancellation;
    private int _visibleEntryCount = FolderPageSize;
    private string _appliedSearchText = string.Empty;
    private DateTime _searchDueAtUtc;
    private bool _disposed;

    public ObjectEditingViewModel(IObjectAssetCatalogService catalog, ObjectClipboardService clipboard)
    {
        _catalog = catalog;
        _clipboard = clipboard;
        _clipboard.Changed += OnClipboardChanged;
        ClipboardLabel = FormatObjectNames(_clipboard.Entries.Select(entry => entry.Name));
    }

    public bool HasWorldSelection => _worldSelection.Count > 0;
    public GridLength BrowserSplitterWidth => IsBrowserVisible ? new GridLength(5) : new GridLength(0);
    public GridLength BrowserPanelWidth => IsBrowserVisible
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(0);
    public string CurrentFolderPath => string.IsNullOrEmpty(SelectedFolder?.Path)
        ? "Objects"
        : SelectedFolder.Path;
    public string FavoritesTabLabel => $"Favorites ({_favorites.Count})";

    [ObservableProperty] private bool _isPanelVisible = true;
    [ObservableProperty] private bool _isBrowserVisible = true;
    [ObservableProperty] private bool _showWmo = true;
    [ObservableProperty] private bool _showM2 = true;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isFiltering;
    [ObservableProperty] private bool _hasMoreEntries;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _selectedBrowserTabIndex;
    [ObservableProperty] private IReadOnlyList<ObjectBrowserFolderViewModel> _rootFolders = [];
    [ObservableProperty] private IReadOnlyList<object> _folderEntries = [];
    [ObservableProperty] private IReadOnlyList<ObjectBrowserItemViewModel> _favoriteResults = [];
    [ObservableProperty] private ObjectBrowserFolderViewModel? _selectedFolder;
    [ObservableProperty] private ObjectBrowserItemViewModel? _selectedObject;
    [ObservableProperty] private ObjectBrowserItemViewModel? _selectedFavorite;
    [ObservableProperty] private object? _selectedFolderEntry;
    [ObservableProperty] private string _clipboardLabel = "None";
    [ObservableProperty] private string _selectedWorldObjectsLabel = "None";

    [RelayCommand] private void TogglePanel() => IsPanelVisible = !IsPanelVisible;
    [RelayCommand] private void ToggleBrowser() => IsBrowserVisible = !IsBrowserVisible;

    partial void OnIsBrowserVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(BrowserSplitterWidth));
        OnPropertyChanged(nameof(BrowserPanelWidth));
    }

    [RelayCommand]
    private void ChooseObject(ObjectBrowserItemViewModel? item)
    {
        if (item == null)
            return;
        SelectedObject = item;
        _clipboard.SelectAsset(item.Path, item.FileDataId);
    }

    [RelayCommand]
    private void AddFavorite(ObjectBrowserItemViewModel? item)
    {
        if (item == null || !_favoritePaths.Add(item.Path))
            return;
        item.IsFavorite = true;
        _favorites.Add(item);
        OnPropertyChanged(nameof(FavoritesTabLabel));
        RefreshFavorites();
    }

    [RelayCommand]
    private void RemoveFavorite(ObjectBrowserItemViewModel? item)
    {
        if (item == null || !_favoritePaths.Remove(item.Path))
            return;
        _favorites.RemoveAll(candidate =>
            candidate.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase));
        item.IsFavorite = false;
        foreach (var entry in FolderEntries.OfType<ObjectBrowserItemViewModel>())
            if (entry.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase))
                entry.IsFavorite = false;
        OnPropertyChanged(nameof(FavoritesTabLabel));
        RefreshFavorites();
    }

    partial void OnSelectedObjectChanged(ObjectBrowserItemViewModel? value)
    {
        if (value != null)
            _clipboard.SelectAsset(value.Path, value.FileDataId);
    }

    partial void OnSelectedFavoriteChanged(ObjectBrowserItemViewModel? value)
    {
        if (value != null)
            ChooseObject(value);
    }

    partial void OnSelectedFolderEntryChanged(object? value)
    {
        if (value is ObjectBrowserItemViewModel item)
            ChooseObject(item);
    }

    public void SetWorldSelection(IReadOnlyList<EditorObjectSnapshot> selection)
    {
        _worldSelection = selection.ToArray();
        SelectedWorldObjectsLabel = FormatObjectNames(_worldSelection.Select(item => item.Name));
        OnPropertyChanged(nameof(HasWorldSelection));
    }

    public void CopyWorldSelection() => _clipboard.CopyWorldObjects(_worldSelection);

    private void OnClipboardChanged(object? sender, EventArgs e) =>
        ClipboardLabel = FormatObjectNames(_clipboard.Entries.Select(entry => entry.Name));

    internal static string FormatObjectNames(IEnumerable<string> names)
    {
        var selected = names.Take(2).ToArray();
        return selected.Length switch
        {
            0 => "None",
            1 => selected[0],
            _ => $"{names.Count()} objects"
        };
    }

    [RelayCommand]
    private void NavigateToParent()
    {
        if (SelectedFolder?.Parent is { } parent)
            SelectedFolder = parent;
    }

    [RelayCommand]
    private void NavigateFolder(ObjectBrowserFolderViewModel? folder)
    {
        if (folder != null)
            SelectedFolder = folder;
    }

    [RelayCommand]
    private void LoadMoreEntries()
    {
        if (!HasMoreEntries)
            return;
        _visibleEntryCount += FolderPageSize;
        PublishCurrentFolder();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDueAtUtc = DateTime.UtcNow + SearchDelay;
        ScheduleFilter(SearchDelay);
    }

    partial void OnShowWmoChanged(bool value) => ScheduleFilter(RemainingSearchDelay());
    partial void OnShowM2Changed(bool value) => ScheduleFilter(RemainingSearchDelay());
    partial void OnSelectedBrowserTabIndexChanged(int value) => ScheduleFilter(RemainingSearchDelay());

    private TimeSpan RemainingSearchDelay() =>
        TimeSpan.FromTicks(Math.Max(0, (_searchDueAtUtc - DateTime.UtcNow).Ticks));
    partial void OnSelectedFolderChanged(ObjectBrowserFolderViewModel? value)
    {
        _visibleEntryCount = FolderPageSize;
        OnPropertyChanged(nameof(CurrentFolderPath));
        PublishCurrentFolder();
    }

    public void Activate()
    {
        if (!IsLoading && !_disposed)
            _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        IsLoading = true;
        try
        {
            var index = await _catalog.GetIndexAsync(cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested || ReferenceEquals(index, _index))
                return;
            _index = index;
            ScheduleFilter(RemainingSearchDelay());
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_disposed)
                Trace.TraceError($"Unable to load object browser catalog: {exception}");
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
                if (!_disposed)
                    IsLoading = false;
            }
            cancellation.Dispose();
        }
    }

    private void ScheduleFilter(TimeSpan delay)
    {
        _filterCancellation?.Cancel();
        if (_disposed)
            return;
        var cancellation = new CancellationTokenSource();
        _filterCancellation = cancellation;
        _ = ApplyFilterAsync(delay, cancellation);
    }

    private async Task ApplyFilterAsync(TimeSpan delay, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, token);
            token.ThrowIfCancellationRequested();
            _appliedSearchText = SearchText;
            RefreshFavorites();
            if (SelectedBrowserTabIndex != 0 || _index == null)
                return;

            IsFiltering = true;
            var selectedPath = SelectedFolder?.Path;
            var index = _index;
            var search = _appliedSearchText;
            var showWmo = ShowWmo;
            var showM2 = ShowM2;
            var snapshot = await Task.Run(
                () => BuildFilteredSnapshot(index, search, showWmo, showM2, token), token);
            if (_disposed || token.IsCancellationRequested || !ReferenceEquals(_index, index))
                return;

            _activeFilter = snapshot.Filter;
            RootFolders = [snapshot.Root];
            SelectedFolder = selectedPath != null && snapshot.FoldersByPath.TryGetValue(selectedPath, out var folder)
                ? folder
                : snapshot.Root;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_disposed)
                Trace.TraceError($"Unable to filter object browser: {exception}");
        }
        finally
        {
            if (ReferenceEquals(_filterCancellation, cancellation))
            {
                _filterCancellation = null;
                if (!_disposed)
                    IsFiltering = false;
            }
            cancellation.Dispose();
        }
    }

    private sealed record FilteredSnapshot(
        ObjectAssetFilterResult Filter,
        ObjectBrowserFolderViewModel Root,
        Dictionary<string, ObjectBrowserFolderViewModel> FoldersByPath);

    private static FilteredSnapshot BuildFilteredSnapshot(
        ObjectAssetIndex index, string search, bool showWmo, bool showM2, CancellationToken token)
    {
        var filter = index.Filter(search, showWmo, showM2, token);
        var foldersByPath = new Dictionary<string, ObjectBrowserFolderViewModel>(StringComparer.OrdinalIgnoreCase);
        ObjectBrowserFolderViewModel Build(ObjectAssetFolder source, ObjectBrowserFolderViewModel? parent)
        {
            token.ThrowIfCancellationRequested();
            var folder = new ObjectBrowserFolderViewModel(source, parent);
            foldersByPath.Add(source.Path, folder);
            folder.Children = source.Children.Where(filter.Includes)
                .Select(child => Build(child, folder))
                .ToArray();
            return folder;
        }

        return new FilteredSnapshot(filter, Build(index.Root, null), foldersByPath);
    }

    private void PublishCurrentFolder()
    {
        if (SelectedFolder is not { } folder || _activeFilter == null)
        {
            FolderEntries = [];
            HasMoreEntries = false;
            return;
        }

        var children = folder.Children;
        var files = _activeFilter.FilesIn(folder.Folder);
        var count = Math.Min(_visibleEntryCount, children.Count + files.Count);
        var entries = new object[count];
        var index = 0;
        for (; index < count && index < children.Count; index++)
            entries[index] = children[index];
        for (; index < count; index++)
        {
            var file = files[index - children.Count];
            entries[index] = CreateItem(file);
        }
        FolderEntries = entries;
        HasMoreEntries = children.Count + files.Count > count;
    }

    private ObjectBrowserItemViewModel CreateItem(ObjectAssetEntry file) =>
        new(file.Name, file.Path, file.FileDataId, file.Kind)
        {
            IsFavorite = _favoritePaths.Contains(file.Path)
        };

    private void RefreshFavorites()
    {
        var words = _appliedSearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        FavoriteResults = _favorites.Where(item =>
            words.All(word => item.Path.Contains(word, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    public void Dispose()
    {
        _disposed = true;
        _clipboard.Changed -= OnClipboardChanged;
        _loadCancellation?.Cancel();
        _filterCancellation?.Cancel();
    }
}
