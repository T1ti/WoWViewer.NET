using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using WTEditor.Application;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Views;

namespace WTEditor.Avalonia;

internal static class Program
{
    public static ServiceProvider Services { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        WoWRenderLib.Listfile.EnsureLoadedAsync().GetAwaiter().GetResult();
        Services = ConfigureServices().BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IProjectStore, JsonProjectStore>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IFileExplorerService, FileExplorerService>();
        services.AddSingleton<IProjectSelectionDialogService, ProjectSelectionDialogService>();
        services.AddSingleton<IEditorSettingsStore, ProjectEditorSettingsStore>();
        services.AddSingleton<UndoService>();
        services.AddSingleton<EditorSession>();
        services.AddSingleton<ISettingsDialogService, SettingsDialogService>();
        services.AddSingleton<IMinimapService, MinimapService>();
        services.AddSingleton<ITerrainTextureThumbnailService, TerrainTextureThumbnailService>();
        services.AddSingleton<ITerrainTexturePreviewService, TerrainTexturePreviewService>();
        services.AddSingleton<IClientFileCatalogService, ClientFileCatalogService>();
        services.AddSingleton<IMapCatalogService, MapCatalogService>();
        services.AddSingleton<IMapTerrainMetadataCacheService, MapTerrainMetadataCacheService>();
        services.AddSingleton<WorldMapStartupPreloader>();
        services.AddSingleton<IObjectInspectorSectionProvider, TerrainInspectorSectionProvider>();
        services.AddSingleton<SelectionInspectorViewModel>();
        services.AddSingleton<TerrainEditingViewModel>();
        services.AddSingleton<TextureEditingViewModel>();

        services.AddSingleton<Editor3DViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<WorldSelectionViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<ProjectSelectionViewModel>();
        services.AddTransient<MainWindow>();
        services.AddTransient<ProjectSelectionWindow>();

        return services;
    }

    public static void DisposeServices() => Services.Dispose();

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
