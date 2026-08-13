using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using WTEditor.Application.Models;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Views;

namespace WTEditor.Avalonia.Services;

public interface ISettingsDialogService
{
    Task<EditorSettingsSnapshot?> ShowAsync(EditorSettingsSnapshot settings);
}

public sealed class SettingsDialogService : ISettingsDialogService
{
    public async Task<EditorSettingsSnapshot?> ShowAsync(EditorSettingsSnapshot settings)
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow == null)
        {
            return null;
        }

        var viewModel = new ClientSettingsViewModel(settings);
        var window = new SettingsWindow { DataContext = viewModel };

        return await window.ShowDialog<bool>(desktop.MainWindow)
            ? viewModel.ApplyTo(settings).Normalize()
            : null;
    }
}
