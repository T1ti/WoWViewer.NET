using Microsoft.VisualStudio.TestTools.UnitTesting;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Loaders;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Objects;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;
using WoWRenderLib.Services;

namespace WoWRenderLib.DX11.Tests;

[TestClass]
[DoNotParallelize]
public sealed class Wrath335LiquidQueueTests
{
    [TestMethod]
    public void NativeListsUseMaterialFlagAndUnsignedIdentityInsteadOfDepthOrAssetOrder()
    {
        var material = Material(0, Vector4.One, 0);
        Assert.AreEqual(WorldLiquidDrawPhase.Early, Wrath335LiquidInstances.Phase(material));
        Assert.AreEqual(WorldLiquidDrawPhase.Late, Wrath335LiquidInstances.Phase(material with { WmoMaterialFlags = 3 }));
        Assert.AreEqual(WorldLiquidDrawPhase.Early, Wrath335LiquidInstances.Phase(material with { WmoMaterialFlags = 2 }));
        Assert.AreEqual(WorldLiquidDrawPhase.Late, Wrath335LiquidInstances.Phase(material with { Wrath335 = null }));
        var keys = new List<WorldLiquidSortKey>
        {
            new(0, 999, 0, 0, 0, WorldLiquidDrawPhase.Late, 0xfffffffcu),
            new(1, -999, 0, 0, 0, WorldLiquidDrawPhase.Late, 4),
            new(2, -1000, 0, 0, 0, WorldLiquidDrawPhase.Early, 12)
        };
        keys.Sort(WorldLiquidBatchOrdering.Compare);
        CollectionAssert.AreEqual(new[] { 2, 1, 0 }, keys.Select(key => key.VisibleIndex).ToArray());
    }

    [TestMethod]
    public void PlacementAndBatchGenerationOwnIdentitiesAcrossCameraChangesAndReloads()
    {
        var identities = new Wrath335LiquidInstances();
        var first = new object(); var second = new object();
        ParsedWorldLiquidBatch[] shared = [default, default];
        var a = identities.Get(first, shared, 0);
        var b = identities.Get(first, shared, 1);
        var c = identities.Get(second, shared, 0);
        Assert.IsTrue(a < b && b < c);
        Assert.AreEqual(a, identities.Get(first, shared, 0));
        Assert.AreNotEqual(a, identities.Get(first, shared.ToArray(), 0));
        Assert.AreNotEqual(a, identities.Get(new object(), shared, 0));
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 32; i++) identities.Get(first, shared, i & 1);
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(false, true)]
    public unsafe void PreparedListsSubmitEachInstanceOnceAndComposePixelsAcrossFlushes(bool split, bool sharedPlacements)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("DX11 WARP requires Windows.");
        using var d3d = D3D11.GetApi();
        ComPtr<ID3D11Device> device = default;
        ComPtr<ID3D11DeviceContext> context = default;
        SilkMarshal.ThrowHResult(d3d.CreateDevice(default(ComPtr<IDXGIAdapter>), D3DDriverType.Warp,
            default, 0, null, 0, D3D11.SdkVersion, ref device, null, ref context));
        ComPtr<ID3D11Texture2D> target = default, readback = default, surface = default;
        ComPtr<ID3D11RenderTargetView> targetView = default;
        ComPtr<ID3D11ShaderResourceView> surfaceView = default;
        WorldLiquidResources resources = default;
        var cache = (ConcurrentDictionary<uint, ComPtr<ID3D11ShaderResourceView>>)typeof(BLPCache)
            .GetField("Cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var key = uint.MaxValue;
        while (cache.ContainsKey(key)) key--;
        var wmoCache = (Dictionary<uint, WorldModel>)typeof(WMOCache)
            .GetField("Cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var wmoDevice = typeof(WMOCache).GetField("cachedDevice", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previousWmoDevice = wmoDevice.GetValue(null);
        var wmoKey = uint.MaxValue;
        while (wmoCache.ContainsKey(wmoKey)) wmoKey--;
        WMOContainer? first = null, second = null;
        try
        {
            using var shaders = new ShaderManager(device, Path.Combine(AppContext.BaseDirectory, "Shaders"));
            using var renderer = new WorldLiquidRenderer(device, context);
            renderer.Initialize(shaders);
            var description = new Texture2DDesc
            {
                Width = 1, Height = 1, MipLevels = 1, ArraySize = 1,
                Format = Format.FormatR32G32B32A32Float, SampleDesc = new(1, 0),
                Usage = Usage.Default, BindFlags = (uint)BindFlag.ShaderResource
            };
            var black = new Vector4(0, 0, 0, 1);
            var data = new SubresourceData { PSysMem = &black, SysMemPitch = 16 };
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, in data, ref surface));
            SilkMarshal.ThrowHResult(device.CreateShaderResourceView(surface, null, ref surfaceView));
            Assert.IsTrue(cache.TryAdd(key, surfaceView)); // Borrowed for this fixture only.
            description.Width = description.Height = 16;
            description.BindFlags = (uint)BindFlag.RenderTarget;
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref target));
            SilkMarshal.ThrowHResult(device.CreateRenderTargetView(target, null, ref targetView));
            description.Usage = Usage.Staging; description.BindFlags = 0; description.CPUAccessFlags = (uint)CpuAccessFlag.Read;
            SilkMarshal.ThrowHResult(device.CreateTexture2D(in description, null, ref readback));
            var bounds = new BoundingBox(new(-20, -20, 0), new(20, 20, 0));
            resources = WorldLiquidLoader.Upload(device, new ParsedWorldLiquid
            {
                Vertices = [new() { Position = new(-20, -20, 0) }, new() { Position = new(-20, 20, 0) },
                    new() { Position = new(20, 20, 0) }, new() { Position = new(20, -20, 0) }],
                Indices = [0, 1, 2, 0, 2, 3],
                Batches = sharedPlacements ? [Batch(0, bounds)] : [Batch(0, bounds), Batch(1, bounds)],
                Materials = [Material(split ? 0u : 1u, new(.4f, 0, 0, .5f), key), Material(1, new(0, 0, .4f, .5f), key)]
            }, 0);
            var tile = new ADTContainer(device, new MapTile { TileX = 32, TileY = 32 });
            tile.OnLoaded(new() { worldLiquid = resources });
            var camera = new Camera(new(0, -8, 8), 90, -45, 1) { FarPlane = 100 };
            var lighting = WorldLightingSettings.Defaults with { HasLiquidAlphaData = true, WaterShallowAlpha = 1, WaterDeepAlpha = 1 };
            var frame = new WorldLiquidFrame(camera, 0, Vector3.UnitZ, Vector3.One,
                Vector3.Zero, lighting, Vector3.Zero, false);
            var viewport = new Viewport { Width = 16, Height = 16, MaxDepth = 1 };
            context.RSSetViewports(1, in viewport);
            context.OMSetRenderTargets(1, ref targetView, default(ComPtr<ID3D11DepthStencilView>));
            var clear = Vector4.Zero;
            context.ClearRenderTargetView(targetView, (float*)&clear);
            WmoLiquidInstance[] wmoSources = [];
            if (sharedPlacements)
            {
                wmoCache.Add(wmoKey, new()
                {
                    rootWMOFileDataID = wmoKey, wrath335 = true, doodadSets = [],
                    groupBatches = [new() { liquid = resources, boundingBox = bounds }]
                });
                first = new(device, wmoKey, 0) { ModelMatrix = Matrix4x4.Identity };
                second = new(device, wmoKey, 0) { ModelMatrix = Matrix4x4.Identity };
                wmoSources = [new(first, 0), new(first, 0), new(second, 0)];
            }
            var prepared = renderer.Prepare(camera, sharedPlacements ? [] : [tile, tile], wmoSources,
                new HashSet<int>(), 100, 100);
            Assert.AreEqual(sharedPlacements ? 3 : 4, prepared.CandidateBatches);
            Assert.AreEqual(2, prepared.VisibleBatches);
            var early = renderer.RenderPrepared(frame, WorldLiquidDrawPhase.Early);
            Assert.AreEqual(split ? 1u : 0u, early.DrawCalls);
            Assert.AreEqual(0u, renderer.RenderPrepared(frame, WorldLiquidDrawPhase.Early).DrawCalls);
            var late = renderer.RenderPrepared(frame, WorldLiquidDrawPhase.Late);
            Assert.AreEqual(split ? 1u : 2u, late.DrawCalls);
            Assert.AreEqual(12ul, early.SubmittedIndices + late.SubmittedIndices);
            Assert.AreEqual(0u, renderer.RenderPrepared(frame, WorldLiquidDrawPhase.Late).DrawCalls);
            context.CopyResource((ID3D11Resource*)readback.Handle, (ID3D11Resource*)target.Handle);
            MappedSubresource mapped = default;
            SilkMarshal.ThrowHResult(context.Map((ID3D11Resource*)readback.Handle, 0, Map.Read, 0, ref mapped));
            try
            {
                var pixel = *(Vector4*)((byte*)mapped.PData + 8 * mapped.RowPitch + 8 * sizeof(Vector4));
                var expected = sharedPlacements ? new Vector4(.3f, 0, 0, .75f) : new Vector4(.1f, 0, .2f, .75f);
                Assert.IsTrue(Vector4.Distance(expected, pixel) < .00001f, $"Got {pixel}");
            }
            finally { context.Unmap((ID3D11Resource*)readback.Handle, 0); }
            ComPtr<ID3D11ShaderResourceView> bound = default;
            context.PSGetShaderResources(0, 1, ref bound);
            try { Assert.IsTrue(bound.Handle == null); }
            finally { bound.Dispose(); }
            prepared = renderer.Prepare(camera, [], [], new HashSet<int>(), 100, 100);
            Assert.AreEqual(0, prepared.VisibleBatches);
            Assert.AreEqual(0u, renderer.RenderPrepared(frame, WorldLiquidDrawPhase.Late).DrawCalls);
        }
        finally
        {
            context.ClearState();
            cache.TryRemove(key, out _);
            if (sharedPlacements)
            {
                wmoCache.Remove(wmoKey); // Fixture owns the shared GPU resources.
                if (first != null) WMOCache.Release(wmoKey, 0);
                if (second != null) WMOCache.Release(wmoKey, 0);
                wmoDevice.SetValue(null, previousWmoDevice);
            }
            WorldLiquidLoader.Unload(ref resources, 0);
            surfaceView.Dispose(); targetView.Dispose(); surface.Dispose(); readback.Dispose(); target.Dispose();
            context.Dispose(); device.Dispose();
        }
    }

    private static ParsedWorldLiquidBatch Batch(int material, BoundingBox bounds) => new(0, material, 0, 6, material, bounds, false, false)
        { IsWmo = true, IsWmoInterior = true };
    private static WorldLiquidMaterialDescriptor Material(uint flags, Vector4 color, uint surface) =>
        new(new(17, 0), WorldLiquidMaterialFamily.Water, color, color, 1, 0, 0, [])
        {
            Wrath335 = new(1, 0, 1, 0, 1, 1000), WmoMaterialFlags = flags,
            TextureSlots = [new([surface]), new([]) { ProceduralDepth = WorldLiquidWaterType.Wmo }]
        };
}
