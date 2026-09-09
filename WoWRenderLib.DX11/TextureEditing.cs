using System.Numerics;

namespace WoWRenderLib.DX11;

public enum TextureBrushMode
{
    Paint,
    Smooth,
    Colour
}

public readonly record struct TextureBrushInput(
    TextureBrushMode ToolMode,
    uint SelectedTextureFileDataId,
    byte Opacity,
    float Strength);

/// <summary>A texture used by a terrain chunk, preserving its material layer order.</summary>
public readonly record struct TerrainChunkTextureLayer(int LayerIndex, uint FileDataId);

/// <summary>CPU sampling rules matching the terrain shader's packed alpha maps.</summary>
public static class TerrainAlphaMapSampler
{
    public const int Size = 64;
    private const int Channels = 4;

    public static float[] SampleWeights(byte[][]? alphaMaterials, int layerCount, Vector2 uv)
    {
        layerCount = Math.Clamp(layerCount, 0, 8);
        if (layerCount == 0)
            return [];

        var weights = new float[layerCount];
        var overlaySum = 0f;
        for (var layerIndex = 1; layerIndex < layerCount; layerIndex++)
        {
            var groupIndex = layerIndex / Channels;
            var channelIndex = layerIndex % Channels;
            var weight = SampleChannel(alphaMaterials, groupIndex, channelIndex, uv);
            weights[layerIndex] = weight;
            overlaySum += weight;
        }

        weights[0] = 1f - Math.Clamp(overlaySum, 0f, 1f);
        return weights;
    }

    public static int FindDominantLayer(
        ReadOnlySpan<float> weights,
        ReadOnlySpan<int> materialFileDataIds)
    {
        var dominantLayer = -1;
        var dominantWeight = float.NegativeInfinity;
        var count = Math.Min(weights.Length, materialFileDataIds.Length);
        for (var layerIndex = 0; layerIndex < count; layerIndex++)
        {
            if (materialFileDataIds[layerIndex] <= 0)
                continue;

            // >= intentionally gives equal opacity to the higher layer.
            if (weights[layerIndex] >= dominantWeight)
            {
                dominantWeight = weights[layerIndex];
                dominantLayer = layerIndex;
            }
        }

        return dominantLayer;
    }

    private static float SampleChannel(
        byte[][]? alphaMaterials,
        int groupIndex,
        int channelIndex,
        Vector2 uv)
    {
        if (alphaMaterials == null ||
            (uint)groupIndex >= (uint)alphaMaterials.Length ||
            alphaMaterials[groupIndex] is not { Length: >= Size * Size * Channels } pixels)
        {
            return 0f;
        }

        // D3D normalized linear sampling addresses texel centers at n + 0.5.
        // The terrain shader applies frac() before sampling each chunk map.
        var wrappedU = uv.X - MathF.Floor(uv.X);
        var wrappedV = uv.Y - MathF.Floor(uv.Y);
        var x = wrappedU * Size - 0.5f;
        var y = wrappedV * Size - 0.5f;
        var baseX = (int)MathF.Floor(x);
        var baseY = (int)MathF.Floor(y);
        var x0 = Math.Clamp(baseX, 0, Size - 1);
        var y0 = Math.Clamp(baseY, 0, Size - 1);
        var x1 = Math.Clamp(baseX + 1, 0, Size - 1);
        var y1 = Math.Clamp(baseY + 1, 0, Size - 1);
        var tx = Math.Clamp(x - baseX, 0f, 1f);
        var ty = Math.Clamp(y - baseY, 0f, 1f);

        float At(int px, int py) => pixels[((py * Size + px) * Channels) + channelIndex] / 255f;
        static float Lerp(float start, float end, float amount) => start + (end - start) * amount;
        var top = Lerp(At(x0, y0), At(x1, y0), tx);
        var bottom = Lerp(At(x0, y1), At(x1, y1), tx);
        return Lerp(top, bottom, ty);
    }
}

/// <summary>Presentation metadata for texture operations, independent of brush geometry.</summary>
public static class TextureBrushModes
{
    public static Vector4 GetPreviewColor(TextureBrushMode mode) => mode switch
    {
        TextureBrushMode.Paint => new Vector4(1f, 0.58f, 0.18f, 1f),
        TextureBrushMode.Smooth => new Vector4(0.35f, 1f, 0.55f, 1f),
        TextureBrushMode.Colour => new Vector4(1f, 0.32f, 0.72f, 1f),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported texture brush mode.")
    };
}

/// <summary>Format-independent math for normalized four-layer terrain weights.</summary>
public static class TextureLayerMath
{
    public static Vector4 Smooth(Vector4 current, Vector4 neighborhoodAverage, float amount)
    {
        current = Normalize(current);
        neighborhoodAverage = Normalize(neighborhoodAverage);
        return Normalize(Vector4.Lerp(current, neighborhoodAverage, Math.Clamp(amount, 0f, 1f)));
    }

    public static Vector4 Normalize(Vector4 weights)
    {
        weights = Vector4.Max(weights, Vector4.Zero);
        var sum = weights.X + weights.Y + weights.Z + weights.W;
        return sum > 0.000001f ? weights / sum : new Vector4(1f, 0f, 0f, 0f);
    }
}
