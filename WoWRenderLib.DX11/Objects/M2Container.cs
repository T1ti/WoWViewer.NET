using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using WoWRenderLib.DX11.Cache;
using WoWRenderLib.DX11.Managers;
using WoWRenderLib.DX11.Raycasting;
using WoWRenderLib.DX11.Structs;

namespace WoWRenderLib.DX11.Objects
{
    public class M2Container : Container3D
    {
        public bool[] EnabledGeosets { get; }

        private DoodadBatch m2;
        public WMOContainer? ParentWMO { get; set; } = null;
        public Vector3 LocalPosition { get; set; }
        public Quaternion LocalRotation { get; set; }
        public float LocalScale { get; set; } = 1.0f;

        public M2Container(ComPtr<ID3D11Device> device, uint fileDataID, CompiledShader shaderProgram, uint parentFileDataId) : base(device, fileDataID, shaderProgram, parentFileDataId)
        {
            m2 = M2Cache.GetOrLoad(device, fileDataID, shaderProgram, parentFileDataId);
            EnabledGeosets = new bool[m2.submeshes.Length];
            Array.Fill(EnabledGeosets, true);
        }

        public override BoundingSphere? GetBoundingSphere()
        {
            var transformedCenter = Vector3.Transform(Vector3.Zero, GetModelMatrix());
            return new BoundingSphere(transformedCenter, m2.boundingRadius * Scale);
        }

        public override BoundingBox? GetBoundingBox()
        {
            var box = new BoundingBox(m2.boundingBox.Min, m2.boundingBox.Max);
            return BoundingBox.Transform(box, GetModelMatrix());
        }

        public BoundingBox GetLocalBoundingBox()
        {
            return new BoundingBox(m2.boundingBox.Min, m2.boundingBox.Max);
        }
    }
}
