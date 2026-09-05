using System.ComponentModel;
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
        if (_isStarted && _viewport.RendererState == RendererLifecycleState.Ready)
            _ = _mapCatalogService.LoadAsync();
    }

    public void Dispose() => _viewport.PropertyChanged -= OnViewportPropertyChanged;
}
