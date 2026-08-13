using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Views;

namespace WTEditor.Avalonia;

public partial class App : global::Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (AutomatedBenchmarkOptions.Current.Enabled)
            {
                var editor = Program.Services.GetRequiredService<Editor3DViewModel>();
                editor.AutomatedPerformanceCaptureSaved += (_, filePath) =>
                {
                    Console.WriteLine($"WTEDITOR_BENCHMARK_RESULT={filePath}");
                    Dispatcher.UIThread.Post(() => desktop.Shutdown(0));
                };
                editor.AutomatedPerformanceCaptureFailed += (_, message) =>
                {
                    Console.Error.WriteLine($"WTEDITOR_BENCHMARK_ERROR={message}");
                    Dispatcher.UIThread.Post(() => desktop.Shutdown(2));
                };
            }

            desktop.MainWindow = Program.Services.GetRequiredService<MainWindow>();
            desktop.Exit += (_, _) => Program.DisposeServices();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
