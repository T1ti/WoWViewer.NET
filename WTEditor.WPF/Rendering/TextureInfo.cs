namespace WTEditor.Rendering;

class TextureInfo
{
    public Texture Texture;
    public bool IsManaged;

    public TextureInfo(Texture texture, bool isManaged)
    {
        Texture = texture;
        IsManaged = isManaged;
    }
}
