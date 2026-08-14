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
        Services = ConfigureServices().BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IEditorSettingsStore, JsonEditorSettingsStore>();
        services.AddSingleton<EditorSession>();
        services.AddSingleton<ISettingsDialogService, SettingsDialogService>();
        services.AddSingleton<IObjectInspectorSectionProvider, TransformInspectorSectionProvider>();
        services.AddSingleton<IObjectInspectorSectionProvider, M2InspectorSectionProvider>();
        services.AddSingleton<IObjectInspectorSectionProvider, WorldModelInspectorSectionProvider>();
        services.AddSingleton<IObjectInspectorSectionProvider, TerrainInspectorSectionProvider>();
        services.AddSingleton<SelectionInspectorViewModel>();

        services.AddSingleton<Editor3DViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        return services;
    }

    public static void DisposeServices() => Services.Dispose();

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
