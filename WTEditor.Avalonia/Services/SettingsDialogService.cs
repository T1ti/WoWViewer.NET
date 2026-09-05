using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System.ComponentModel;
using WTEditor.Application.Models;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Views;

namespace WTEditor.Avalonia.Services;

public interface ISettingsDialogService
{
    Task<EditorSettingsSnapshot?> ShowAsync(
        EditorSettingsSnapshot settings,
        Action<EditorSettingsSnapshot>? applyPreview = null);
}

public sealed class SettingsDialogService : ISettingsDialogService
{
    public async Task<EditorSettingsSnapshot?> ShowAsync(
        EditorSettingsSnapshot settings,
        Action<EditorSettingsSnapshot>? applyPreview = null)
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow == null)
        {
            return null;
        }

        var viewModel = new ClientSettingsViewModel(settings);
        var window = new SettingsWindow { DataContext = viewModel };
        PropertyChangedEventHandler previewChanged = (_, _) =>
            applyPreview?.Invoke(viewModel.ApplyTo(settings).Normalize());
        viewModel.PropertyChanged += previewChanged;

        var wasApplied = false;
        try
        {
            wasApplied = await window.ShowDialog<bool>(desktop.MainWindow);
            return wasApplied ? viewModel.ApplyTo(settings).Normalize() : null;
        }
        finally
        {
            viewModel.PropertyChanged -= previewChanged;
            if (!wasApplied)
                applyPreview?.Invoke(settings);
        }
    }
}
