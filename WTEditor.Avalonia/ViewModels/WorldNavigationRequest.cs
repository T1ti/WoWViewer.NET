using WTEditor.Application.Geometry;

namespace WTEditor.Avalonia.ViewModels;

public sealed record WorldNavigationRequest(
    uint WdtFileDataId,
    TilePoint Position,
    bool IsGlobalWmo);
