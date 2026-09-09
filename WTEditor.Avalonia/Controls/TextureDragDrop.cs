using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Controls;

internal static class TextureDragDrop
{
    public static event Action<TexturePaletteItemViewModel?>? ActiveTextureChanged;

    private static readonly DataFormat<TexturePaletteItemViewModel> TextureFormat =
        DataFormat.CreateInProcessFormat<TexturePaletteItemViewModel>("wteditor.terrain-texture");

    public static async Task<DragDropEffects> StartAsync(
        PointerPressedEventArgs eventArgs,
        TexturePaletteItemViewModel texture)
    {
        var transfer = new DataTransfer();
        var item = DataTransferItem.Create(TextureFormat, texture);
        if (texture.Thumbnail is Bitmap bitmap)
            item.SetBitmap(bitmap);
        transfer.Add(item);
        ActiveTextureChanged?.Invoke(texture);
        try
        {
            return await DragDrop.DoDragDropAsync(eventArgs, transfer, DragDropEffects.Copy);
        }
        finally
        {
            ActiveTextureChanged?.Invoke(null);
        }
    }

    public static TexturePaletteItemViewModel? Read(DragEventArgs eventArgs) =>
        eventArgs.DataTransfer.TryGetValue(TextureFormat);
}

/// <summary>Recognizes a texture drag using the platform's native pointer movement threshold.</summary>
internal sealed class TextureDragGesture
{
    private Control? _source;
    private PointerPressedEventArgs? _pressEvent;
    private IPointer? _pointer;
    private Point _origin;
    private TexturePaletteItemViewModel? _texture;

    public bool Begin(Control source, PointerPressedEventArgs eventArgs, TexturePaletteItemViewModel texture)
    {
        if (eventArgs.GetCurrentPoint(source).Properties.PointerUpdateKind !=
            PointerUpdateKind.LeftButtonPressed)
            return false;

        Cancel();
        _source = source;
        _pressEvent = eventArgs;
        _pointer = eventArgs.Pointer;
        _origin = eventArgs.GetPosition(source);
        _texture = texture;
        eventArgs.Pointer.Capture(source);
        return true;
    }

    public async Task<bool> MoveAsync(PointerEventArgs eventArgs)
    {
        if (_source == null || _pressEvent == null || _texture == null ||
            !ReferenceEquals(eventArgs.Pointer, _pointer))
            return false;

        if (!eventArgs.GetCurrentPoint(_source).Properties.IsLeftButtonPressed)
        {
            Cancel();
            return false;
        }

        var threshold = _source.GetPlatformSettings()?.GetTapSize(eventArgs.Pointer.Type)
                        ?? new Size(4, 4);
        if (!HasExceededThreshold(_origin, eventArgs.GetPosition(_source), threshold))
            return false;

        var pressEvent = _pressEvent;
        var texture = _texture;
        Clear(releaseCapture: true);
        await TextureDragDrop.StartAsync(pressEvent, texture);
        return true;
    }

    public bool Release(PointerReleasedEventArgs eventArgs)
    {
        if (_pressEvent == null || !ReferenceEquals(eventArgs.Pointer, _pointer))
            return false;

        var isClick = eventArgs.InitialPressMouseButton == MouseButton.Left;
        Clear(releaseCapture: true);
        return isClick;
    }

    public void Cancel() => Clear(releaseCapture: true);

    internal static bool HasExceededThreshold(Point origin, Point current, Size threshold) =>
        Math.Abs(current.X - origin.X) >= Math.Max(1d, threshold.Width) ||
        Math.Abs(current.Y - origin.Y) >= Math.Max(1d, threshold.Height);

    private void Clear(bool releaseCapture)
    {
        var pointer = _pointer;
        _source = null;
        _pressEvent = null;
        _pointer = null;
        _texture = null;
        if (releaseCapture)
            pointer?.Capture(null);
    }
}
