using Silk.NET.DXGI;

namespace WoWRenderLib.DX11.Structs
{
    public struct DecodedBLP
    {
        public uint FileDataId;
        public byte[] PixelData;
        public int Width;
        public int Height;
        public byte AlphaDepth;
        public bool IsCompressed;
        public Format CompressedFormat;
        public List<MipLevel>? MipLevels;
    }

    public struct MipLevel
    {
        public byte[] Data;
        public int Width;
        public int Height;
        public int Level;
    }
}
