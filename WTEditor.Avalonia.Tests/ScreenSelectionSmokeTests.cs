using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.Structs;
using WoWRenderLib.Raycasting;
using WTEditor.Application;
using WTEditor.Application.Models;
using WTEditor.Application.Services;
using WTEditor.Avalonia.Presentation;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class ScreenSelectionSmokeTests
{
    [TestMethod]
    public void DisplayProjectionPublishesAndClearsRectangle()
    {
        using var viewport = new Editor3DViewModel(new EditorSession(new MemorySettingsStore()));
        ScreenSelectionDisplayProjection.Publish(new(true, new(20, 30), new(120, 230)), viewport);
        Assert.IsTrue(viewport.IsScreenSelectionVisible);
        Assert.AreEqual(20d, viewport.ScreenSelectionX);
        Assert.AreEqual(30d, viewport.ScreenSelectionY);
        Assert.AreEqual(100d, viewport.ScreenSelectionWidth);
        Assert.AreEqual(200d, viewport.ScreenSelectionHeight);
        ScreenSelectionDisplayProjection.Publish(default, viewport);
        Assert.IsFalse(viewport.IsScreenSelectionVisible);
        Assert.AreEqual(0d, viewport.ScreenSelectionWidth);
        Assert.AreEqual(0d, viewport.ScreenSelectionHeight);
    }

    [TestMethod]
    public void GestureNormalizesReverseDragsAndPublishesLogicalPixels()
    {
        var gesture = new ScreenSelectionGesture();
        var input = new InputFrame { Mode = EditorModeId.Selection, PixelScale = 2, LeftMouseDown = true, MousePosition = new(800, 600) };
        Assert.IsNull(gesture.Update(input, false, new(1000)));
        Assert.IsFalse(gesture.Rectangle.IsVisible);
        input.MousePosition = new(200, 100);
        gesture.Update(input, false, new(1000));
        Assert.IsTrue(gesture.Rectangle.IsVisible);
        Assert.AreEqual(new Vector2(100, 50), gesture.Rectangle.Minimum);
        Assert.AreEqual(new Vector2(400, 300), gesture.Rectangle.Maximum);
        input.LeftMouseDown = false;
        input.Modifiers = InputModifiers.Control;
        var request = gesture.Update(input, false, new(1000));
        Assert.IsTrue(request!.Value.IsMarquee);
        Assert.AreEqual(InputModifiers.Control, request.Value.Modifiers);
        Assert.IsFalse(gesture.Rectangle.IsVisible);
        Assert.IsFalse(gesture.HasPointerGesture);
    }

    [TestMethod]
    public void ShortClicksRemainClicksAndCancelledDragsCannotSelectOnRelease()
    {
        for (var cause = 0; cause < 5; cause++)
        {
            var gesture = new ScreenSelectionGesture();
            var input = new InputFrame { Mode = EditorModeId.Selection, LeftMouseDown = true, MousePosition = new(200) };
            gesture.Update(input, false, new(1000));
            input.MousePosition = new(cause == 0 ? 202 : 350);
            if (cause == 1) input.CancelObjectManipulation = true;
            if (cause == 2) input.RightMouseDown = true;
            if (cause == 3) input.Mode = EditorModeId.Terrain;
            gesture.Update(input, cause == 4, new(1000));
            input.LeftMouseDown = false;
            input.RightMouseDown = input.CancelObjectManipulation = false;
            input.Mode = EditorModeId.Selection;
            var result = gesture.Update(input, false, new(1000));
            if (cause == 0) Assert.IsFalse(result!.Value.IsMarquee);
            else Assert.IsNull(result);
        }
    }

    [TestMethod]
    public void VolumeRejectsOffscreenBehindCameraAndBeyondFarPlaneBounds()
    {
        var camera = new Camera(Vector3.Zero, 0, 0, 1) { FarPlane = 100 };
        var vp = camera.GetViewMatrix() * camera.GetProjectionMatrix();
        foreach (var reverse in new[] { false, true })
        {
            var volume = new ScreenSelectionVolume(new(reverse ? 550 : 450), new(reverse ? 450 : 550), new(1000), vp);
            Assert.IsTrue(volume.Intersects(Box(new(10, -1, -1), new(12, 1, 1))));
            Assert.IsFalse(volume.Intersects(Box(new(-12, -1, -1), new(-10, 1, 1))));
            Assert.IsFalse(volume.Intersects(Box(new(10, 10, 10), new(12, 12, 12))));
            Assert.IsFalse(volume.Intersects(Box(new(110, -1, -1), new(120, 1, 1))));
            Assert.IsTrue(volume.Intersects(Box(new(.5f, -.1f, -.1f), new(2, .1f, .1f))));
            Assert.IsFalse(volume.Intersects(Box(new(float.NaN), Vector3.One)));
        }
    }

    [TestMethod]
    public void MarqueeReplacesAddsAndTogglesWithoutSelectingHiddenLayersOrWmoChildren()
    {
        var a = Model(new(10, 0, 0));
        var b = Model(new(12, 0, 0));
        var outside = Model(new(10, 20, 0));
        var child = Model(new(10, 0, 0));
        child.ParentWMO = (WMOContainer)RuntimeHelpers.GetUninitializedObject(typeof(WMOContainer));
        var camera = new Camera(Vector3.Zero, 0, 0, 1);
        var selection = new ObjectSelection();
        var request = new ScreenSelectionRequest(new(400), new(600), InputModifiers.None, true);
        Container3D[] scene = [a, b, outside, child, a];
        selection.Select(outside);
        ScreenObjectSelection.Apply(scene, selection, request, camera, new(1000), true, true, 100);
        CollectionAssert.AreEquivalent(new[] { a, b }, selection.Objects.ToArray());
        Assert.IsFalse(outside.IsSelected);
        Assert.IsFalse(child.IsSelected);
        selection.Select(outside);
        ScreenObjectSelection.Apply(scene, selection, request with { Modifiers = InputModifiers.Shift }, camera, new(1000), true, true, 100);
        Assert.AreEqual(3, selection.Objects.Count);
        ScreenObjectSelection.Apply(scene, selection, request with { Modifiers = InputModifiers.Control }, camera, new(1000), true, true, 100);
        Assert.AreSame(outside, selection.Primary);
        Assert.AreEqual(1, selection.Objects.Count);
        ScreenObjectSelection.Apply(scene, selection, request, camera, new(1000), false, true, 100);
        Assert.AreEqual(0, selection.Objects.Count);
    }

    private static BoundingBox Box(Vector3 min, Vector3 max) => new() { Min = min, Max = max };
    private sealed class MemorySettingsStore : IEditorSettingsStore
    {
        public EditorSettingsSnapshot Load() => new();
        public void Save(EditorSettingsSnapshot settings) { }
    }

    private static M2Container Model(Vector3 center)
    {
        var item = (M2Container)RuntimeHelpers.GetUninitializedObject(typeof(M2Container));
        item.CachedBoundingBox = Box(center - Vector3.One * .5f, center + Vector3.One * .5f);
        item.CachedBoundingSphere = new BoundingSphere(center, 1);
        return item;
    }
}
