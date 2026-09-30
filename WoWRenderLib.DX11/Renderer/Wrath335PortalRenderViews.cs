using System.Numerics;
using System.Runtime.InteropServices;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>A 12340 render view: unit-viewport bounds and a retained projected polygon.</summary>
internal readonly record struct Wrath335PortalRenderView(
    WmoPortalRect Rect, int FirstVertex, int VertexCount);

/// <summary>Reusable polygon storage for the separate 0x795D20 render-view list.</summary>
internal sealed class Wrath335PortalRenderViews
{
    private readonly List<Wrath335PortalRenderView> views = [];
    private readonly List<Vector3> vertices = [];
    public ReadOnlySpan<Wrath335PortalRenderView> Views => CollectionsMarshal.AsSpan(views);

    public ReadOnlySpan<Vector3> Polygon(in Wrath335PortalRenderView view) =>
        CollectionsMarshal.AsSpan(vertices).Slice(view.FirstVertex, view.VertexCount);

    public void Clear()
    {
        views.Clear();
        vertices.Clear();
    }

    public void Add(WmoPortalRect unitRect, ReadOnlySpan<Vector3> polygon)
    {
        views.Add(new(unitRect, vertices.Count, polygon.Length));
        foreach (var vertex in polygon)
            vertices.Add(vertex);
    }

    public void Append(Wrath335PortalRenderViews source)
    {
        foreach (var view in source.Views)
            Add(view.Rect, source.Polygon(view));
    }
}

/// <summary>0x7AC060 exterior depth-zero candidates, blockers and forwarding.</summary>
internal sealed class Wrath335ExteriorPortalViews
{
    private readonly Wrath335PortalRenderViews candidates = new();
    private readonly List<WmoPortalRect> blockers = [];
    public Wrath335PortalRenderViews Forwarded { get; } = new();

    public void Reset()
    {
        BeginSeed();
        Forwarded.Clear();
    }

    public void BeginSeed()
    {
        candidates.Clear();
        blockers.Clear();
    }

    // Back-facing links at depth zero add [0,0,1,1], even if projection failed.
    public void AddBlocker() => blockers.Add(new(0f, 0f, 1f, 1f));

    public void AddCandidate(WmoPortalRect ndcRect, ReadOnlySpan<Vector3> polygon)
    {
        if (polygon.Length < 3)
            return;
        // 0x7A92F3..0x7A933F converts only rectangle X/Y, not polygon vertices.
        candidates.Add(new((ndcRect.MinX + 1f) * 0.5f, (ndcRect.MinY + 1f) * 0.5f,
            (ndcRect.MaxX + 1f) * 0.5f, (ndcRect.MaxY + 1f) * 0.5f), polygon);
    }

    public void EndSeed()
    {
        foreach (var view in candidates.Views)
        {
            var blocked = false;
            foreach (var blocker in blockers)
            {
                // 0x7AC630: touching edges overlap; no epsilon or subtraction.
                var rect = view.Rect;
                if (rect.MaxX < blocker.MinX || rect.MaxY < blocker.MinY ||
                    rect.MinX > blocker.MaxX || rect.MinY > blocker.MaxY)
                    continue;
                blocked = true;
                break;
            }
            if (!blocked)
                Forwarded.Add(view.Rect, candidates.Polygon(view));
        }
        BeginSeed();
    }
}
