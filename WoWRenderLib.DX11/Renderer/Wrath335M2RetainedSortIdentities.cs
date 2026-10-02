using System.Runtime.CompilerServices;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>
/// Lifetime-owned, four-byte-aligned comparator tokens. They preserve sharing,
/// batch address order and resource generations without retaining scene objects.
/// Tokens are not asset IDs or original client addresses. Allocator-dependent
/// tie order against a reference process remains a numerical/capture gate.
/// </summary>
internal static class Wrath335M2RetainedSortIdentities
{
    private sealed record Identity(uint Value);
    private sealed class MeshIdentity(Submesh[] batches)
    {
        internal readonly uint Shared = Allocate();
        internal readonly uint[] Batches = batches.Select(_ => Allocate()).ToArray();
    }

    private static long _lastIdentity;
    private static readonly ConditionalWeakTable<M2Container, Identity> Models = new();
    private static readonly ConditionalWeakTable<Submesh[], MeshIdentity> Meshes = new();

    internal static uint Allocate()
    {
        var value = Interlocked.Add(ref _lastIdentity, 4);
        if (value > int.MaxValue)
            throw new InvalidOperationException("The retained M2 sort identity range is exhausted.");
        return (uint)value;
    }

    internal static uint Model(M2Container model) =>
        Models.GetValue(model, _ => new(Allocate())).Value;

    internal static (uint Shared, uint Batch) Mesh(Submesh[] batches, int batchIndex)
    {
        var identity = Meshes.GetValue(batches, source => new(source));
        return (identity.Shared, identity.Batches[batchIndex]);
    }
}

/// <summary>
/// Tracks resolved GPU resource handles through explicit release. A reused COM
/// address receives a fresh token after retirement. Asset cache keys never enter
/// the comparator. Borrowers must retire handles before releasing their owner.
/// </summary>
internal sealed class Wrath335M2RetainedHandleIdentities
{
    private readonly Dictionary<nint, uint> _identities = [];

    internal uint Resolve(nint handle)
    {
        if (handle == 0) return 0;
        if (!_identities.TryGetValue(handle, out var identity))
        {
            identity = Wrath335M2RetainedSortIdentities.Allocate();
            _identities.Add(handle, identity);
        }
        return identity;
    }

    internal void Retire(nint handle) => _identities.Remove(handle);
    internal void Clear() => _identities.Clear();
}
