using WTEditor.Application.Geometry;

namespace WTEditor.Avalonia.ViewModels;

public sealed record WorldNavigationRequest(
    int MapId,
    string WdtPath,
    uint WdtFileDataIdHint,
    TilePoint Position,
    bool IsGlobalWmo);
