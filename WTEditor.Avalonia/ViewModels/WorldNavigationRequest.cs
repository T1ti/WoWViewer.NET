using WTEditor.Application.Geometry;

namespace WTEditor.Avalonia.ViewModels;

public sealed record WorldNavigationRequest(
    int MapId,
    uint WdtFileDataId,
    TilePoint Position,
    bool IsGlobalWmo);
