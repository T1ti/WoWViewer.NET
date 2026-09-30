namespace WoWRenderLib.DX11.Renderer;

/// <summary>0xCDD0E8 window/complement record; rectangle coordinates are 0..1.</summary>
internal readonly record struct Wrath335PortalWindow(WmoPortalRect Rect, float Distance);

/// <summary>12340 rectangle subtraction at 0x7968D0. Separate from polygon render views.</summary>
internal sealed class Wrath335PortalComplement
{
    private readonly WmoPortalRect[] bankA = new WmoPortalRect[64];
    private readonly WmoPortalRect[] bankB = new WmoPortalRect[64];
    private readonly Wrath335PortalWindow[] output = new Wrath335PortalWindow[64];
    private int count;
    public ReadOnlySpan<Wrath335PortalWindow> Views => output.AsSpan(0, count);
    public bool ReachedFragmentLimit { get; private set; }

    public void Clear()
    {
        count = 0;
        ReachedFragmentLimit = false;
    }

    public void Build(ReadOnlySpan<Wrath335PortalWindow> windows)
    {
        Clear();
        var source = bankA;
        var destination = bankB;
        source[0] = new(0f, 0f, 1f, 1f);
        var sourceCount = 1;
        foreach (var window in windows)
        {
            // 0x79696B: stop before another window when the source exceeds 60.
            if (sourceCount > 60)
            {
                ReachedFragmentLimit = true;
                break;
            }
            var destinationCount = 0;
            for (var i = 0; i < sourceCount; i++)
            {
                var rect = source[i];
                var cut = window.Rect;
                // 0x7969B6..0x7969F4: touching edges do not subtract area.
                if (rect.MaxX <= cut.MinX || rect.MinX >= cut.MaxX ||
                    rect.MaxY <= cut.MinY || rect.MinY >= cut.MaxY)
                    destination[destinationCount++] = rect;
                else
                {
                    // Native CRect is YX/YX. Emit min-Y, max-Y, min-X, max-X
                    // strips in that order, trimming the working Y band first.
                    if (rect.MinY < cut.MinY)
                    {
                        destination[destinationCount++] = rect with { MaxY = cut.MinY };
                        rect = rect with { MinY = cut.MinY };
                    }
                    if (rect.MaxY > cut.MaxY)
                    {
                        destination[destinationCount++] = rect with { MinY = cut.MaxY };
                        rect = rect with { MaxY = cut.MaxY };
                    }
                    if (rect.MinX < cut.MinX)
                    {
                        destination[destinationCount++] = rect with { MaxX = cut.MinX };
                        rect = rect with { MinX = cut.MinX };
                    }
                    if (rect.MaxX > cut.MaxX)
                        destination[destinationCount++] = rect with { MinX = cut.MaxX };
                }
                // 0x796B6F: check after all strips for one source rectangle.
                // Native stops here and drops remaining source rectangles.
                if (destinationCount > 60)
                {
                    ReachedFragmentLimit = true;
                    break;
                }
            }
            sourceCount = destinationCount;
            (source, destination) = (destination, source);
        }
        count = sourceCount;
        for (var i = 0; i < count; i++)
            output[i] = new(source[i], 0f); // 0x796BF4 clears distance, not polygon data.
    }
}
