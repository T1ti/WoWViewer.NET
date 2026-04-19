using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using WoWFormatLib.Structs.WMO;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Raycasting;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Objects
{
    public class WMOContainer : Container3D
    {
        private readonly ComPtr<ID3D11Device> _device;
        private readonly CompiledShader _shaderProgram;
        private bool[]? enabledGroups;
        private bool[]? enabledDoodadSets;

        public bool DoodadsSpawned = false;

        public List<M2Container> ActiveDoodads = [];

        public Action<WMOContainer>? OnDoodadSetsChanged { get; set; }

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

        public WMOContainer(ComPtr<ID3D11Device> device, uint fileDataID, CompiledShader shaderProgram, uint parentFileDataId) : base(device, fileDataID, shaderProgram, parentFileDataId)
        {
            _device = device;
            _shaderProgram = shaderProgram;

            // Trigger initial array creation
            _ = EnabledGroups;
            _ = EnabledDoodadSets;
        }

        private Structs.WorldModel GetWMO()
        {
            return WMOCache.GetOrLoad(_device, FileDataId, _shaderProgram, ParentFileDataId, false);
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
        }

        public void ToggleDoodadSet(int index)
        {
            EnabledDoodadSets[index] = !EnabledDoodadSets[index];
            OnDoodadSetsChanged?.Invoke(this);
        }

        public override BoundingSphere? GetBoundingSphere()
        {
            if (!IsLoaded)
                return null;

            var wmo = GetWMO();
            var center = (wmo.boundingBox.Min + wmo.boundingBox.Max) / 2f;
            var halfExtents = (wmo.boundingBox.Max - wmo.boundingBox.Min) / 2f;
            var radius = halfExtents.Length();

            var transformedCenter = Vector3.Transform(center, GetModelMatrix());

            return new BoundingSphere(transformedCenter, radius * Scale);
        }

        public override BoundingBox? GetBoundingBox()
        {
            if (!IsLoaded)
                return null;

            var wmo = GetWMO();
            var box = new BoundingBox(wmo.boundingBox.Min, wmo.boundingBox.Max);
            return BoundingBox.Transform(box, GetModelMatrix());
        }

        public BoundingBox GetLocalBoundingBox()
        {
            var wmo = GetWMO();
            return new BoundingBox(wmo.boundingBox.Min, wmo.boundingBox.Max);
        }
    }
}
