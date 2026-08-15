namespace WoWRenderLib.DX11.Editing;

/// <summary>
/// The active viewport interaction mode. A single value prevents contradictory
/// input states such as selection and terrain editing both being enabled.
/// </summary>
public enum EditorModeId
{
    Selection,
    Terrain
}
