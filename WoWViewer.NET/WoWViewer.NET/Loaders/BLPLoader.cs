using SereniaBLPLib;
using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WoWFormatLib.Utils;

namespace WoWViewer.NET.Loaders
{
    public static class BLPLoader
    {
        public static uint LoadTexture(GL gl, string fileName)
        {
            if (Listfile.TryGetFileDataID(fileName, out var fileDataID))
                return LoadTexture(gl, fileDataID);
            else
                throw new Exception("Couldn't find filedataid for file " + fileName + " in listfile!");
        }

        public static unsafe uint LoadTexture(GL gl, uint fileDataID)
        {
            gl.ActiveTexture(TextureUnit.Texture0);

            var textureID = gl.GenTexture();
            using (var blp = new BlpFile(CASC.OpenFile(fileDataID)))
            {
                Console.WriteLine("Loading BLP " + fileDataID + " " + blp.preferredFormat.ToString());
                gl.BindTexture(TextureTarget.Texture2D, textureID);

                if (
                    blp.preferredFormat == BlpPixelFormat.Dxt1 ||
                    blp.preferredFormat == BlpPixelFormat.Dxt3 ||
                    blp.preferredFormat == BlpPixelFormat.Dxt5
                )
                {
                    var openGLFormat = InternalFormat.CompressedRgbaS3TCDxt5Ext;

                    if (blp.preferredFormat == BlpPixelFormat.Dxt1 && blp.alphaSize > 0)
                        openGLFormat = InternalFormat.CompressedRgbaS3TCDxt1Ext;
                    else if (blp.preferredFormat == BlpPixelFormat.Dxt1 && blp.alphaSize == 0)
                        openGLFormat = InternalFormat.CompressedRgbS3TCDxt1Ext;
                    else if (blp.preferredFormat == BlpPixelFormat.Dxt3)
                        openGLFormat = InternalFormat.CompressedRgbaS3TCDxt3Ext;

                    var maxMip = 0;

                    for(int i = 0; i < blp.MipMapCount; i++)
                    {
                        var bytes = blp.GetPixels(i, out int width, out int height, true, true);

                        if (width == 0 || height == 0 || bytes.Length == 0)
                            continue;

                        maxMip = i;

                        fixed (byte* buf = bytes)
                            gl.CompressedTexImage2D(TextureTarget.Texture2D, i, openGLFormat, (uint)width, (uint)height, 0, (uint)bytes.Length, buf);
                    }

                    gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, maxMip);
                    gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                }
                else
                {
                    var bmp = blp.GetImage(0) ?? throw new Exception("BMP is null!");
                    var pixelBytes = new byte[bmp.Width * bmp.Height * 4];
                    bmp.CopyPixelDataTo(pixelBytes);

                    fixed (byte* buf = pixelBytes)
                        gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba, (uint)bmp.Width, (uint)bmp.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, buf);

                    gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                }

                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            }

            return textureID;
        }

        public unsafe static uint GenerateAlphaTexture(GL gl, byte[] values, bool saveToFile = false)
        {
            gl.ActiveTexture(TextureUnit.Texture1);

            var textureId = gl.GenTexture();

            var pixelList = new byte[64 * 64 * 4];
            for (var x = 0; x < 64; x++)
            {
                for (var y = 0; y < 64; y++)
                {
                    pixelList[(x * 64) + y] = values[x * 64 + y];
                }
            }
            using (var image = Image.LoadPixelData<A8>(pixelList, 64, 64))
            {
                gl.BindTexture(TextureTarget.Texture2D, textureId);

                fixed (byte* buf = pixelList)
                    gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8, 64, 64, 0, PixelFormat.Red, PixelType.UnsignedByte, buf);

                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

                //  gl.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, (int)TextureEnvMode.Modulate);
            }

            gl.ActiveTexture(TextureUnit.Texture0);

            return textureId;
        }
    }
}
