using System.ComponentModel;
using System.Diagnostics;
using WTEditor.Application.Models;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Services;

/// <summary>
/// Starts the client map and WDT-header cache as soon as renderer content is
/// ready, so opening World Selection never becomes the trigger for this work.
/// </summary>
public sealed class WorldMapStartupPreloader : IDisposable
{
    private readonly IMapCatalogService _mapCatalogService;
    private readonly Editor3DViewModel _viewport;
    private bool _isStarted;
    private bool _isDisposed;

    public WorldMapStartupPreloader(
        IMapCatalogService mapCatalogService,
        Editor3DViewModel viewport)
    {
        _mapCatalogService = mapCatalogService;
        _viewport = viewport;
        _viewport.PropertyChanged += OnViewportPropertyChanged;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _isStarted = true;
        PreloadIfReady();
    }

    private void OnViewportPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(Editor3DViewModel.RendererState))
            PreloadIfReady();
    }

    private void PreloadIfReady()
    {
        if (_isStarted && !_isDisposed && _viewport.RendererState == RendererLifecycleState.Ready)
            _ = ObservePreloadAsync(_mapCatalogService);
    }

    private static async Task ObservePreloadAsync(IMapCatalogService mapCatalogService)
    {
        try
        {
            await mapCatalogService.LoadAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Unable to preload the world map catalog: {exception}");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _viewport.PropertyChanged -= OnViewportPropertyChanged;
        GC.SuppressFinalize(this);
    }
}
