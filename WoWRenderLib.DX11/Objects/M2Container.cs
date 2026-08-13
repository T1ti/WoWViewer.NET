using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Structs;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Objects
{
    public class M2Container : Container3D
    {
        private bool[]? _enabledGeosets;
        private WMOContainer? _parentWmo;
        private Vector3 _localPosition;
        private Quaternion _localRotation;
        private float _localScale = 1.0f;

        public WMOContainer? ParentWMO
        {
            get => _parentWmo;
            set
            {
                if (ReferenceEquals(_parentWmo, value))
                    return;
                _parentWmo = value;
                InvalidateTransform();
            }
        }
        public Vector3 LocalPosition
        {
            get => _localPosition;
            set
            {
                if (_localPosition == value)
                    return;
                _localPosition = value;
                InvalidateTransform();
            }
        }
        public Quaternion LocalRotation
        {
            get => _localRotation;
            set
            {
                if (_localRotation == value)
                    return;
                _localRotation = value;
                InvalidateTransform();
            }
        }
        public float LocalScale
        {
            get => _localScale;
            set
            {
                if (_localScale == value)
                    return;
                _localScale = value;
                InvalidateTransform();
            }
        }

        public bool[] EnabledGeosets
        {
            get
            {
                var m2 = GetM2();

                if (_enabledGeosets == null || _enabledGeosets.Length != m2.submeshes.Length)
                {
                    _enabledGeosets = new bool[m2.submeshes.Length];
                    Array.Fill(_enabledGeosets, true);
                }

                return _enabledGeosets;
            }
        }

        public M2Container(ComPtr<ID3D11Device> device, uint fileDataID, uint parentFileDataId) : base(device, fileDataID, parentFileDataId)
        {
            GetM2(true);
        }

        public override BoundingSphere? GetBoundingSphere()
        {
            if (CachedBoundingSphere.HasValue)
                return CachedBoundingSphere.Value;

            var m2 = GetM2();
            if (m2.fileDataID != FileDataId)
                return null;

            var localSphere = new BoundingSphere(m2.boundingBox.Center, m2.boundingRadius);
            CachedBoundingSphere = BoundingSphere.Transform(localSphere, GetModelMatrix());
            return CachedBoundingSphere.Value;
        }

        public override BoundingBox? GetBoundingBox()
        {
            if (CachedBoundingBox.HasValue)
                return CachedBoundingBox.Value;

            var m2 = GetM2();
            if (m2.fileDataID != FileDataId)
                return null;

            CachedBoundingBox = BoundingBox.Transform(m2.boundingBox, GetModelMatrix());
            return CachedBoundingBox.Value;
        }

        public BoundingBox GetLocalBoundingBox()
        {
            return GetM2().boundingBox;
        }

        public ParsedDoodadBatch GetM2(bool keepTrack = false)
        {
            return M2Cache.GetOrLoad(_device, FileDataId, ParentFileDataId, keepTrack);
        }
    }
}
