using CommunityToolkit.Mvvm.ComponentModel;
using WTEditor.Application.Models;

namespace WTEditor.Avalonia.ViewModels;

/// <summary>
/// Stable presentation item for a frame timing row. The renderer publishes a
/// new immutable snapshot every frame; keeping the UI item alive prevents an
/// ItemsControl from recreating its text visuals on every profiler refresh.
/// </summary>
public sealed partial class ProfilerFrameStepViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private double _durationMilliseconds;
    [ObservableProperty] private FrameTimingDomain _domain;

    public ProfilerFrameStepViewModel(FrameTimingStep step) => Update(step);

    public string Description => FrameTimingCategoryCatalog.Describe(Name);

    public void Update(FrameTimingStep step)
    {
        Name = step.Name;
        DurationMilliseconds = step.DurationMilliseconds;
        Domain = step.Domain;
    }

    partial void OnNameChanged(string value) =>
        OnPropertyChanged(nameof(Description));
}

/// <summary>
/// Stable presentation item for a render-pass row in the world viewport
/// profiler.
/// </summary>
public sealed partial class ProfilerRenderPassViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private double _cpuCullingMilliseconds;
    [ObservableProperty] private double _cpuSubmissionMilliseconds;
    [ObservableProperty] private double? _gpuMilliseconds;
    [ObservableProperty] private int _drawCalls;
    [ObservableProperty] private int _submittedItems;
    [ObservableProperty] private string _submittedItemLabel = string.Empty;
    [ObservableProperty] private long _submittedIndices;

    public ProfilerRenderPassViewModel(RenderPassMetrics pass) => Update(pass);

    public long SubmittedTriangles => SubmittedIndices / 3;

    public string Description => Name switch
    {
        "World models (WMO)" => "WMO culling, command submission, draw calls, and indexed geometry for visible world-model groups.",
        "Doodads (M2)" => "M2 culling, animation evaluation, command submission, draw calls, and indexed geometry for visible doodads.",
        "Terrain (ADT)" => "Terrain culling, level-of-detail selection, command submission, draw calls, and indexed geometry for visible chunks.",
        "Liquids (MH2O)" => "Liquid culling and command submission for visible ADT and WMO liquid batches.",
        _ => "Renderer workload category."
    };

    /// <summary>
    /// Nullable GPU spans are expected while detailed timing is disabled or
    /// while the first timestamp query is still pending. Do not let a null
    /// nullable binding render as a misleading bare "ms" suffix.
    /// </summary>
    public string GpuSpanDisplay => GpuMilliseconds is { } value
        ? $"{value:F3} ms"
        : "—";

    public void Update(RenderPassMetrics pass)
    {
        Name = pass.Name;
        CpuCullingMilliseconds = pass.CpuCullingMilliseconds;
        CpuSubmissionMilliseconds = pass.CpuSubmissionMilliseconds;
        GpuMilliseconds = pass.GpuMilliseconds;
        DrawCalls = pass.DrawCalls;
        SubmittedItems = pass.SubmittedItems;
        SubmittedItemLabel = pass.SubmittedItemLabel;
        SubmittedIndices = pass.SubmittedIndices;
    }

    partial void OnSubmittedIndicesChanged(long value) =>
        OnPropertyChanged(nameof(SubmittedTriangles));

    partial void OnGpuMillisecondsChanged(double? value) =>
        OnPropertyChanged(nameof(GpuSpanDisplay));
}
