using WTEditor.Application.Services;
using WTEditor.Avalonia.ViewModels;
using WoWRenderLib.DX11;

namespace WTEditor.Avalonia.Rendering;

/// <summary>Registers completed renderer gestures in the shared editor history.</summary>
internal static class ObjectGizmoEditBridge
{
    public static void ApplyHistoryAction(WowViewerEngine? engine, Editor3DViewModel? viewModel, bool redo)
    {
        engine?.CancelObjectManipulation();
        if (engine != null)
            Publish(engine, viewModel);
        if (redo)
            viewModel?.UndoService.Redo();
        else
            viewModel?.UndoService.Undo();
    }

    public static void Publish(WowViewerEngine engine, Editor3DViewModel? viewModel)
    {
        Presentation.ObjectGizmoFeedbackDisplayProjection.Publish(engine.ObjectGizmoFeedback, viewModel);
        Presentation.ScreenSelectionDisplayProjection.Publish(engine.SelectionRectangle, viewModel);
        while (engine.TakeCompletedObjectEdit() is { } edit)
            viewModel?.RecordAppliedEdit(new DelegateEditorCommand(edit.Description,
                () => engine.ApplyObjectEdit(edit, useAfter: true),
                () => engine.ApplyObjectEdit(edit, useAfter: false)));
    }
}
