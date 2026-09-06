using Avalonia.Media;

namespace WTEditor.Avalonia.Presentation;

/// <summary>Single source for the editor's 24 px filled vector icon family.</summary>
public static class EditorIcons
{
    public static Geometry WorldMap { get; } = Geometry.Parse("M3,5 L9,3 L15,5 L21,3 L21,19 L15,21 L9,19 L3,21 Z M8,4 L10,3.5 L10,19.5 L8,19 Z M14,4.5 L16,4.5 L16,20 L14,20 Z");
    public static Geometry World { get; } = Geometry.Parse("M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 Z M11,4 C8,6 8,18 11,20 L13,20 C16,18 16,6 13,4 Z M3,11 L21,11 L21,13 L3,13 Z");
    public static Geometry Data { get; } = Geometry.Parse("M4,5 C4,2.5 20,2.5 20,5 C20,7.5 4,7.5 4,5 Z M4,8 C8,10 16,10 20,8 L20,12 C16,14 8,14 4,12 Z M4,15 C8,17 16,17 20,15 L20,19 C20,21.5 4,21.5 4,19 Z");
    public static Geometry Select { get; } = Geometry.Parse("M5,2 L20,13 L13.2,14.2 L17,21 L13.5,23 L9.7,16 L5,20 Z");
    public static Geometry Terrain { get; } = Geometry.Parse("M2,20 L8.5,8 L12,13 L15.5,7 L22,20 Z");
    public static Geometry Wmo { get; } = Geometry.Parse("M3,21 L3,9 L6,9 L6,5 L9,5 L9,9 L15,9 L15,5 L18,5 L18,9 L21,9 L21,21 L15,21 L15,15 L9,15 L9,21 Z");
    public static Geometry Doodad { get; } = Geometry.Parse("M12,2 L21,7 L21,17 L12,22 L3,17 L3,7 Z M5,8 L11,11.3 L11,19.5 L5,16 Z M13,11.3 L19,8 L19,16 L13,19.5 Z");
    public static Geometry Grid { get; } = Geometry.Parse("M3,3 H8 V8 H3 Z M10,3 H14 V8 H10 Z M16,3 H21 V8 H16 Z M3,10 H8 V14 H3 Z M10,10 H14 V14 H10 Z M16,10 H21 V14 H16 Z M3,16 H8 V21 H3 Z M10,16 H14 V21 H10 Z M16,16 H21 V21 H16 Z");
    public static Geometry Wireframe { get; } = Geometry.Parse("M3,3 H21 V5 H3 Z M3,19 H21 V21 H3 Z M3,5 H5 V19 H3 Z M19,5 H21 V19 H19 Z M4,4 L5.4,3.3 L20.7,19 L19.3,20 Z M19,4 L20.7,5 L12.7,12 L11.3,10.7 Z M4,19 L12,11 L13.4,12.4 L5,20.7 Z");
    public static Geometry Settings { get; } = Geometry.Parse("M10,2 H14 L15,5 L18,3 L21,6 L19,9 L22,10 V14 L19,15 L21,18 L18,21 L15,19 L14,22 H10 L9,19 L6,21 L3,18 L5,15 L2,14 V10 L5,9 L3,6 L6,3 L9,5 Z M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 Z");
    public static Geometry Metrics { get; } = Geometry.Parse("M3,20 V13 H7 V20 Z M10,20 V8 H14 V20 Z M17,20 V3 H21 V20 Z");
    public static Geometry Profiler { get; } = Geometry.Parse("M2,18 V11 H6 V15 H9 V7 H13 V13 H17 V4 H22 V18 Z");
    public static Geometry Close { get; } = Geometry.Parse("M4,5.5 L5.5,4 L12,10.5 L18.5,4 L20,5.5 L13.5,12 L20,18.5 L18.5,20 L12,13.5 L5.5,20 L4,18.5 L10.5,12 Z");
    public static Geometry ExpansionCircle { get; } = Geometry.Parse("M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 Z");
    public static Geometry ExpansionDiamond { get; } = Geometry.Parse("M12,2 L22,12 L12,22 L2,12 Z");
    public static Geometry ExpansionHexagon { get; } = Geometry.Parse("M12,2 L20.5,7 L20.5,17 L12,22 L3.5,17 L3.5,7 Z");
    public static Geometry ExpansionShield { get; } = Geometry.Parse("M12,2 L21,6 L19,20 L12,23 L5,20 L3,6 Z");
    public static Geometry ExpansionStar { get; } = Geometry.Parse("M12,2 L14.7,8.3 L21.5,8.9 L16.4,13.4 L18,20 L12,16.4 L6,20 L7.6,13.4 L2.5,8.9 L9.3,8.3 Z");
}
