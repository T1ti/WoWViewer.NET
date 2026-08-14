using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Renderer;

public static class TerrainBatching
{
    public static bool UsesHeightTextures(int layerCount, IReadOnlyList<int> heightMaterialFileDataIds)
    {
        ArgumentNullException.ThrowIfNull(heightMaterialFileDataIds);

        var activeLayerCount = Math.Clamp(layerCount, 0, heightMaterialFileDataIds.Count);
        for (var index = 0; index < activeLayerCount; index++)
        {
            if (heightMaterialFileDataIds[index] > 0)
                return true;
        }

        return false;
    }

    public static int[] BuildCompatibleRunLengths(ADTRenderBatch[] renderBatches)
    {
        ArgumentNullException.ThrowIfNull(renderBatches);

        var runLengths = new int[renderBatches.Length];
        for (var index = renderBatches.Length - 1; index >= 0; index--)
        {
            runLengths[index] = index + 1 < renderBatches.Length &&
                AreCompatible(renderBatches[index], renderBatches[index + 1])
                    ? runLengths[index + 1] + 1
                    : 1;
        }

        return runLengths;
    }

    public static int CountCompatibleContiguousChunks(
        int startVisibleIndex,
        IReadOnlyList<int> visibleChunkIndices,
        IReadOnlyList<bool> farLodSelections,
        IReadOnlyList<int> compatibleRunLengths)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startVisibleIndex);
        if (startVisibleIndex >= visibleChunkIndices.Count ||
            visibleChunkIndices.Count != farLodSelections.Count)
        {
            return 0;
        }

        var firstChunkIndex = visibleChunkIndices[startVisibleIndex];
        if ((uint)firstChunkIndex >= (uint)compatibleRunLengths.Count)
            return 0;

        var maximumRunLength = compatibleRunLengths[firstChunkIndex];
        var farLod = farLodSelections[startVisibleIndex];
        var runLength = 1;
        while (runLength < maximumRunLength &&
               startVisibleIndex + runLength < visibleChunkIndices.Count &&
               visibleChunkIndices[startVisibleIndex + runLength] == firstChunkIndex + runLength &&
               farLodSelections[startVisibleIndex + runLength] == farLod)
        {
            runLength++;
        }

        return runLength;
    }

    public static int CountCompatibleContiguousChunks(
        int startVisibleIndex,
        IReadOnlyList<int> visibleChunkIndices,
        IReadOnlyList<bool> farLodSelections,
        ADTRenderBatch[] renderBatches)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startVisibleIndex);
        if (startVisibleIndex >= visibleChunkIndices.Count ||
            visibleChunkIndices.Count != farLodSelections.Count)
        {
            return 0;
        }

        var firstChunkIndex = visibleChunkIndices[startVisibleIndex];
        if ((uint)firstChunkIndex >= (uint)renderBatches.Length)
            return 0;

        var firstBatch = renderBatches[firstChunkIndex];
        var farLod = farLodSelections[startVisibleIndex];
        var runLength = 1;
        for (var index = startVisibleIndex + 1; index < visibleChunkIndices.Count; index++)
        {
            var chunkIndex = visibleChunkIndices[index];
            if (chunkIndex != firstChunkIndex + runLength ||
                farLodSelections[index] != farLod ||
                (uint)chunkIndex >= (uint)renderBatches.Length ||
                !AreCompatible(firstBatch, renderBatches[chunkIndex]))
            {
                break;
            }

            runLength++;
        }

        return runLength;
    }

    public static bool AreCompatible(in ADTRenderBatch left, in ADTRenderBatch right) =>
        left.layerCount == right.layerCount &&
        left.usesHeightTextures == right.usesHeightTextures &&
        left.materialFDIDs.AsSpan().SequenceEqual(right.materialFDIDs) &&
        (!left.usesHeightTextures ||
         left.heightMaterialFDIDs.AsSpan().SequenceEqual(right.heightMaterialFDIDs));

    public static int GetShaderLayerCount(int actualLayerCount) => actualLayerCount switch
    {
        <= 1 => 1,
        2 => 2,
        <= 4 => 4,
        _ => 8
    };
}
