using Avalonia;

namespace WTEditor.Avalonia.Views;

internal enum ObjectDockEdge
{
    Left,
    Right,
    Bottom
}

internal static class ObjectWindowDocking
{
    public static ObjectDockEdge? FindTarget(
        PixelPoint pointer, PixelRect workspace, int snapDistance)
    {
        ObjectDockEdge? target = null;
        var nearest = snapDistance + 1;

        void Consider(ObjectDockEdge edge, int distance)
        {
            if (distance > snapDistance || distance >= nearest)
                return;
            target = edge;
            nearest = distance;
        }

        if (pointer.X < workspace.X || pointer.X >= workspace.Right ||
            pointer.Y < workspace.Y || pointer.Y >= workspace.Bottom)
            return null;

        Consider(ObjectDockEdge.Left, pointer.X - workspace.X);
        Consider(ObjectDockEdge.Right, workspace.Right - pointer.X);
        Consider(ObjectDockEdge.Bottom, workspace.Bottom - pointer.Y);

        return target;
    }
}
