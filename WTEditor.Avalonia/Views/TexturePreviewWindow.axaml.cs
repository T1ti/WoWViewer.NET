using Avalonia.Controls;
using Avalonia.Interactivity;
using WTEditor.Avalonia.Services;

namespace WTEditor.Avalonia.Views;

public partial class TexturePreviewWindow : Window
{
    private TerrainTexturePreviewImages? _ownedImages;

    public string DisplayName { get; private set; } = "Texture preview";
    public string WindowTitle => $"{DisplayName} — Texture preview";
    public string Details { get; private set; } = string.Empty;
    public TexturePreviewWindow()
    {
        InitializeWindow();
    }

    internal TexturePreviewWindow(
        string displayName,
        uint fileDataId,
        TerrainTexturePreviewImages images)
    {
        DisplayName = displayName;
        var metadata = images.Metadata;
        Details = $"{metadata.Width} × {metadata.Height}  •  BLP v{metadata.Version}  •  " +
                  $"{metadata.ColorEncoding} / {metadata.PixelFormat}  •  Alpha {metadata.AlphaDepth}-bit  •  " +
                  $"{metadata.MipCount} mipmap{(metadata.MipCount == 1 ? string.Empty : "s")}  •  " +
                  $"Mip flags {metadata.MipFlags}  •  File data ID: {fileDataId}";
        _ownedImages = images;
        InitializeWindow();
    }

    private void InitializeWindow()
    {
        InitializeComponent();
        PreviewImageControl.Source = _ownedImages?.Combined;
        Closed += (_, _) => DisposeImages();
    }

    private void Combined_OnClick(object? sender, RoutedEventArgs e) =>
        SetPreview(TexturePreviewChannelMode.Combined);

    private void Color_OnClick(object? sender, RoutedEventArgs e) =>
        SetPreview(TexturePreviewChannelMode.Color);

    private void Alpha_OnClick(object? sender, RoutedEventArgs e) =>
        SetPreview(TexturePreviewChannelMode.Alpha);

    private void SetPreview(TexturePreviewChannelMode mode)
    {
        if (_ownedImages is null || PreviewImageControl is null)
            return;
        PreviewImageControl.Source = mode switch
        {
            TexturePreviewChannelMode.Color => _ownedImages.Color,
            TexturePreviewChannelMode.Alpha => _ownedImages.Alpha,
            _ => _ownedImages.Combined
        };
    }

    private void DisposeImages()
    {
        PreviewImageControl.Source = null;
        _ownedImages?.Dispose();
        _ownedImages = null;
    }
}
