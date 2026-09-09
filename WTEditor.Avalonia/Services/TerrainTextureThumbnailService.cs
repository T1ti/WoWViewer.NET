using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WoWLib;
using WoWRenderLib.Services;

namespace WTEditor.Avalonia.Services;

public interface ITerrainTextureThumbnailService
{
    Task<IImage?> LoadAsync(uint fileDataId, CancellationToken cancellationToken = default);
}

/// <summary>Loads small, cached previews for terrain BLP materials.</summary>
public sealed class TerrainTextureThumbnailService : ITerrainTextureThumbnailService, IDisposable
{
    private const uint MaximumThumbnailDimension = 128;
    private readonly ConcurrentDictionary<uint, Lazy<Task<Bitmap?>>> _cache = new();

    public async Task<IImage?> LoadAsync(
        uint fileDataId,
        CancellationToken cancellationToken = default)
    {
        if (fileDataId == 0)
            return null;

        var load = _cache.GetOrAdd(
            fileDataId,
            static id => new Lazy<Task<Bitmap?>>(
                () => Task.Run(() => Load(id)),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return await load.Value.WaitAsync(cancellationToken);
    }

    private static Bitmap? Load(uint fileDataId)
    {
        try
        {
            return TerrainTextureImageLoader.Load(
                fileDataId,
                MaximumThumbnailDimension,
                TexturePreviewChannelMode.Color).Bitmap;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Terrain texture thumbnail {fileDataId}: {exception.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var load in _cache.Values)
        {
            if (load.IsValueCreated && load.Value.IsCompletedSuccessfully)
                load.Value.Result?.Dispose();
        }
        _cache.Clear();
    }
}

internal enum TexturePreviewChannelMode
{
    Combined,
    Color,
    Alpha
}

internal readonly record struct TerrainTextureMetadata(
    uint Width,
    uint Height,
    uint Version,
    string ColorEncoding,
    string PixelFormat,
    byte AlphaDepth,
    byte MipFlags,
    ulong MipCount);

internal readonly record struct TerrainTextureImage(Bitmap Bitmap, TerrainTextureMetadata Metadata);

internal sealed class TerrainTexturePreviewImages : IDisposable
{
    public required Bitmap Combined { get; init; }
    public required Bitmap Color { get; init; }
    public required Bitmap Alpha { get; init; }
    public required TerrainTextureMetadata Metadata { get; init; }

    public void Dispose()
    {
        Combined.Dispose();
        Color.Dispose();
        Alpha.Dispose();
    }
}

internal static class TerrainTextureImageLoader
{
    public static TerrainTextureImage Load(
        uint fileDataId,
        uint? maximumDimension = null,
        TexturePreviewChannelMode channelMode = TexturePreviewChannelMode.Combined)
    {
        using var blp = new WoWLib.Formats.BLP.BLP();
        blp.Read(WowlibFileSystem.Current, new FileKey(new FileDataId(fileDataId)));
        uint mip = 0;
        while (maximumDimension is { } maximum &&
               mip + 1 < blp.MipCount &&
               (blp.MipWidth(mip) > maximum || blp.MipHeight(mip) > maximum))
        {
            mip++;
        }

        using var image = blp.Decode(mip);
        var pixels = image.Pixels.AsSpan().ToArray();
        ApplyChannelMode(pixels, channelMode);
        return new TerrainTextureImage(
            CreateBitmap(pixels, image.Width, image.Height),
            CreateMetadata(blp));
    }

    public static TerrainTexturePreviewImages LoadPreview(uint fileDataId)
    {
        using var blp = new WoWLib.Formats.BLP.BLP();
        blp.Read(WowlibFileSystem.Current, new FileKey(new FileDataId(fileDataId)));
        using var image = blp.Decode(0);
        var source = image.Pixels.AsSpan().ToArray();
        var color = source.ToArray();
        var alpha = source.ToArray();
        ApplyChannelMode(color, TexturePreviewChannelMode.Color);
        ApplyChannelMode(alpha, TexturePreviewChannelMode.Alpha);

        Bitmap? combinedBitmap = null;
        Bitmap? colorBitmap = null;
        Bitmap? alphaBitmap = null;
        try
        {
            combinedBitmap = CreateBitmap(source, image.Width, image.Height);
            colorBitmap = CreateBitmap(color, image.Width, image.Height);
            alphaBitmap = CreateBitmap(alpha, image.Width, image.Height);
            return new TerrainTexturePreviewImages
            {
                Combined = combinedBitmap,
                Color = colorBitmap,
                Alpha = alphaBitmap,
                Metadata = CreateMetadata(blp)
            };
        }
        catch
        {
            combinedBitmap?.Dispose();
            colorBitmap?.Dispose();
            alphaBitmap?.Dispose();
            throw;
        }
    }

    internal static void ApplyChannelMode(Span<byte> pixels, TexturePreviewChannelMode channelMode)
    {
        if (channelMode == TexturePreviewChannelMode.Combined)
            return;

        for (var index = 0; index + 3 < pixels.Length; index += 4)
        {
            if (channelMode == TexturePreviewChannelMode.Alpha)
                pixels[index] = pixels[index + 1] = pixels[index + 2] = pixels[index + 3];
            pixels[index + 3] = byte.MaxValue;
        }
    }

    private static TerrainTextureMetadata CreateMetadata(WoWLib.Formats.BLP.BLP blp) => new(
        blp.Width,
        blp.Height,
        blp.Version,
        blp.ColorEncoding.ToString(),
        blp.PreferredFormat.ToString(),
        blp.AlphaDepth,
        blp.MipFlags,
        blp.MipCount);

    private static Bitmap CreateBitmap(byte[] pixels, uint width, uint height)
    {
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return new Bitmap(
                PixelFormat.Rgba8888,
                AlphaFormat.Unpremul,
                handle.AddrOfPinnedObject(),
                new PixelSize((int)width, (int)height),
                new Vector(96, 96),
                checked((int)width * 4));
        }
        finally
        {
            handle.Free();
        }
    }
}
