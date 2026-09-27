using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Editing;
using WoWRenderLib.DX11.Objects;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class ObjectGizmoSmokeTests
{
    [TestMethod]
    public void PlacementCoordinatesAndRotationMatchTheRenderedModel()
    {
        foreach (var rotation in new[] { Vector3.Zero, new Vector3(23, 74, -36), new Vector3(90, 45, 20), new Vector3(-90, 45, 20) })
        {
            var item = new Container3D(default, 0, 0) { Position = new(20, 30, 40), Rotation = rotation, Scale = 1 };
            Near(ObjectGizmoMath.ToWorldPosition(item.Position), item.GetModelMatrix().Translation);
            var expected = item.GetModelMatrix();
            var recovered = ObjectGizmoMath.RotationMatrix(ObjectGizmoMath.PlacementRotation(expected));
            for (var axis = 0; axis < 3; axis++)
                Near(Vector3.TransformNormal(ObjectGizmoMath.UnitAxis(axis), expected), Vector3.TransformNormal(ObjectGizmoMath.UnitAxis(axis), recovered));
        }
    }

    [TestMethod]
    public void GroupRotationAndScaleUseSharedCenterAndPreserveUniformSizes()
    {
        var left = new PlacementTransform(ObjectGizmoMath.ToPlacementPosition(new(-2, 0, 0)), Vector3.Zero, 2);
        var right = left with { Position = ObjectGizmoMath.ToPlacementPosition(new(2, 0, 0)), Scale = 3 };
        var a = ObjectGizmoMath.Rotate(left, Vector3.Zero, Vector3.UnitZ, MathF.PI / 2);
        var b = ObjectGizmoMath.Rotate(right, Vector3.Zero, Vector3.UnitZ, MathF.PI / 2);
        Near(new(0, -2, 0), ObjectGizmoMath.ToWorldPosition(a.Position));
        Near(new(0, 2, 0), ObjectGizmoMath.ToWorldPosition(b.Position));
        Near(new(-4, 0, 0), ObjectGizmoMath.ToWorldPosition(ObjectGizmoMath.Scale(left, Vector3.Zero, 2, false).Position));
        Assert.AreEqual(6f, ObjectGizmoMath.Scale(right, Vector3.Zero, 2, false).Scale);
        Assert.AreEqual(left, ObjectGizmoMath.Scale(left, Vector3.Zero, 2, true));
    }

    [TestMethod]
    public void SelectionTogglesAndClearsEveryHighlight()
    {
        var a = new Container3D(default, 0, 0);
        var b = new Container3D(default, 0, 0);
        var selection = new ObjectSelection();
        selection.Select(a);
        selection.Select(b, additive: true);
        selection.Select(a, additive: true);
        Assert.AreEqual(2, selection.Objects.Count);
        Assert.AreSame(a, selection.Primary);
        selection.Select(a, toggle: true);
        Assert.IsFalse(a.IsSelected);
        Assert.AreSame(b, selection.Primary);
        selection.Clear();
        Assert.IsFalse(b.IsSelected);
    }

    [TestMethod]
    public void FrameUsesSelectionMeanPrimaryLocalAxesAndCameraViewAxes()
    {
        var a = new Container3D(default, 0, 0) { Position = new(0, 0, 2), Scale = 1 };
        var b = new Container3D(default, 0, 0) { Position = new(0, 0, -2), Rotation = new(15, 30, 45), Scale = 1 };
        var camera = new Camera(new(10, 10, 10), 225, -35, 1);
        Assert.IsTrue(ObjectGizmoMath.TryCreateFrame([a, b], ObjectGizmoOrientation.Global, camera, 1000, 1, out var frame));
        Near(Vector3.Zero, frame.Pivot);
        Near(Vector3.UnitX, frame.Axis(0));
        Assert.IsTrue(ObjectGizmoMath.TryCreateFrame([a, b], ObjectGizmoOrientation.Local, camera, 1000, 1, out frame));
        Near(Vector3.TransformNormal(Vector3.UnitX, b.GetModelMatrix()), frame.Axis(0));
        Assert.IsTrue(ObjectGizmoMath.TryCreateFrame([a, b], ObjectGizmoOrientation.View, camera, 1000, 1, out frame));
        Near(camera.Right, frame.Axis(0));
        Near(-camera.Front, frame.Axis(2));
    }

    [TestMethod]
    public void MoveGestureEditsTheWholeSelectionAndProducesOneStableUndoBatch()
    {
        var (selection, controller, camera) = CreateGesture();
        var before = selection.Objects.Select(PlacementTransform.Capture).ToArray();
        var input = Input(new(500, 500));
        controller.Update(input, selection, camera, 1000, 1000, false);
        input.LeftMouseDown = true;
        Assert.IsTrue(controller.Update(input, selection, camera, 1000, 1000, false));
        Assert.IsTrue(controller.IsDragging);
        input.MousePosition += new Vector2(80, -20);
        controller.Update(input, selection, camera, 1000, 1000, false);
        var a = selection.Objects[0].Position - before[0].Position;
        var b = selection.Objects[1].Position - before[1].Position;
        Assert.IsTrue(a.Length() > .1f);
        Near(a, b);
        input.LeftMouseDown = false;
        Assert.IsTrue(controller.Update(input, selection, camera, 1000, 1000, false), "Release belongs to the gizmo, not click selection.");
        var edit = controller.TakeCompletedEdit();
        Assert.IsNotNull(edit);
        Assert.AreEqual(2, edit.Entries.Length);
        Assert.IsNull(controller.TakeCompletedEdit());
        var targets = selection.Objects.ToArray();
        selection.Clear();
        foreach (var entry in edit.Entries)
            Apply(entry.Target, entry.Before);
        Assert.AreEqual(before[0], PlacementTransform.Capture(targets[0]));
        foreach (var entry in edit.Entries)
            Apply(entry.Target, entry.After);
        Assert.AreEqual(edit.Entries[0].After, PlacementTransform.Capture(targets[0]));
    }

    [TestMethod]
    public void CancellationModeSwitchAndSelectionChangeRestoreEveryPlacement()
    {
        for (var cause = 0; cause < 4; cause++)
        {
            var (selection, controller, camera) = CreateGesture();
            var targets = selection.Objects.ToArray();
            var before = targets.Select(PlacementTransform.Capture).ToArray();
            var input = Input(new(500, 500));
            input.LeftMouseDown = true;
            controller.Update(input, selection, camera, 1000, 1000, false);
            input.MousePosition += new Vector2(70, 10);
            controller.Update(input, selection, camera, 1000, 1000, false);
            if (cause == 0) input.CancelObjectManipulation = true;
            if (cause == 1) input.RightMouseDown = true;
            if (cause == 2) input.Mode = EditorModeId.Terrain;
            if (cause == 3) selection.Select(targets[0]);
            Assert.IsTrue(controller.Update(input, selection, camera, 1000, 1000, false));
            Assert.IsFalse(controller.IsDragging);
            Assert.IsNull(controller.TakeCompletedEdit());
            for (var i = 0; i < targets.Length; i++)
                Assert.AreEqual(before[i], PlacementTransform.Capture(targets[i]));
        }
    }

    [TestMethod]
    public void ScaleGesturePreservesRatiosAndCanUndoWithoutMovement()
    {
        var (selection, controller, camera) = CreateGesture();
        selection.Objects[0].Scale = 2;
        selection.Objects[1].Scale = 3;
        var input = Input(new(500, 500), ObjectGizmoMode.Scale);
        input.LeftMouseDown = true;
        controller.Update(input, selection, camera, 1000, 1000, false);
        Assert.IsTrue(controller.IsDragging);
        input.MousePosition += new Vector2(40, -40);
        controller.Update(input, selection, camera, 1000, 1000, false);
        Assert.IsTrue(selection.Objects[0].Scale > 2);
        Assert.AreEqual(1.5f, selection.Objects[1].Scale / selection.Objects[0].Scale, .0001f);
        Assert.AreEqual(selection.Objects[0].Scale, -ObjectGizmoMath.ToWorldPosition(selection.Objects[0].Position).X, .001f);
        controller.Cancel();
        Assert.AreEqual(2f, selection.Objects[0].Scale);
        input.LeftMouseDown = false;
        controller.Update(input, selection, camera, 1000, 1000, false);
        input.MousePosition = new(500, 500);
        input.LeftMouseDown = true;
        controller.Update(input, selection, camera, 1000, 1000, false);
        Assert.IsTrue(controller.IsDragging);
        input.LeftMouseDown = false;
        controller.Update(input, selection, camera, 1000, 1000, false);
        Assert.IsNull(controller.TakeCompletedEdit(), "An unchanged click must not add history.");
    }

    [TestMethod]
    public void RotationRingGestureMovesObjectsAroundTheSharedPivot()
    {
        var (selection, controller, camera) = CreateGesture();
        camera.Position = new(0, 0, 20);
        camera.SetDirection(new(0, .01f, -1));
        ObjectGizmoMath.TryCreateFrame(selection.Objects, ObjectGizmoOrientation.Global, camera, 1000, 1, out var frame);
        var vp = camera.GetViewMatrix() * camera.GetProjectionMatrix();
        var start = Vector3.Normalize(new Vector3(1, 1, 0)) * (.85f * frame.Size);
        var end = Vector3.Transform(start, Matrix4x4.CreateRotationZ(.5f));
        ObjectGizmoMath.Project(start, vp, new(1000), out var a);
        ObjectGizmoMath.Project(end, vp, new(1000), out var b);
        var input = Input(a, ObjectGizmoMode.Rotate);
        input.LeftMouseDown = true;
        controller.Update(input, selection, camera, 1000, 1000, false);
        Assert.IsTrue(controller.IsDragging);
        Assert.AreEqual(GizmoHandle.Z, controller.Handle);
        input.MousePosition = b;
        controller.Update(input, selection, camera, 1000, 1000, false);
        Near(new(-2 * MathF.Cos(.5f), -2 * MathF.Sin(.5f), 0), ObjectGizmoMath.ToWorldPosition(selection.Objects[0].Position));
        input.LeftMouseDown = false;
        controller.Update(input, selection, camera, 1000, 1000, false);
        Assert.AreEqual(2, controller.TakeCompletedEdit()!.Entries.Length);
    }

    [TestMethod]
    public void PickingAndScreenSizeStayAlignedAtHighDpiAndDifferentDistances()
    {
        var (selection, _, camera) = CreateGesture();
        foreach (var dpi in new[] { 1f, 1.5f, 2f })
        {
            var viewport = new Vector2(1000 * dpi);
            ObjectGizmoMath.TryCreateFrame(selection.Objects, ObjectGizmoOrientation.Global, camera, (int)viewport.Y, dpi, out var frame);
            var vp = camera.GetViewMatrix() * camera.GetProjectionMatrix();
            ObjectGizmoMath.Project(frame.Pivot + frame.Axis(0) * frame.Size * .7f, vp, viewport, out var point);
            Assert.AreEqual(GizmoHandle.X, ObjectGizmoGeometry.Move.HitTest(frame, ObjectGizmoMode.Move, point, vp, viewport, dpi, out _));
            camera.Position *= 2;
            ObjectGizmoMath.TryCreateFrame(selection.Objects, ObjectGizmoOrientation.Global, camera, (int)viewport.Y, dpi, out var distant);
            Assert.AreEqual(frame.Size * 2, distant.Size, .001f);
        }
    }

    [TestMethod]
    public void ClassicWmoScaleAndTerrainSelectionNeverStartTransformGestures()
    {
        var (selection, controller, camera) = CreateGesture();
        var wmo = (WMOContainer)RuntimeHelpers.GetUninitializedObject(typeof(WMOContainer));
        selection.Select(wmo, additive: true);
        controller.Update(Input(new(500), ObjectGizmoMode.Scale), selection, camera, 1000, 1000, true);
        Assert.IsFalse(controller.IsVisible);
        controller.Update(Input(new(500), ObjectGizmoMode.Move), selection, camera, 1000, 1000, true);
        Assert.IsTrue(controller.IsVisible);
        selection.Select(new Container3D(default, 0, 0));
        controller.Update(Input(new(500)), selection, camera, 1000, 1000, false);
        Assert.IsFalse(controller.IsVisible);
    }

    internal static (ObjectSelection Selection, ObjectGizmoController Controller, Camera Camera) CreateGesture()
    {
        var selection = new ObjectSelection();
        foreach (var x in new[] { -2f, 2f })
        {
            var item = (M2Container)RuntimeHelpers.GetUninitializedObject(typeof(M2Container));
            item.Position = ObjectGizmoMath.ToPlacementPosition(new(x, 0, 0));
            item.Scale = 1;
            selection.Select(item, additive: true);
        }
        var camera = new Camera(new(12, 10, 9), 0, 0, 1);
        camera.SetDirection(-camera.Position);
        return (selection, new ObjectGizmoController(Apply), camera);
    }

    internal static InputFrame Input(Vector2 pointer, ObjectGizmoMode mode = ObjectGizmoMode.Move) => new()
    {
        MousePosition = pointer, Mode = EditorModeId.Selection, GizmoMode = mode, PixelScale = 1
    };

    private static void Apply(Container3D item, PlacementTransform value)
    {
        item.Position = value.Position;
        item.Rotation = value.Rotation;
        item.Scale = value.Scale;
    }

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.IsTrue(Vector3.Distance(expected, actual) < .002f, $"Expected {expected}, got {actual}");
}
