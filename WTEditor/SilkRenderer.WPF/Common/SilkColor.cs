using System.Numerics;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;

namespace WTEditor.SilkRenderer.WPF.Common;

public struct SilkColor
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public byte A { get; set; }

    public static readonly SilkColor Zero = FromBgra(0);
    public static readonly SilkColor Transparent = FromBgra(0);
    public static readonly SilkColor AliceBlue = FromBgra(4293982463u);
    public static readonly SilkColor AntiqueWhite = FromBgra(4294634455u);
    public static readonly SilkColor Aqua = FromBgra(4278255615u);
    public static readonly SilkColor Aquamarine = FromBgra(4286578644u);
    public static readonly SilkColor Azure = FromBgra(4293984255u);
    public static readonly SilkColor Beige = FromBgra(4294309340u);
    public static readonly SilkColor Bisque = FromBgra(4294960324u);
    public static readonly SilkColor Black = FromBgra(4278190080u);
    public static readonly SilkColor BlanchedAlmond = FromBgra(4294962125u);
    public static readonly SilkColor Blue = FromBgra(4278190335u);
    public static readonly SilkColor BlueViolet = FromBgra(4287245282u);
    public static readonly SilkColor Brown = FromBgra(4289014314u);
    public static readonly SilkColor BurlyWood = FromBgra(4292786311u);
    public static readonly SilkColor CadetBlue = FromBgra(4284456608u);
    public static readonly SilkColor Chartreuse = FromBgra(4286578432u);
    public static readonly SilkColor Chocolate = FromBgra(4291979550u);
    public static readonly SilkColor Coral = FromBgra(4294934352u);
    public static readonly SilkColor CornflowerBlue = FromBgra(4284782061u);
    public static readonly SilkColor Cornsilk = FromBgra(4294965468u);
    public static readonly SilkColor Crimson = FromBgra(4292613180u);
    public static readonly SilkColor Cyan = FromBgra(4278255615u);
    public static readonly SilkColor DarkBlue = FromBgra(4278190219u);
    public static readonly SilkColor DarkCyan = FromBgra(4278225803u);
    public static readonly SilkColor DarkGoldenrod = FromBgra(4290283019u);
    public static readonly SilkColor DarkGray = FromBgra(4289309097u);
    public static readonly SilkColor DarkGreen = FromBgra(4278215680u);
    public static readonly SilkColor DarkKhaki = FromBgra(4290623339u);
    public static readonly SilkColor DarkMagenta = FromBgra(4287299723u);
    public static readonly SilkColor DarkOliveGreen = FromBgra(4283788079u);
    public static readonly SilkColor DarkOrange = FromBgra(4294937600u);
    public static readonly SilkColor DarkOrchid = FromBgra(4288230092u);
    public static readonly SilkColor DarkRed = FromBgra(4287299584u);
    public static readonly SilkColor DarkSalmon = FromBgra(4293498490u);
    public static readonly SilkColor DarkSeaGreen = FromBgra(4287609995u);
    public static readonly SilkColor DarkSlateBlue = FromBgra(4282924427u);
    public static readonly SilkColor DarkSlateGray = FromBgra(4281290575u);
    public static readonly SilkColor DarkTurquoise = FromBgra(4278243025u);
    public static readonly SilkColor DarkViolet = FromBgra(4287889619u);
    public static readonly SilkColor DeepPink = FromBgra(4294907027u);
    public static readonly SilkColor DeepSkyBlue = FromBgra(4278239231u);
    public static readonly SilkColor DimGray = FromBgra(4285098345u);
    public static readonly SilkColor DodgerBlue = FromBgra(4280193279u);
    public static readonly SilkColor Firebrick = FromBgra(4289864226u);
    public static readonly SilkColor FloralWhite = FromBgra(4294966000u);
    public static readonly SilkColor ForestGreen = FromBgra(4280453922u);
    public static readonly SilkColor Fuchsia = FromBgra(4294902015u);
    public static readonly SilkColor Gainsboro = FromBgra(4292664540u);
    public static readonly SilkColor GhostWhite = FromBgra(4294506751u);
    public static readonly SilkColor Gold = FromBgra(4294956800u);
    public static readonly SilkColor Goldenrod = FromBgra(4292519200u);
    public static readonly SilkColor Gray = FromBgra(4286611584u);
    public static readonly SilkColor Green = FromBgra(4278222848u);
    public static readonly SilkColor GreenYellow = FromBgra(4289593135u);
    public static readonly SilkColor Honeydew = FromBgra(4293984240u);
    public static readonly SilkColor HotPink = FromBgra(4294928820u);
    public static readonly SilkColor IndianRed = FromBgra(4291648604u);
    public static readonly SilkColor Indigo = FromBgra(4283105410u);
    public static readonly SilkColor Ivory = FromBgra(4294967280u);
    public static readonly SilkColor Khaki = FromBgra(4293977740u);
    public static readonly SilkColor Lavender = FromBgra(4293322490u);
    public static readonly SilkColor LavenderBlush = FromBgra(4294963445u);
    public static readonly SilkColor LawnGreen = FromBgra(4286381056u);
    public static readonly SilkColor LemonChiffon = FromBgra(4294965965u);
    public static readonly SilkColor LightBlue = FromBgra(4289583334u);
    public static readonly SilkColor LightCoral = FromBgra(4293951616u);
    public static readonly SilkColor LightCyan = FromBgra(4292935679u);
    public static readonly SilkColor LightGoldenrodYellow = FromBgra(4294638290u);
    public static readonly SilkColor LightGray = FromBgra(4292072403u);
    public static readonly SilkColor LightGreen = FromBgra(4287688336u);
    public static readonly SilkColor LightPink = FromBgra(4294948545u);
    public static readonly SilkColor LightSalmon = FromBgra(4294942842u);
    public static readonly SilkColor LightSeaGreen = FromBgra(4280332970u);
    public static readonly SilkColor LightSkyBlue = FromBgra(4287090426u);
    public static readonly SilkColor LightSlateGray = FromBgra(4286023833u);
    public static readonly SilkColor LightSteelBlue = FromBgra(4289774814u);
    public static readonly SilkColor LightYellow = FromBgra(4294967264u);
    public static readonly SilkColor Lime = FromBgra(4278255360u);
    public static readonly SilkColor LimeGreen = FromBgra(4281519410u);
    public static readonly SilkColor Linen = FromBgra(4294635750u);
    public static readonly SilkColor Magenta = FromBgra(4294902015u);
    public static readonly SilkColor Maroon = FromBgra(4286578688u);
    public static readonly SilkColor MediumAquamarine = FromBgra(4284927402u);
    public static readonly SilkColor MediumBlue = FromBgra(4278190285u);
    public static readonly SilkColor MediumOrchid = FromBgra(4290401747u);
    public static readonly SilkColor MediumPurple = FromBgra(4287852763u);
    public static readonly SilkColor MediumSeaGreen = FromBgra(4282168177u);
    public static readonly SilkColor MediumSlateBlue = FromBgra(4286277870u);
    public static readonly SilkColor MediumSpringGreen = FromBgra(4278254234u);
    public static readonly SilkColor MediumTurquoise = FromBgra(4282962380u);
    public static readonly SilkColor MediumVioletRed = FromBgra(4291237253u);
    public static readonly SilkColor MidnightBlue = FromBgra(4279834992u);
    public static readonly SilkColor MintCream = FromBgra(4294311930u);
    public static readonly SilkColor MistyRose = FromBgra(4294960353u);
    public static readonly SilkColor Moccasin = FromBgra(4294960309u);
    public static readonly SilkColor NavajoWhite = FromBgra(4294958765u);
    public static readonly SilkColor Navy = FromBgra(4278190208u);
    public static readonly SilkColor OldLace = FromBgra(4294833638u);
    public static readonly SilkColor Olive = FromBgra(4286611456u);
    public static readonly SilkColor OliveDrab = FromBgra(4285238819u);
    public static readonly SilkColor Orange = FromBgra(4294944000u);
    public static readonly SilkColor OrangeRed = FromBgra(4294919424u);
    public static readonly SilkColor Orchid = FromBgra(4292505814u);
    public static readonly SilkColor PaleGoldenrod = FromBgra(4293847210u);
    public static readonly SilkColor PaleGreen = FromBgra(4288215960u);
    public static readonly SilkColor PaleTurquoise = FromBgra(4289720046u);
    public static readonly SilkColor PaleVioletRed = FromBgra(4292571283u);
    public static readonly SilkColor PapayaWhip = FromBgra(4294963157u);
    public static readonly SilkColor PeachPuff = FromBgra(4294957753u);
    public static readonly SilkColor Peru = FromBgra(4291659071u);
    public static readonly SilkColor Pink = FromBgra(4294951115u);
    public static readonly SilkColor Plum = FromBgra(4292714717u);
    public static readonly SilkColor PowderBlue = FromBgra(4289781990u);
    public static readonly SilkColor Purple = FromBgra(4286578816u);
    public static readonly SilkColor Red = FromBgra(4294901760u);
    public static readonly SilkColor RosyBrown = FromBgra(4290547599u);
    public static readonly SilkColor RoyalBlue = FromBgra(4282477025u);
    public static readonly SilkColor SaddleBrown = FromBgra(4287317267u);
    public static readonly SilkColor Salmon = FromBgra(4294606962u);
    public static readonly SilkColor SandyBrown = FromBgra(4294222944u);
    public static readonly SilkColor SeaGreen = FromBgra(4281240407u);
    public static readonly SilkColor SeaShell = FromBgra(4294964718u);
    public static readonly SilkColor Sienna = FromBgra(4288696877u);
    public static readonly SilkColor Silver = FromBgra(4290822336u);
    public static readonly SilkColor SkyBlue = FromBgra(4287090411u);
    public static readonly SilkColor SlateBlue = FromBgra(4285160141u);
    public static readonly SilkColor SlateGray = FromBgra(4285563024u);
    public static readonly SilkColor Snow = FromBgra(4294966010u);
    public static readonly SilkColor SpringGreen = FromBgra(4278255487u);
    public static readonly SilkColor SteelBlue = FromBgra(4282811060u);
    public static readonly SilkColor Tan = FromBgra(4291998860u);
    public static readonly SilkColor Teal = FromBgra(4278222976u);
    public static readonly SilkColor Thistle = FromBgra(4292394968u);
    public static readonly SilkColor Tomato = FromBgra(4294927175u);
    public static readonly SilkColor Turquoise = FromBgra(4282441936u);
    public static readonly SilkColor Violet = FromBgra(4293821166u);
    public static readonly SilkColor Wheat = FromBgra(4294303411u);
    public static readonly SilkColor White = FromBgra(uint.MaxValue);
    public static readonly SilkColor WhiteSmoke = FromBgra(4294309365u);
    public static readonly SilkColor Yellow = FromBgra(4294967040u);
    public static readonly SilkColor YellowGreen = FromBgra(4288335154u);

    public SilkColor(byte red, byte green, byte blue, byte alpha = 255)
    {
        R = red;
        G = green;
        B = blue;
        A = alpha;
    }
    public SilkColor(float red, float green, float blue, float alpha = 1.0f)
    {
        R = ToByte(red);
        G = ToByte(green);
        B = ToByte(blue);
        A = ToByte(alpha);
    }
    public SilkColor(int rgba)
    {
        A = (byte)((uint)(rgba >> 24) & 0xFFu);
        B = (byte)((uint)(rgba >> 16) & 0xFFu);
        G = (byte)((uint)(rgba >> 8) & 0xFFu);
        R = (byte)((uint)rgba & 0xFFu);
    }
    public SilkColor(uint rgba)
    {
        A = (byte)((rgba >> 24) & 0xFFu);
        B = (byte)((rgba >> 16) & 0xFFu);
        G = (byte)((rgba >> 8) & 0xFFu);
        R = (byte)(rgba & 0xFFu);
    }

    public readonly int ToBgra() => B | (G << 8) | (R << 16) | (A << 24);
    public readonly int ToRgba() => R | (G << 8) | (B << 16) | (A << 24);
    public readonly int ToAbgr() => A | (B << 8) | (G << 16) | (R << 24);

    public static SilkColor FromDrawingColor(DrawingColor color)
    {
        return new SilkColor(color.R, color.G, color.B, color.A);
    }

    public static SilkColor FromMediaColor(MediaColor color)
    {
        return new SilkColor(color.R, color.G, color.B, color.A);
    }

    public static SilkColor FromBgra(int color)
    {
        return new SilkColor((byte)((uint)(color >> 16) & 0xFFu), (byte)((uint)(color >> 8) & 0xFFu), (byte)((uint)color & 0xFFu), (byte)((uint)(color >> 24) & 0xFFu));
    }

    public static SilkColor FromBgra(uint color)
    {
        return FromBgra((int)color);
    }

    public static SilkColor FromRgba(int color)
    {
        return new SilkColor(color);
    }

    public static SilkColor FromRgba(uint color)
    {
        return new SilkColor(color);
    }

    public static SilkColor FromAbgr(int color)
    {
        return new SilkColor((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);
    }

    public static SilkColor FromAbgr(uint color)
    {
        return FromAbgr((int)color);
    }

    public static SilkColor FromHsv(Vector4 hsv)
    {
        float num = hsv.X * 360f;
        float y = hsv.Y;
        float z = hsv.Z;
        float num2 = z * y;
        float num3 = num / 60f;
        float num4 = num2 * (1f - Math.Abs(num3 % 2f - 1f));
        float num5;
        float num6;
        float num7;
        if (num3 >= 0f && num3 < 1f)
        {
            num5 = num2;
            num6 = num4;
            num7 = 0f;
        }
        else if (num3 >= 1f && num3 < 2f)
        {
            num5 = num4;
            num6 = num2;
            num7 = 0f;
        }
        else if (num3 >= 2f && num3 < 3f)
        {
            num5 = 0f;
            num6 = num2;
            num7 = num4;
        }
        else if (num3 >= 3f && num3 < 4f)
        {
            num5 = 0f;
            num6 = num4;
            num7 = num2;
        }
        else if (num3 >= 4f && num3 < 5f)
        {
            num5 = num4;
            num6 = 0f;
            num7 = num2;
        }
        else if (num3 >= 5f && num3 < 6f)
        {
            num5 = num2;
            num6 = 0f;
            num7 = num4;
        }
        else
        {
            num5 = 0f;
            num6 = 0f;
            num7 = 0f;
        }

        float num8 = z - num2;
        return new SilkColor(hsv.W, num5 + num8, num6 + num8, num7 + num8);
    }

    public static DrawingColor ByDrawingColor(SilkColor color)
    {
        return DrawingColor.FromArgb(color.A, color.R, color.G, color.B);
    }
    public static MediaColor ByMediaColor(SilkColor color)
    {
        return MediaColor.FromArgb(color.A, color.R, color.G, color.B);
    }

    private static byte ToByte(float component)
    {
        return ToByte((int)(component * 255f));
    }
    public static byte ToByte(int value)
    {
        return (byte)((value >= 0) ? ((value > 255) ? 255u : ((uint)value)) : 0u);
    }
}