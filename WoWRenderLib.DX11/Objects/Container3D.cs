using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System.Numerics;
using WoWRenderLib.Raycasting;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Objects
{
    public class Container3D
    {
        private Vector3 _position;
        private Vector3 _rotation;
        private float _scale;
        private Matrix4x4? _modelMatrix;

        public uint ParentFileDataId { get; set; }
        public uint FileDataId { get; set; }
        public Vector3 Position
        {
            get => _position;
            set
            {
                if (_position == value)
                    return;
                _position = value;
                InvalidateTransform();
            }
        }
        public Vector3 Rotation
        {
            get => _rotation;
            set
            {
                if (_rotation == value)
                    return;
                _rotation = value;
                InvalidateTransform();
            }
        }
        public float Scale
        {
            get => _scale;
            set
            {
                if (_scale == value)
                    return;
                _scale = value;
                InvalidateTransform();
            }
        }

        public Matrix4x4? ModelMatrix
        {
            get => _modelMatrix;
            set
            {
                if (_modelMatrix == value)
                    return;

                _modelMatrix = value;
                CachedBoundingBox = null;
                CachedBoundingSphere = null;
                OnTransformInvalidated();
            }
        }

        public ComPtr<ID3D11Device> _device;

        public BoundingBox? CachedBoundingBox { get; set; }
        public BoundingSphere? CachedBoundingSphere { get; set; }

        public bool IsSelected { get; set; } = false;

        public Container3D(ComPtr<ID3D11Device> device, uint fileDataId, uint parentFileDataId)
        {
            _device = device;
            FileDataId = fileDataId;
            ParentFileDataId = parentFileDataId;
        }

        public virtual BoundingSphere? GetBoundingSphere()
        {
            return null;
        }

        public virtual BoundingBox? GetBoundingBox()
        {
            return null;
        }

        public void InvalidateTransform()
        {
            _modelMatrix = null;
            CachedBoundingBox = null;
            CachedBoundingSphere = null;
            OnTransformInvalidated();
        }

        protected virtual void OnTransformInvalidated()
        {
        }

        public virtual Matrix4x4 GetModelMatrix()
        {
            if (ModelMatrix.HasValue)
                return ModelMatrix.Value;

            Matrix4x4 modelMatrix;

            if (this is M2Container m2Container && m2Container.ParentWMO != null)
            {
                modelMatrix = Matrix4x4.CreateScale(m2Container.LocalScale);
                modelMatrix *= Matrix4x4.CreateFromQuaternion(m2Container.LocalRotation);
                modelMatrix *= Matrix4x4.CreateTranslation(m2Container.LocalPosition.X, m2Container.LocalPosition.Y, m2Container.LocalPosition.Z);

                var parentMatrix = Matrix4x4.CreateScale(m2Container.ParentWMO.Scale);
                parentMatrix *= Matrix4x4.CreateRotationX(MathF.PI / 180f * m2Container.ParentWMO.Rotation.Z);
                parentMatrix *= Matrix4x4.CreateRotationY(MathF.PI / 180f * m2Container.ParentWMO.Rotation.X);
                parentMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * (m2Container.ParentWMO.Rotation.Y + 90f));
                parentMatrix *= Matrix4x4.CreateTranslation(m2Container.ParentWMO.Position.X, m2Container.ParentWMO.Position.Z, m2Container.ParentWMO.Position.Y);

                modelMatrix *= parentMatrix;
            }
            else
            {
                modelMatrix = Matrix4x4.CreateScale(this.Scale);
                modelMatrix *= Matrix4x4.CreateRotationX(MathF.PI / 180f * this.Rotation.Z);
                modelMatrix *= Matrix4x4.CreateRotationY(MathF.PI / 180f * this.Rotation.X);
                modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * (this.Rotation.Y + 90f));
                modelMatrix *= Matrix4x4.CreateTranslation(this.Position.X, this.Position.Z, this.Position.Y);
            }

            modelMatrix *= Matrix4x4.CreateRotationZ(MathF.PI / 180f * -270f);

            _modelMatrix = modelMatrix;

            return modelMatrix;
        }
    }
}
