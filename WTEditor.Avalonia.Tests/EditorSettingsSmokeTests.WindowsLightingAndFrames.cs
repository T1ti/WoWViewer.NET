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
    public void MainView_XamlCanBePopulatedAtRuntime()
    {
        var view = new MainView();

        Assert.IsNotNull(view);
        Assert.AreEqual(7d, view.FindControl<Thumb>("InspectorLeftResizeHandle")!.Width);
        Assert.AreEqual(7d, view.FindControl<Thumb>("InspectorRightResizeHandle")!.Width);
        Assert.AreEqual(7d, view.FindControl<Thumb>("InspectorBottomResizeHandle")!.Height);
    }

    [TestMethod]
    public void MainWindow_ProvidesThreeNonClosableWorkspaceTabsInRequestedOrder()
    {
        var window = new MainWindow();
        var tabs = window.FindControl<TabControl>("WorkspaceTabs");

        Assert.IsNotNull(tabs);
        var items = tabs.Items.Cast<TabItem>().ToArray();
        CollectionAssert.AreEqual(
            new[] { "World Selection", "Main Editor", "Data Tools" },
            items.Select(tab => tab.Header).Cast<string>().ToArray());
    }

    [TestMethod]
    public void LightingPanel_XamlCanBePopulatedAtRuntime()
    {
        var panel = new LightingPanel();

        Assert.IsNotNull(panel.FindControl<Border>("LightingPanelRoot"));
    }

    [TestMethod]
    public void LightingWindow_IsResizable()
    {
        var window = new LightingWindow();

        Assert.IsTrue(window.CanResize);
        Assert.AreEqual(640d, window.MinWidth);
        Assert.AreEqual(420d, window.MinHeight);
    }

    [TestMethod]
    public void LightingViewModel_SynchronizesRendererStateWithoutEchoingAndPublishesEdits()
    {
        var expected = new LightingSettingsSnapshot(
            12,
            1537,
            Vector3.Normalize(new Vector3(-0.5f, -0.5f, 1f)),
            new Vector3(0.1f, 0.2f, 0.3f),
            new Vector3(0.8f, 0.7f, 0.6f),
            new Vector3(0.05f, 0.3f, 0.5f),
            new Vector3(0.01f, 0.1f, 0.2f),
            new Vector3(0.2f, 0.4f, 0.1f),
            new Vector3(0.1f, 0.2f, 0.15f),
            0.7f,
            0.9f,
            0.75f,
            1f,
            true,
            true,
            true);
        var viewModel = new LightingViewModel();
        LightingSettingsSnapshot? published = null;
        viewModel.Changed += (_, lighting) => published = lighting;

        // Control initialization must not overwrite the client profile with
        // the view model's placeholder alpha values.
        viewModel.OceanDeepAlpha = 0.25f;
        Assert.IsNull(published);

        viewModel.Update(expected);

        Assert.IsNull(published);
        Assert.AreEqual("Dynamic LightData parameter 12, time 1537", viewModel.ProfileDescription);
        Assert.AreEqual(expected.OceanCloseColor.Z, viewModel.OceanCloseB);

        // The disabled slider may write its nearest tick back after the
        // renderer update. This must remain a display echo, not a request to
        // switch the engine to manual/static lighting.
        viewModel.Time = 1560;
        Assert.IsNull(published);
        Assert.IsTrue(viewModel.IsDynamic);

        // A bound ColorPicker writes its 8-bit display color back even though
        // the renderer snapshot retains greater float precision. That UI
        // round-trip must not be mistaken for a manual override.
        viewModel.AmbientColor = viewModel.AmbientColor;
        Assert.IsNull(published);
        Assert.IsTrue(viewModel.IsDynamic);

        // All renderer-owned fields must reject delayed control writes while
        // live lighting is active, not only the time slider and ColorPickers.
        viewModel.DirectionX = -0.25f;
        Assert.IsNull(published);
        Assert.IsTrue(viewModel.IsDynamic);

        // Manual overrides become available only through the explicit live
        // lighting toggle, so a delayed field echo cannot make this transition.
        viewModel.IsDynamic = false;
        Assert.IsNotNull(published);
        Assert.IsFalse(published.IsDynamic);
        published = null;

        viewModel.OceanCloseColor = Color.FromRgb(26, 128, 204);

        Assert.IsNotNull(published);
        Assert.AreEqual(26f / 255f, published.OceanCloseColor.X, 0.0001f);
        Assert.AreEqual(128f / 255f, published.OceanCloseColor.Y, 0.0001f);
        Assert.AreEqual(204f / 255f, published.OceanCloseColor.Z, 0.0001f);
        Assert.AreEqual(0.75f, published.OceanShallowAlpha);
        Assert.IsFalse(published.IsDynamic);
    }

    [TestMethod]
    public void LightingPanel_ColorPickerPublishesTheCompleteClientLightingSnapshot()
    {
        var session = new EditorSession(new MemorySettingsStore(new EditorSettingsSnapshot()));
        using var viewModel = new Editor3DViewModel(session);
        var expected = new LightingSettingsSnapshot(
            12,
            1440,
            Vector3.UnitZ,
            new Vector3(0.1f),
            new Vector3(0.2f),
            new Vector3(0.1f, 0.2f, 0.3f),
            new Vector3(0.2f, 0.3f, 0.4f),
            new Vector3(0.3f, 0.4f, 0.5f),
            new Vector3(0.4f, 0.5f, 0.6f),
            0.5f,
            1f,
            0.75f,
            1f,
            true,
            true,
            false);
        viewModel.UpdateActiveLighting(expected);
        var panel = new LightingPanel { DataContext = viewModel };
        LightingSettingsSnapshot? published = null;
        viewModel.LightingSettingsChanged += (_, lighting) => published = lighting;

        Assert.IsTrue(panel.FindControl<Slider>("LightingTimeSlider")!.IsSnapToTickEnabled);

        Assert.AreEqual(0.5d, panel.FindControl<CompactNumberBox>(
            "WaterShallowAlphaBox")!.Value, 0.0001d);
        Assert.AreEqual(0.75d, panel.FindControl<CompactNumberBox>(
            "OceanShallowAlphaBox")!.Value, 0.0001d);

        panel.FindControl<ColorPicker>("OceanCloseColorPicker")!
            .SetCurrentValue(ColorPicker.ColorProperty, Color.FromRgb(32, 96, 224));

        Assert.IsNotNull(published);
        Assert.AreEqual(32f / 255f, published.OceanCloseColor.X, 0.0001f);
        Assert.AreEqual(96f / 255f, published.OceanCloseColor.Y, 0.0001f);
        Assert.AreEqual(224f / 255f, published.OceanCloseColor.Z, 0.0001f);
        Assert.AreEqual(0.5f, published.WaterShallowAlpha, 0.0001f);
        Assert.AreEqual(0.75f, published.OceanShallowAlpha, 0.0001f);
    }

    [TestMethod]
    public void LightingProjection_RoundTripsEveryActiveRendererValue()
    {
        var renderer = new WorldLightingSettings(
            12,
            1440,
            Vector3.UnitZ,
            new Vector3(0.1f),
            new Vector3(0.2f),
            new Vector3(0.3f),
            new Vector3(0.4f),
            new Vector3(0.5f),
            new Vector3(0.6f),
            0.7f,
            0.8f,
            0.75f,
            1f,
            true,
            true,
            true);

        var roundTrip = LightingSettingsProjection.ToRenderer(
            LightingSettingsProjection.ToDisplay(renderer));

        Assert.AreEqual(renderer, roundTrip);
    }

    [TestMethod]
    public void ViewportFrameRatePolicy_AppliesCapExactlyAndSuspendsAtOneFps()
    {
        Assert.IsNull(
            ViewportFrameRatePolicy.GetFrameIntervalSeconds(
                100,
                ViewportRenderActivity.Foreground,
                isForegroundFrameRateLimitEnabled: false));
        Assert.AreEqual(0.01d,
            ViewportFrameRatePolicy.GetFrameIntervalSeconds(
                100,
                ViewportRenderActivity.Foreground,
                isForegroundFrameRateLimitEnabled: true)!.Value,
            0.0001d);
        Assert.IsNull(
            ViewportFrameRatePolicy.GetFrameIntervalSeconds(
                100,
                ViewportRenderActivity.Background,
                isForegroundFrameRateLimitEnabled: false));
        Assert.AreEqual(0.01d,
            ViewportFrameRatePolicy.GetFrameIntervalSeconds(
                100,
                ViewportRenderActivity.Background,
                isForegroundFrameRateLimitEnabled: true)!.Value,
            0.0001d);
        Assert.AreEqual(1d,
            ViewportFrameRatePolicy.GetFrameIntervalSeconds(
                100,
                ViewportRenderActivity.Suspended,
                isForegroundFrameRateLimitEnabled: false)!.Value,
            0.0001d);
    }

    [TestMethod]
    public void ViewportFrameClock_DoesNotConsumeDeadlineUntilFrameIsPresented()
    {
        var clock = new ViewportFrameClock(dueToleranceSeconds: 0);
        const double interval = 0.01d;

        Assert.IsTrue(clock.IsFrameDue(1d, interval));
        clock.MarkFramePresented(1d, interval);

        // A due callback with no free presentation buffer does not call
        // MarkFramePresented. The next callback must therefore remain due.
        Assert.IsTrue(clock.IsFrameDue(1.01d, interval));
        Assert.IsTrue(clock.IsFrameDue(1.015d, interval));

        clock.MarkFramePresented(1.015d, interval);
        Assert.IsFalse(clock.IsFrameDue(1.019d, interval));
        Assert.IsTrue(clock.IsFrameDue(1.02d, interval));
    }

    [TestMethod]
    public void ViewportFrameClock_DisablingCapClearsExistingDeadline()
    {
        var clock = new ViewportFrameClock(dueToleranceSeconds: 0);

        clock.MarkFramePresented(1d, frameIntervalSeconds: 0.01d);

        Assert.IsTrue(clock.IsFrameDue(1.001d, frameIntervalSeconds: null));
        Assert.IsTrue(clock.IsFrameDue(1.001d, frameIntervalSeconds: 0.01d));
    }

    [TestMethod]
    public void StreamingBudgetUsesRemainingFrameTimeAndFpsCap()
    {
        Assert.AreEqual(
            9d,
            StreamingFrameBudget.CalculateMilliseconds(
                frameIntervalSeconds: 0.01d,
                elapsedBeforeRenderMilliseconds: 0d,
                estimatedRenderMilliseconds: 0d),
            0.001d);
        Assert.AreEqual(
            3d,
            StreamingFrameBudget.CalculateMilliseconds(
                frameIntervalSeconds: 0.01d,
                elapsedBeforeRenderMilliseconds: 1d,
                estimatedRenderMilliseconds: 5d),
            0.001d);
        Assert.AreEqual(
            0d,
            StreamingFrameBudget.CalculateMilliseconds(
                frameIntervalSeconds: 0.01d,
                elapsedBeforeRenderMilliseconds: 3d,
                estimatedRenderMilliseconds: 8d),
            0.001d);
        Assert.AreEqual(
            3d,
            StreamingFrameBudget.CalculateMilliseconds(
                frameIntervalSeconds: 0.01d,
                elapsedBeforeRenderMilliseconds: 3d,
                estimatedRenderMilliseconds: 8d,
                hasPendingWork: true),
            0.001d);
        Assert.AreEqual(
            10d,
            StreamingFrameBudget.CalculateMilliseconds(
                frameIntervalSeconds: 1d / 60d,
                elapsedBeforeRenderMilliseconds: 0d,
                estimatedRenderMilliseconds: 0d),
            0.01d);
        Assert.IsTrue(double.IsFinite(StreamingFrameBudget.CalculateMilliseconds(
            frameIntervalSeconds: double.NaN,
            elapsedBeforeRenderMilliseconds: double.NaN,
            estimatedRenderMilliseconds: double.PositiveInfinity)));
    }

}
