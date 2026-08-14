using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Renderer;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Objects
{
    public class WMOContainer : Container3D
    {
        private bool[]? enabledGroups;
        private bool[]? enabledDoodadSets;
        private bool[]? _portalVisibleGroups;
        private bool[]? _portalVisibleDoodads;
        private readonly WmoPortalVisibilityScratch _portalVisibilityScratch = new();
        private long _portalVisibilityFrame = -1;

        public bool DoodadsSpawned = false;

        public List<M2Container> ActiveDoodads = [];

        public Action<WMOContainer>? OnDoodadSetsChanged { get; set; }
        public Action<WMOContainer>? OnGroupsChanged { get; set; }

        public string[] DoodadSets
        {
            get
            {
                var wmo = GetWMO();
                return wmo.doodadSets;
            }
        }

        public string[] Groups
        {
            get
            {
                var wmo = GetWMO();
                return [.. wmo.groupBatches.Select(x => x.groupName)];
            }
        }

        public bool[] EnabledGroups
        {
            get
            {
                var wmo = GetWMO();
                if (enabledGroups == null || enabledGroups.Length != wmo.groupBatches.Length)
                {
                    enabledGroups = new bool[wmo.groupBatches.Length];
                    Array.Fill(enabledGroups, true);
                }
                return enabledGroups;
            }
        }

        public bool[] EnabledDoodadSets
        {
            get
            {
                var wmo = GetWMO();
                if (enabledDoodadSets == null || enabledDoodadSets.Length != wmo.doodadSets.Length)
                {
                    enabledDoodadSets = new bool[wmo.doodadSets.Length];
                    for (int i = 0; i < wmo.doodadSets.Length; i++)
                    {
                        if (i == 0) // todo: check if this is a string check like below or not
                                    //if (wmo.doodadSets[i].Equals("Set_$DefaultGlobal", StringComparison.OrdinalIgnoreCase))
                            enabledDoodadSets[i] = true;
                        else
                            if (DoodadSetsToEnable.Count > 0 && DoodadSetsToEnable.Contains((uint)i))
                                enabledDoodadSets[i] = true;
                            else
                                enabledDoodadSets[i] = false;
                    }
                }

                return enabledDoodadSets;
            }
        }

        public bool IsLoaded
        {
            get
            {
                var wmo = GetWMO();
                return wmo.rootWMOFileDataID == FileDataId && wmo.groupBatches != null && wmo.groupBatches.Length > 0;
            }
        }

        public uint UniqueID;

        // TODO: This is a bit of a hack -- this is what sets should be enabled AFTER the WMO is actually loaded, so we use it above to ensure things are always loaded correctly. Keep in mind when doing async rework.
        public List<uint> DoodadSetsToEnable = [];

        public WMOContainer(ComPtr<ID3D11Device> device, uint fileDataID, uint parentFileDataId) : base(device, fileDataID, parentFileDataId)
        {
            GetWMO(true);
            // Trigger initial array creation
            _ = EnabledGroups;
            _ = EnabledDoodadSets;
        }

        public Structs.WorldModel GetWMO(bool keepTrack = false)
        {
            return WMOCache.GetOrLoad(_device, FileDataId, ParentFileDataId, keepTrack);
        }

        public void ToggleGroup(string name)
        {
            var wmo = GetWMO();
            var index = Array.FindIndex(wmo.groupBatches, x => x.groupName.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (index == -1)
                return;

            ToggleGroup(index);
        }

        public void ToggleDoodadSet(string name)
        {
            var wmo = GetWMO();
            var index = Array.FindIndex(wmo.doodadSets, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (index == -1)
                return;

            ToggleDoodadSet(index);
        }

        public void ToggleGroup(int index)
        {
            EnabledGroups[index] = !EnabledGroups[index];
            OnGroupsChanged?.Invoke(this);
        }

        public void ToggleDoodadSet(int index)
        {
            EnabledDoodadSets[index] = !EnabledDoodadSets[index];
            OnDoodadSetsChanged?.Invoke(this);
        }

        public void GetPortalVisibilityBuffers(
            in Structs.WorldModel wmo,
            out bool[] visibleGroups,
            out bool[] visibleDoodads,
            out WmoPortalVisibilityScratch scratch)
        {
            if (_portalVisibleGroups == null || _portalVisibleGroups.Length != wmo.groupBatches.Length)
                _portalVisibleGroups = new bool[wmo.groupBatches.Length];
            if (_portalVisibleDoodads == null || _portalVisibleDoodads.Length != wmo.doodads.Length)
                _portalVisibleDoodads = new bool[wmo.doodads.Length];
            visibleGroups = _portalVisibleGroups;
            visibleDoodads = _portalVisibleDoodads;
            scratch = _portalVisibilityScratch;
        }

        public void SetPortalVisibilityFrame(long frameNumber) =>
            _portalVisibilityFrame = frameNumber;

        public bool IsDoodadPortalVisible(int doodadIndex, long frameNumber) =>
            _portalVisibilityFrame != frameNumber ||
            _portalVisibleDoodads == null ||
            (uint)doodadIndex >= (uint)_portalVisibleDoodads.Length ||
            _portalVisibleDoodads[doodadIndex];

        public override BoundingSphere? GetBoundingSphere()
        {
            if (CachedBoundingSphere.HasValue)
                return CachedBoundingSphere.Value;

            if (!IsLoaded)
                return null;

            var wmo = GetWMO();
            var center = (wmo.boundingBox.Min + wmo.boundingBox.Max) / 2f;
            var halfExtents = (wmo.boundingBox.Max - wmo.boundingBox.Min) / 2f;
            var radius = halfExtents.Length();

            var transformedCenter = Vector3.Transform(center, GetModelMatrix());

            CachedBoundingSphere = new BoundingSphere(transformedCenter, radius * Scale);
            return CachedBoundingSphere.Value;
        }

        public override BoundingBox? GetBoundingBox()
        {
            if (CachedBoundingBox.HasValue)
                return CachedBoundingBox.Value;

            if (!IsLoaded)
                return null;

            var wmo = GetWMO();
            var box = new BoundingBox(wmo.boundingBox.Min, wmo.boundingBox.Max);
            CachedBoundingBox = BoundingBox.Transform(box, GetModelMatrix());
            return CachedBoundingBox.Value;
        }

        protected override void OnTransformInvalidated()
        {
            foreach (var doodad in ActiveDoodads)
                doodad.InvalidateTransform();
        }

        public BoundingBox GetLocalBoundingBox()
        {
            var wmo = GetWMO();
            return new BoundingBox(wmo.boundingBox.Min, wmo.boundingBox.Max);
        }

        public static string CreateEnabledGroupSignature(ReadOnlySpan<bool> groups)
        {
            var packed = new byte[(groups.Length + 7) / 8];
            for (var index = 0; index < groups.Length; index++)
            {
                if (groups[index])
                    packed[index / 8] |= (byte)(1 << (index & 7));
            }

            return $"{groups.Length}:{Convert.ToHexString(packed)}";
        }
    }
}
