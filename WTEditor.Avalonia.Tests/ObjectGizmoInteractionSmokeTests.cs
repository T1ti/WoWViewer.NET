using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WoWRenderLib.DX11.Editing;

namespace WTEditor.Avalonia.Tests;

[TestClass]
public sealed class ObjectGizmoInteractionSmokeTests
{
    [TestMethod]
    public void OverlappingPlaneAndArrowPickTheVisibleSurfaceFromEitherSide()
    {
        var plane = new Vector3(.33f, .33f, 0);
        var arrow = new Vector3(0, 0, .8f);
        var frame = new GizmoFrame(Vector3.Zero, Matrix4x4.Identity, 1);
        foreach (var side in new[] { 1, -1 })
        {
            var camera = new Camera(plane + (plane - arrow) * (side * 30), 0, 0, 1);
            camera.SetDirection(-camera.Position);
            var vp = camera.GetViewMatrix() * camera.GetProjectionMatrix();
            ObjectGizmoMath.Project(plane, vp, new(1000), out var screen);
            var hit = ObjectGizmoGeometry.Move.HitTest(frame, ObjectGizmoMode.Move, screen, vp, new(1000), 1, out _);
            Assert.AreEqual(side == 1 ? GizmoHandle.XY : GizmoHandle.Z, hit,
                "The nearer plane wins from below; the nearer arrow wins from above.");
        }
    }

    [TestMethod]
    public void HiddenRingHalvesCannotBeSelectedButOuterCircleCan()
    {
        var (selection, _, camera) = ObjectGizmoSmokeTests.CreateGesture();
        ObjectGizmoMath.TryCreateFrame(selection.Objects, ObjectGizmoOrientation.Global, camera, 1000, 1, out var frame);
        var vp = camera.GetViewMatrix() * camera.GetProjectionMatrix();
        var view = GizmoView.Create(frame, vp);
        for (var axis = 0; axis < 3; axis++)
        {
            var normal = ObjectGizmoMath.UnitAxis(axis);
            var back = -Vector3.Normalize(view.CameraLocal - normal * Vector3.Dot(view.CameraLocal, normal)) * .85f;
            var handle = (GizmoHandle)(axis + 1);
            Assert.IsFalse(view.IsVisible(back, handle, ObjectGizmoMode.Rotate, true));
            Assert.IsTrue(view.IsVisible(-back, handle, ObjectGizmoMode.Rotate, true));
            ObjectGizmoMath.Project(Vector3.Transform(back, frame.World), vp, new(1000), out var screen);
            Assert.AreNotEqual(handle, ObjectGizmoGeometry.Rotate.HitTest(frame, ObjectGizmoMode.Rotate, screen, vp, new(1000), 1, out _));
        }
        var outer = Vector3.Transform(view.Vertex(new(1.08f, 0, 0), GizmoHandle.ViewRotate), frame.World);
        ObjectGizmoMath.Project(outer, vp, new(1000), out var point);
        Assert.AreEqual(GizmoHandle.ViewRotate, ObjectGizmoGeometry.Rotate.HitTest(frame, ObjectGizmoMode.Rotate, point, vp, new(1000), 1, out _));
    }

    [TestMethod]
    public void OuterCircleRotatesTheGroupAroundTheViewAxisAndReportsAngle()
    {
        var (selection, controller, camera) = ObjectGizmoSmokeTests.CreateGesture();
        var initial = selection.Objects.Select(PlacementTransform.Capture).ToArray();
        ObjectGizmoMath.TryCreateFrame(selection.Objects, ObjectGizmoOrientation.Global, camera, 1000, 1, out var frame);
        var vp = camera.GetViewMatrix() * camera.GetProjectionMatrix();
        var view = GizmoView.Create(frame, vp);
        var start = Vector3.Transform(view.Vertex(new(1.08f, 0, 0), GizmoHandle.ViewRotate), frame.World);
        var end = Vector3.Transform(start, Matrix4x4.CreateFromAxisAngle(-camera.Front, .7f));
        ObjectGizmoMath.Project(start, vp, new(1000), out var a);
        ObjectGizmoMath.Project(end, vp, new(1000), out var b);
        var input = ObjectGizmoSmokeTests.Input(a, ObjectGizmoMode.Rotate);
        input.LeftMouseDown = true;
        controller.Update(input, selection, camera, 1000, 1000, false);
        Assert.AreEqual(GizmoHandle.ViewRotate, controller.Handle);
        Assert.IsTrue(controller.IsDragging);
        input.MousePosition = b;
        controller.Update(input, selection, camera, 1000, 1000, false);
        var expected = ObjectGizmoMath.Rotate(initial[0], frame.Pivot, -camera.Front, .7f);
        Assert.IsTrue(Vector3.Distance(expected.Position, selection.Objects[0].Position) < .002f);
        StringAssert.Contains(controller.Feedback.Text!, "View:");
        StringAssert.Contains(controller.Feedback.Text!, "40.11");
        var guide = new GizmoDragGeometry();
        guide.Build(controller.DragVisual, vp);
        Assert.IsTrue(guide.Vertices.Any(vertex => vertex.W == (float)GizmoHandle.IndicatorFill));
        controller.Cancel();
        Assert.IsNull(controller.Feedback.Text);
        Assert.AreEqual(initial[0], PlacementTransform.Capture(selection.Objects[0]));
    }

    [TestMethod]
    public void CombinedHandlesStartTheMatchingOperationAndRecordItsUndoDescription()
    {
        foreach (var handle in new[] { GizmoHandle.X, GizmoHandle.RotateZ, GizmoHandle.ScaleX, GizmoHandle.ScaleCenter })
        {
            var (selection, controller, camera) = ObjectGizmoSmokeTests.CreateGesture();
            var input = ObjectGizmoSmokeTests.Input(new(-100), ObjectGizmoMode.Transform);
            controller.Update(input, selection, camera, 1000, 1000, false);
            var vp = camera.GetViewMatrix() * camera.GetProjectionMatrix();
            var point = FindHandle(controller.Frame, vp, handle);
            input.MousePosition = point;
            input.LeftMouseDown = true;
            controller.Update(input, selection, camera, 1000, 1000, false);
            Assert.IsTrue(controller.IsDragging, handle.ToString());
            Assert.AreEqual(handle, controller.Handle);
            var expectedMode = handle == GizmoHandle.X ? ObjectGizmoMode.Move : handle == GizmoHandle.RotateZ ? ObjectGizmoMode.Rotate : ObjectGizmoMode.Scale;
            Assert.AreEqual(expectedMode, controller.Operation);
            input.MousePosition += new Vector2(17, -19);
            controller.Update(input, selection, camera, 1000, 1000, false);
            Assert.IsFalse(string.IsNullOrEmpty(controller.Feedback.Text));
            var guides = new GizmoDragGeometry();
            guides.Build(controller.DragVisual, vp);
            Assert.IsTrue(guides.Vertices.Count > 0 && guides.Vertices.Count < 4096);
            input.LeftMouseDown = false;
            controller.Update(input, selection, camera, 1000, 1000, false);
            var edit = controller.TakeCompletedEdit();
            Assert.IsNotNull(edit);
            StringAssert.StartsWith(edit.Description, expectedMode.ToString());
            Assert.IsNull(controller.Feedback.Text);
        }
    }

    [TestMethod]
    public void CombinedModeCanSuppressScaleWithoutSuppressingMoveAndRotate()
    {
        var (selection, _, camera) = ObjectGizmoSmokeTests.CreateGesture();
        ObjectGizmoMath.TryCreateFrame(selection.Objects, ObjectGizmoOrientation.Global, camera, 1000, 1, out var frame);
        var vp = camera.GetViewMatrix() * camera.GetProjectionMatrix();
        var scale = FindHandle(frame, vp, GizmoHandle.ScaleX);
        var hidden = ObjectGizmoGeometry.Transform.HitTest(frame, ObjectGizmoMode.Transform, scale, vp, new(1000), 1, out _, false);
        Assert.IsFalse(GizmoView.IsScale(hidden));
        foreach (var handle in new[] { GizmoHandle.X, GizmoHandle.RotateZ })
        {
            var point = FindHandle(frame, vp, handle);
            Assert.AreEqual(handle, ObjectGizmoGeometry.Transform.HitTest(frame, ObjectGizmoMode.Transform, point, vp, new(1000), 1, out _, false));
        }
    }

    private static Vector2 FindHandle(GizmoFrame frame, Matrix4x4 vp, GizmoHandle handle)
    {
        var geometry = ObjectGizmoGeometry.Transform;
        var view = GizmoView.Create(frame, vp);
        for (var index = 0; index < geometry.Vertices.Length; index += 3)
        {
            if (geometry.Vertices[index].W != (float)handle) continue;
            var center = Vector3.Zero;
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = geometry.Vertices[index + corner];
                center += view.Vertex(new(vertex.X, vertex.Y, vertex.Z), handle) / 3;
            }
            if (ObjectGizmoMath.Project(Vector3.Transform(center, frame.World), vp, new(1000), out var screen) &&
                geometry.HitTest(frame, ObjectGizmoMode.Transform, screen, vp, new(1000), 1, out _) == handle)
                return screen;
        }
        Assert.Fail($"No visible pickable surface for {handle}.");
        return default;
    }
}
