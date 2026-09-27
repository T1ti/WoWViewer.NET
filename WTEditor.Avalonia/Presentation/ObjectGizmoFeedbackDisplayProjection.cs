using WTEditor.Avalonia.ViewModels;
using WoWRenderLib.DX11.Editing;

namespace WTEditor.Avalonia.Presentation;

internal static class ObjectGizmoFeedbackDisplayProjection
{
    public static void Publish(ObjectGizmoFeedback feedback, Editor3DViewModel? viewModel)
    {
        if (viewModel == null) return;
        viewModel.GizmoFeedbackText = feedback.Text ?? string.Empty;
        viewModel.GizmoFeedbackX = feedback.ScreenPosition.X;
        viewModel.GizmoFeedbackY = feedback.ScreenPosition.Y;
    }
}
