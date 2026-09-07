namespace WoWRenderLib.DX11.Streaming;

public readonly record struct AssetPipelineMetrics(
    int Pending,
    int Active,
    long Completed,
    long Skipped,
    long Failed,
    double LastProcessingMilliseconds,
    double MaximumProcessingMilliseconds);

public readonly record struct AssetStreamingMetrics(
    AssetPipelineMetrics Adt,
    AssetPipelineMetrics Blp,
    AssetPipelineMetrics M2,
    AssetPipelineMetrics Wmo)
{
    public int Pending => Adt.Pending + Blp.Pending + M2.Pending + Wmo.Pending;
    public long Skipped => Adt.Skipped + Blp.Skipped + M2.Skipped + Wmo.Skipped;
    public long Failed => Adt.Failed + Blp.Failed + M2.Failed + Wmo.Failed;
}
