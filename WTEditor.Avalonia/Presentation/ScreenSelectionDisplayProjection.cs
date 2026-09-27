using WTEditor.Avalonia.ViewModels;
using WoWRenderLib.DX11.Editing;

namespace WTEditor.Avalonia.Presentation;

internal static class ScreenSelectionDisplayProjection
{
    public static void Publish(ScreenSelectionRectangle rectangle, Editor3DViewModel? viewModel)
    {
        if (viewModel == null) return;
        viewModel.IsScreenSelectionVisible = rectangle.IsVisible;
        viewModel.ScreenSelectionX = rectangle.Minimum.X;
        viewModel.ScreenSelectionY = rectangle.Minimum.Y;
        viewModel.ScreenSelectionWidth = rectangle.Maximum.X - rectangle.Minimum.X;
        viewModel.ScreenSelectionHeight = rectangle.Maximum.Y - rectangle.Minimum.Y;
    }
}
