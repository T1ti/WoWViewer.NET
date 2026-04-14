using BLPSharp;
using Silk.NET.OpenGL;
using System.Diagnostics;
using WoWFormatLib.FileProviders;
using WoWFormatLib.Structs.TEX;

namespace WoWRenderLib.Loaders
{
    public static class BLPLoader
    {
        private static readonly byte[] PlaceholderPixels = [255, 0, 255, 255];

        public static unsafe uint CreatePlaceholderTexture(GL gl)
        {
            var textureID = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, textureID);

            fixed (byte* buf = PlaceholderPixels)
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, buf);

            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            return textureID;
        }

        public static unsafe void LoadTextureIntoID(GL gl, uint fileDataID, uint textureID)
        {
            gl.ActiveTexture(TextureUnit.Texture0);

            using (var blp = new BLPFile(FileProvider.OpenFile(fileDataID)))
            {
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

                    for (int i = 0; i < blp.MipMapCount; i++)
                    {
                        int scale = (int)Math.Pow(2, i);

                        var width = blp.width / scale;
                        var height = blp.height / scale;

                        if (width == 0 || height == 0)
                            break;

                        var bytes = blp.GetPictureData(i, width, height);

                        maxMip = i;

                        fixed (byte* buf = bytes)
                            gl.CompressedTexImage2D(TextureTarget.Texture2D, i, openGLFormat, (uint)width, (uint)height, 0, (uint)bytes.Length, buf);
                    }

                    gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, maxMip);
                    gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                }
                else
                {
                    var pixelBytes = blp.GetPixels(0, out int width, out int height) ?? throw new Exception("BMP is null!");
                    fixed (byte* buf = pixelBytes)
                        gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, buf);

                    gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                }

                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            }
        }

        public unsafe static uint GenerateAlphaTexture(GL gl, byte[] values, bool saveToFile = false)
        {
            gl.ActiveTexture(TextureUnit.Texture1);

            var textureID = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, textureID);

            fixed (byte* buf = values)
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 64, 64, 0, PixelFormat.Rgba, PixelType.UnsignedByte, buf);

            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            //  gl.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, (int)TextureEnvMode.Modulate);

            gl.ActiveTexture(TextureUnit.Texture0);

            return textureID;
        }

        public unsafe static uint CreateTextureFromBlob(GL gl, BlobTexture blobTex, byte[] bytes)
        {
            var pixelFormat = blobTex.dxtFormat switch
            {
                0 => InternalFormat.CompressedRgbaS3TCDxt1Ext,// todo: alpha or not?
                1 => InternalFormat.CompressedRgbaS3TCDxt3Ext,
                2 => InternalFormat.CompressedRgbaS3TCDxt5Ext,
                _ => throw new NotImplementedException(),
            };

            gl.ActiveTexture(TextureUnit.Texture0);

            var textureID = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, textureID);

            var sizePerBlock = blobTex.dxtFormat switch
            {
                0 => 8,
                1 => 16,
                2 => 16,
                _ => throw new NotImplementedException(),
            };

            // block is 4x4
            var expectedBytes = (blobTex.sizeX / 4) * (blobTex.sizeY / 4) * sizePerBlock;
            bytes = [.. bytes.Take(expectedBytes)];

            fixed (byte* buf = bytes)
                gl.CompressedTexImage2D(TextureTarget.Texture2D, 0, pixelFormat, blobTex.sizeX, blobTex.sizeY, 0, (uint)bytes.Length, buf);

            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            return textureID;
        }
    }
}