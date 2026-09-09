using Avalonia.Controls;
using Avalonia.Input;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Controls;

public partial class ChunkTexturePickerControl : UserControl
{
    private readonly TextureDragGesture _dragGesture = new();

    public ChunkTexturePickerControl() => InitializeComponent();

    private void TextureSlot_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: TexturePaletteItemViewModel texture } source)
            return;

        _dragGesture.Begin(source, e, texture);
    }

    private async void TextureSlot_OnPointerMoved(object? sender, PointerEventArgs e) =>
        await _dragGesture.MoveAsync(e);

    private void TextureSlot_OnPointerReleased(object? sender, PointerReleasedEventArgs e) =>
        _dragGesture.Release(e);
}
