using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WTEditor.Application;
using WTEditor.Application.Commands;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Services;
using WTEditor.Avalonia.Rendering;
using WTEditor.Avalonia.Presentation;
using WTEditor.Avalonia.Controls;
using WTEditor.Avalonia.ViewModels;
using WTEditor.Avalonia.Views;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Raycasting;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Loaders;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;
using WoWRenderLib.Services;

namespace WTEditor.Avalonia.Tests;

public sealed partial class EditorSettingsSmokeTests
{
    [TestMethod]
    public void PerformanceAnalyzer_ClassifiesCpuGpuAndBalancedFrames()
    {
        Assert.AreEqual(
            PerformanceBottleneck.Cpu,
            PerformanceAnalyzer.Classify(cpuMilliseconds: 12, gpuMilliseconds: 5));
        Assert.AreEqual(
            PerformanceBottleneck.Gpu,
            PerformanceAnalyzer.Classify(cpuMilliseconds: 4, gpuMilliseconds: 11));
        Assert.AreEqual(
            PerformanceBottleneck.Balanced,
            PerformanceAnalyzer.Classify(cpuMilliseconds: 10, gpuMilliseconds: 10.5));
        Assert.AreEqual(
            PerformanceBottleneck.Unknown,
            PerformanceAnalyzer.Classify(cpuMilliseconds: 10, gpuMilliseconds: null));
    }

    [TestMethod]
    public void PerformanceAnalyzer_UsesRecentComparableSamplesForStableDiagnosis()
    {
        var samples = Enumerable.Range(1, 35)
            .Select(index => new FrameProfileSnapshot(
                index,
                DateTimeOffset.UtcNow,
                16.6,
                index <= 5 ? 50 : 4,
                index <= 5 ? 2 : 12,
                Array.Empty<FrameTimingStep>(),
                0,
                0,
                0))
            .ToArray();

        Assert.AreEqual(PerformanceBottleneck.Gpu, PerformanceAnalyzer.ClassifyRecent(samples));
    }

    [TestMethod]
    public void PerformanceAnalyzer_DetectsCpuSubmissionStarvation()
    {
        var samples = Enumerable.Range(1, 30)
            .Select(index => new FrameProfileSnapshot(
                index,
                DateTimeOffset.UtcNow,
                80,
                75,
                68,
                Array.Empty<FrameTimingStep>(),
                9_500,
                0,
                0)
            {
                RenderWorkload = new RenderWorkloadMetrics(
                    [new RenderPassMetrics("Terrain", 3, 67, null, 9_500, 9_500, "draws")],
                    0,
                    0,
                    0,
                    0)
            })
            .ToArray();

        Assert.IsTrue(PerformanceAnalyzer.IsLikelyCpuSubmissionStarved(samples));
        Assert.IsFalse(PerformanceAnalyzer.IsLikelyCpuSubmissionStarved(
            samples.Select(sample => sample with { CpuFrameMilliseconds = 5, GpuFrameMilliseconds = 4 })));
    }

    [TestMethod]
    public void PerformanceWorkload_ReportsIndexedTrianglesAndExplicitCullReasons()
    {
        var snapshot = new FrameProfileSnapshot(
            1, DateTimeOffset.UtcNow, 16, 5, 4,
            Array.Empty<FrameTimingStep>(), 2, 30, 0);
        var pass = new RenderPassMetrics("M2", 1, 1, 1, 2, 4, "instances", 21);
        var culling = new CullingMetrics(
            10, 100,
            5, 20,
            8, 50,
            SizeCulledWorldModels: 3,
            SizeCulledDoodads: 7);

        Assert.AreEqual(10, snapshot.SubmittedTriangles);
        Assert.AreEqual(7, pass.SubmittedTriangles);
        Assert.AreEqual(90, culling.CulledTerrainChunks);
        Assert.AreEqual(12, culling.CulledWorldModels);
        Assert.AreEqual(35, culling.CulledDoodads);
    }

    [TestMethod]
    public void PerformanceCaptureAnalyzer_ComputesInterpolatedPercentiles()
    {
        var distribution = PerformanceCaptureAnalyzer.Summarize(
            Enumerable.Range(1, 100).Select(value => (double)value));

        Assert.AreEqual(100, distribution.SampleCount);
        Assert.AreEqual(50.5d, distribution.Mean, 0.0001d);
        Assert.AreEqual(50.5d, distribution.Median, 0.0001d);
        Assert.AreEqual(95.05d, distribution.P95, 0.0001d);
        Assert.AreEqual(99.01d, distribution.P99, 0.0001d);
        Assert.AreEqual(100d, distribution.Maximum, 0.0001d);
    }

    [TestMethod]
    public void PerformanceCaptureAnalyzer_PreservesRawSamplesAndGroupsSteps()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            new FrameProfileSnapshot(
                1, startedAt, 10, 4, 6,
                [new FrameTimingStep("Terrain drawing (GPU)", 5, FrameTimingDomain.Gpu)],
                10, 100, 0) { EngineFrameMilliseconds = 4.5 },
            new FrameProfileSnapshot(
                2, startedAt.AddMilliseconds(10), 10, 5, 8,
                [new FrameTimingStep("Terrain drawing (GPU)", 7, FrameTimingDomain.Gpu)],
                12, 120, 0) { EngineFrameMilliseconds = 5.5 }
        };
        var context = new PerformanceCaptureContext(
            "terrain-only", 1920, 1080, Vector3.Zero, Vector3.UnitX,
            true, false, false, true, 20_000, 20_000, 4);

        var capture = PerformanceCaptureAnalyzer.Create(
            startedAt,
            startedAt.AddSeconds(10),
            context,
            samples);

        Assert.AreEqual(2, capture.Samples.Count);
        Assert.AreEqual(1, capture.Summary.Steps.Count);
        Assert.AreEqual(6d, capture.Summary.Steps[0].DurationMilliseconds.Median, 0.0001d);
        Assert.AreEqual(7d, capture.Summary.GpuFrameMilliseconds.Median, 0.0001d);
    }

    [TestMethod]
    public void AutomatedBenchmarkOptions_ReadAndClampEnvironmentValues()
    {
        var values = new Dictionary<string, string>
        {
            ["WTEDITOR_BENCHMARK"] = "true",
            ["WTEDITOR_BENCHMARK_MINIMUM_LOAD_SECONDS"] = "2.5",
            ["WTEDITOR_BENCHMARK_STABLE_FRAMES"] = "0",
            ["WTEDITOR_BENCHMARK_WARMUP_SECONDS"] = "3",
            ["WTEDITOR_BENCHMARK_CAPTURE_SECONDS"] = "12.5",
            ["WTEDITOR_BENCHMARK_TIMEOUT_SECONDS"] = "900"
        };

        var options = AutomatedBenchmarkOptions.FromEnvironment(name =>
            values.TryGetValue(name, out var value) ? value : null);

        Assert.IsTrue(options.Enabled);
        Assert.AreEqual(2.5d, options.MinimumLoadDuration.TotalSeconds, 0.001d);
        Assert.AreEqual(1, options.StableFrameCount);
        Assert.AreEqual(3d, options.WarmupDuration.TotalSeconds, 0.001d);
        Assert.AreEqual(12.5d, options.CaptureDuration.TotalSeconds, 0.001d);
        Assert.AreEqual(900d, options.Timeout.TotalSeconds, 0.001d);
    }

    [TestMethod]
    public void AutomatedBenchmarkCoordinator_WaitsForIdleStableWorldWorkload()
    {
        var options = new AutomatedBenchmarkOptions(
            true,
            TimeSpan.FromSeconds(1),
            StableFrameCount: 2,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30));
        var coordinator = new AutomatedBenchmarkCoordinator(options);
        var startedAt = DateTimeOffset.UtcNow;

        Assert.AreEqual(
            AutomatedBenchmarkAction.None,
            coordinator.Observe(CreateBenchmarkSnapshot(startedAt, pendingAssets: 1)));
        Assert.AreEqual(
            AutomatedBenchmarkAction.None,
            coordinator.Observe(CreateBenchmarkSnapshot(startedAt.AddSeconds(1), drawCalls: 100)));
        Assert.AreEqual(1, coordinator.StableFrames);

        // A changing workload restarts the consecutive stable-frame window.
        Assert.AreEqual(
            AutomatedBenchmarkAction.None,
            coordinator.Observe(CreateBenchmarkSnapshot(startedAt.AddSeconds(1.1), drawCalls: 101)));
        Assert.AreEqual(1, coordinator.StableFrames);
        Assert.AreEqual(
            AutomatedBenchmarkAction.StartCapture,
            coordinator.Observe(CreateBenchmarkSnapshot(startedAt.AddSeconds(1.2), drawCalls: 101)));
    }

    [TestMethod]
    public void AutomatedBenchmarkCoordinator_TimesOutWithoutWorldWorkload()
    {
        var options = new AutomatedBenchmarkOptions(
            true,
            TimeSpan.Zero,
            StableFrameCount: 1,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(10));
        var coordinator = new AutomatedBenchmarkCoordinator(options);
        var startedAt = DateTimeOffset.UtcNow;

        Assert.AreEqual(
            AutomatedBenchmarkAction.None,
            coordinator.Observe(CreateBenchmarkSnapshot(startedAt, drawCalls: 0, hasWorld: false)));
        Assert.AreEqual(
            AutomatedBenchmarkAction.Timeout,
            coordinator.Observe(CreateBenchmarkSnapshot(startedAt.AddSeconds(10), drawCalls: 0, hasWorld: false)));
    }

}
