using Avalonia.Controls;
using Avalonia.Input;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Controls;

public partial class TextureSelectionControl : UserControl
{
    public TextureSelectionControl()
    {
        InitializeComponent();
        DragDrop.AddDragOverHandler(ActiveTextureDropTarget, ActiveTexture_OnDragOver);
        DragDrop.AddDragLeaveHandler(ActiveTextureDropTarget, ActiveTexture_OnDragLeave);
        DragDrop.AddDropHandler(ActiveTextureDropTarget, ActiveTexture_OnDrop);
        DragDrop.AddDragOverHandler(FavoritesList, Favorites_OnDragOver);
        DragDrop.AddDragLeaveHandler(FavoritesList, Favorites_OnDragLeave);
        DragDrop.AddDropHandler(FavoritesList, Favorites_OnDrop);
    }

    private void ActiveTexture_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && DataContext is TextureEditingViewModel editor)
            editor.ShowTextureBrowserCommand.Execute(null);
    }

    private void ActiveTexture_OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TextureDragDrop.Read(e) == null ? DragDropEffects.None : DragDropEffects.Copy;
        ActiveTextureDropTarget.Classes.Set("drag-over", e.DragEffects != DragDropEffects.None);
    }

    private void ActiveTexture_OnDragLeave(object? sender, DragEventArgs e) => ResetDropTarget();

    private void ActiveTexture_OnDrop(object? sender, DragEventArgs e)
    {
        var texture = TextureDragDrop.Read(e);
        if (texture != null && DataContext is TextureEditingViewModel editor)
        {
            editor.SelectTextureCommand.Execute(texture);
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        ResetDropTarget();
    }

    private void ResetDropTarget() => ActiveTextureDropTarget.Classes.Remove("drag-over");

    private void Favorites_OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TextureDragDrop.Read(e) == null ? DragDropEffects.None : DragDropEffects.Copy;
        FavoritesList.Background = global::Avalonia.Media.Brush.Parse("#3324669A");
    }

    private void Favorites_OnDragLeave(object? sender, DragEventArgs e) => ResetFavoritesDropTarget();

    private void Favorites_OnDrop(object? sender, DragEventArgs e)
    {
        var texture = TextureDragDrop.Read(e);
        if (texture != null && DataContext is TextureEditingViewModel editor)
        {
            editor.AddFavoriteCommand.Execute(texture);
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        ResetFavoritesDropTarget();
    }

    private void ResetFavoritesDropTarget() =>
        FavoritesList.Background = global::Avalonia.Media.Brushes.Transparent;
}
