using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.Services;
using WTEditor.Application.Services;
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
            var projects = Program.Services.GetRequiredService<IProjectService>();

            var shouldShowProjectSelection = !AutomatedBenchmarkOptions.Current.Enabled;
            string? startupProjectError = null;

            if (!AutomatedBenchmarkOptions.Current.Enabled &&
                projects.AutoLoadLastProject &&
                projects.LastProjectId is { } lastProjectId)
            {
                try
                {
                    shouldShowProjectSelection = !projects.SelectProject(lastProjectId);
                    if (shouldShowProjectSelection)
                        startupProjectError = "The last loaded project could not be found.";
                }
                catch (Exception exception)
                {
                    shouldShowProjectSelection = true;
                    startupProjectError = exception.Message;
                }
            }

            if (AutomatedBenchmarkOptions.Current.Enabled)
            {
                if (projects.Projects.Count == 0)
                {
                    var benchmarkFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "WTEditor",
                        "AutomatedProject");
                    projects.AddProject("Automated benchmark", benchmarkFolder);
                }

                projects.SelectProject(projects.Projects[0].Id);
            }

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

            if (AutomatedBenchmarkOptions.Current.Enabled || !shouldShowProjectSelection)
            {
                Program.Services.GetRequiredService<WorldMapStartupPreloader>().Start();
                desktop.MainWindow = Program.Services.GetRequiredService<MainWindow>();
            }
            else
            {
                var selectionWindow = Program.Services.GetRequiredService<ProjectSelectionWindow>();
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                selectionWindow.ProjectOpened += async (_, _) =>
                {
                    try
                    {
                        Program.Services.GetRequiredService<WorldMapStartupPreloader>().Start();
                        var mainWindow = Program.Services.GetRequiredService<MainWindow>();
                        desktop.MainWindow = mainWindow;
                        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                        mainWindow.Show();
                        selectionWindow.Close();
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine($"Unable to open project: {exception}");
                        await new ErrorMessageWindow(
                            "Unable to open project",
                            exception.Message).ShowDialog<bool>(selectionWindow);
                    }
                };
                if (startupProjectError != null)
                    selectionWindow.Opened += async (_, _) =>
                        await new ErrorMessageWindow(
                            "Unable to load last project",
                            startupProjectError).ShowDialog<bool>(selectionWindow);
                desktop.MainWindow = selectionWindow;
            }

            desktop.Exit += (_, _) => Program.DisposeServices();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
