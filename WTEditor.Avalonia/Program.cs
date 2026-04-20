using System;
using Avalonia;
using Avalonia.OpenGL;
using Avalonia.Win32;
using Microsoft.Extensions.DependencyInjection;
using WTEditor.Avalonia.Util;
using WTEditor.Avalonia.ViewModels;
using static Avalonia.Win32.AngleOptions;

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
                .With(new AngleOptions
                {
                    // This needs to be OpenGL ES to start up, but we'll
                    // use a different version of OpenGL downstream.
                    GlProfiles = [new GlVersion(GlProfileType.OpenGLES, 3, 0, GlConstants.Compatibility),], // currently angle only supports gl ES 3.0 for d3d11
                    AllowedPlatformApis = [PlatformApi.DirectX11],

                    // enables wgl, BAD
                    // new GlVersion(GlProfileType.OpenGL,4,5,GlConstants.Compatibility)

                })
                .With(new Win32PlatformOptions()
                {
                    // priority in order
                    RenderingMode = [Win32RenderingMode.AngleEgl/*, Win32RenderingMode.Wgl, Win32RenderingMode.Software*/],
                    // WglProfiles = new[]
                    // {
                    //     new GlVersion(GlProfileType.OpenGL, 4, 5),
                    //     new GlVersion(GlProfileType.OpenGL, 4, 1), // last supported mac version

                    // },
                    ShouldRenderOnUIThread = false,
                    CompositionMode = [
                         Win32CompositionMode.LowLatencyDxgiSwapChain, // default was WinUIComposition
                         Win32CompositionMode.WinUIComposition,
                         Win32CompositionMode.DirectComposition
                     ]

                })
                .With(new X11PlatformOptions()
                { // linux
                    // Linux uses Glx to render Desktop OPengl
                    RenderingMode = [X11RenderingMode.Glx, /*X11RenderingMode.Software*/],
                })
                .With(new AvaloniaNativePlatformOptions()
                { // mac
                    // mac uses metal by default but can use opengl up to 4.1
                    RenderingMode = [AvaloniaNativeRenderingMode.OpenGl,],
                })
                .With(new SkiaOptions
                {
                    // Use as much memory as available, similar to WPF. This
                    // massively improves performance.
                    MaxGpuResourceSizeBytes = long.MaxValue
                })
                .LogToTrace();
    }
}
