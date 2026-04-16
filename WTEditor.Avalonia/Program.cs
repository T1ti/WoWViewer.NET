using System;
using Avalonia;
using Avalonia.OpenGL;
using Microsoft.Extensions.DependencyInjection;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Views;

namespace WTEditor.Avalonia
{
    internal sealed class Program
    {
        public static IServiceProvider Services { get; private set; }
        public static IServiceScope? AppScope { get; private set; }

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            var services = new ServiceCollection();

            // Register view models
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindowViewModel>();


            Services = services.BuildServiceProvider();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .With(new Win32PlatformOptions()
                {
                    // UseWgl = true,
                    RenderingMode = [Win32RenderingMode.Wgl],
                    WglProfiles = new[]
                    {
                        new GlVersion(GlProfileType.OpenGL, 4, 5),
                        new GlVersion(GlProfileType.OpenGL, 4, 0),
                        new GlVersion(GlProfileType.OpenGL, 3, 3)
                    }
                })
                .LogToTrace();
    }
}
