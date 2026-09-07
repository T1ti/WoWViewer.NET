using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Objects
{
    public class ADTContainer : Container3D
    {
        public Terrain Terrain { get; private set; }
        public MapTile mapTile;
        public event Action<ADTContainer, Terrain>? LoadCallback;
        public event Action<ADTContainer, Exception>? LoadFailedCallback;
        public bool IsLoaded { get; private set; }
        public bool IsModified { get; private set; }
        private ADTVertex[] _originalVertices = [];
        private bool _hasOriginalVertices;
        private bool _cacheReferenceHeld;
        private long? _unloadRequestedAt;

        public ADTContainer(ComPtr<ID3D11Device> device, MapTile mapTile) : base(device, mapTile.wdtFileDataID, mapTile.wdtFileDataID)
        {
            // TODO: LOD ADTs or premade placeholder Terrain?
            this.mapTile = mapTile;
        }

        public void UpdateTerrain(Terrain terrain)
        {
            Terrain = terrain;
        }

        internal void MarkCacheReferenceHeld()
        {
            _cacheReferenceHeld = true;
        }

        public void OnLoaded(Terrain terrain)
        {
            // this gets called by the cache when it finishes (up)loading terrain
            UpdateTerrain(terrain);
            _originalVertices = [];
            _hasOriginalVertices = false;
            IsModified = false;
            IsLoaded = true;
            LoadCallback?.Invoke(this, terrain); // and in turn we left scene manager know it loaded!
        }

        public void OnLoadFailed(Exception exception) =>
            LoadFailedCallback?.Invoke(this, exception);

        internal bool IsUnloadScheduled => _unloadRequestedAt.HasValue;

        internal void ScheduleUnload(TimeProvider timeProvider) =>
            _unloadRequestedAt ??= timeProvider.GetTimestamp();

        internal void CancelUnload() => _unloadRequestedAt = null;

        internal bool IsUnloadDue(TimeProvider timeProvider, TimeSpan delay) =>
            _unloadRequestedAt is long requestedAt &&
            timeProvider.GetElapsedTime(requestedAt) >= delay;

        public void Unload()
        {
            if (_cacheReferenceHeld)
            {
                ADTCache.Release(mapTile, mapTile.wdtFileDataID);
                _cacheReferenceHeld = false;
            }

            IsLoaded = false;
            IsModified = false;
            _originalVertices = [];
            _hasOriginalVertices = false;
            _unloadRequestedAt = null;
            Terrain = default;
        }

        internal void EnsureOriginalVerticesCaptured()
        {
            if (_hasOriginalVertices)
                return;

            _originalVertices = Terrain.vertices?.ToArray() ?? [];
            _hasOriginalVertices = true;
        }

        public void RefreshModifiedState()
        {
            var vertices = Terrain.vertices;
            IsModified = _hasOriginalVertices && vertices is { Length: > 0 } &&
                         (_originalVertices.Length != vertices.Length ||
                          !HaveSamePositions(_originalVertices, vertices));
        }

        public void MarkSaved()
        {
            _originalVertices = [];
            _hasOriginalVertices = false;
            IsModified = false;
        }

        private static bool HaveSamePositions(ADTVertex[] left, ADTVertex[] right)
        {
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index].Position != right[index].Position)
                    return false;
            }

            return true;
        }

        public override Matrix4x4 GetModelMatrix()
        {
            if (ModelMatrix.HasValue)
                return ModelMatrix.Value;

            ModelMatrix = Matrix4x4.CreateRotationZ(MathF.PI) * Matrix4x4.CreateScale(-1f, -1f, 1f);

            return ModelMatrix.Value;
        }
    }
}
