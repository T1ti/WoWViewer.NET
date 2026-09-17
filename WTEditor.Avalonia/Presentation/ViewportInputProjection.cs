using System.Numerics;
using WTEditor.Application.Models;
using WTEditor.Avalonia.ViewModels;
using WoWRenderLib.DX11;
using WoWRenderLib.DX11.Editing;

namespace WTEditor.Avalonia.Presentation;

/// <summary>
/// Translates UI input state into the renderer's per-frame input contract.
/// </summary>
internal static class ViewportInputProjection
{
    public static InputFrame Create(Editor3DViewModel? viewModel)
    {
        var keysDown = new HashSet<Silk.NET.Input.Key>();
        if (viewModel != null)
        {
            if (viewModel.Forward) keysDown.Add(Silk.NET.Input.Key.W);
            if (viewModel.Backward) keysDown.Add(Silk.NET.Input.Key.S);
            if (viewModel.Left) keysDown.Add(Silk.NET.Input.Key.A);
            if (viewModel.Right) keysDown.Add(Silk.NET.Input.Key.D);
            if (viewModel.Up) keysDown.Add(Silk.NET.Input.Key.Q);
            if (viewModel.Down) keysDown.Add(Silk.NET.Input.Key.E);
            if (viewModel.Shift) keysDown.Add(Silk.NET.Input.Key.ShiftLeft);
            if (viewModel.Ctrl) keysDown.Add(Silk.NET.Input.Key.ControlLeft);
            if (viewModel.Space) keysDown.Add(Silk.NET.Input.Key.Space);
        }

        return new InputFrame
        {
            MousePosition = viewModel?.MousePosition ?? Vector2.Zero,
            LeftMouseDown = viewModel?.LeftMouseDown ?? false,
            RightMouseDown = viewModel?.RightMouseDown ?? false,
            Mode = viewModel?.EditorMode ?? EditorModeId.Selection,
            Modifiers = (viewModel?.Shift == true ? InputModifiers.Shift : InputModifiers.None) |
                        (viewModel?.Ctrl == true ? InputModifiers.Control : InputModifiers.None),
            Brush = new BrushInput(
                (float)(viewModel?.BrushSize ?? 10),
                (float)(viewModel?.BrushFalloff ?? 0.35),
                viewModel?.BrushHasFalloff ?? true,
                viewModel?.BrushShape ?? BrushShape.Circle,
                viewModel?.BrushFalloffProfile ?? BrushFalloffProfile.Smooth),
            TerrainBrush = new TerrainBrushInput
            {
                ToolMode = (TerrainBrushMode)(viewModel?.TerrainBrushToolMode ?? 0),
                Speed = (float)(viewModel?.TerrainBrushSpeed ?? 5),
                FlattenHeight = (float)(viewModel?.TerrainFlattenHeight ?? 0),
                FlattenTarget = (TerrainFlattenTarget)(viewModel?.TerrainFlattenTarget ?? 0),
                SmoothIterations = viewModel?.TerrainSmoothIterations ?? 1
            },
            TextureBrush = new TextureBrushInput(
                (TextureBrushMode)(viewModel?.TextureBrushToolMode ?? 0),
                viewModel?.TextureBrushTextureFileDataId ?? 0,
                (byte)Math.Clamp(
                    (int)Math.Round(viewModel?.TextureBrushOpacity ?? 255),
                    0,
                    255),
                (float)(viewModel?.TextureBrushStrength ?? 1)),
            MouseWheel = viewModel?.MouseWheel ?? 0f,
            KeysDown = keysDown
        };
    }
}
