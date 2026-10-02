using System.Numerics;
using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>12340 size categories (0x7BDB10), thresholds (0x78F570/0x78FB60)
/// and submission distance/alpha (0x791CB0). GPU fade blend/state selection is separate.</summary>
internal readonly struct Wrath335DoodadFade
{
    public float EnvironmentDetail { get; }
    public bool ObjectFade { get; }

    public Wrath335DoodadFade(float environmentDetail, bool objectFade = true)
    {
        EnvironmentDetail = float.IsFinite(environmentDetail) ? Math.Clamp(environmentDetail, 0.5f, 1.5f) : 1f;
        ObjectFade = objectFade;
    }

    public static Wrath335DoodadFade? Resolve(RendererSettings settings, bool referenceClient) =>
        referenceClient && settings.UseClientRenderingRules ? new(settings.EnvironmentDetail, settings.ObjectFade) : null;

    public static byte SizeCategory(in BoundingBox worldBounds)
    {
        // 0x7BDCFD..0x7BDD4C: subtraction remains extended, inclusive extents.
        var extent = Math.Max((double)worldBounds.Max.X - worldBounds.Min.X,
            Math.Max((double)worldBounds.Max.Y - worldBounds.Min.Y, (double)worldBounds.Max.Z - worldBounds.Min.Z));
        return extent <= 1d ? (byte)0 : extent <= 4d ? (byte)1 : extent <= 15d ? (byte)2 : extent <= 100d ? (byte)3 : (byte)4;
    }

    internal readonly record struct Limits(float MaximumSquared, float MinimumSquared, float Minimum, float Range);
    internal Limits GetLimits(byte category)
    {
        var maximum = category switch { 0 => 30d, 1 => 100d, 2 => 200d, 3 => 750d, _ => 1250d };
        var range = category switch { 0 => 5f, 1 => 10f, 2 => 15f, 3 => 20f, _ => 50f };
        if (category is > 0 and < 4)
            maximum *= EnvironmentDetail;
        var minimum = maximum - range;
        // 0x78F570 squares the extended products, then stores each table entry as float.
        return new((float)(maximum * maximum), (float)(minimum * minimum), (float)minimum, range);
    }

    public byte MinimumCategory(float distance)
    {
        if (distance < 0f) return 0;
        var squared = (double)distance * distance;
        for (byte category = 0; category < 4; category++)
            if (squared < GetLimits(category).MaximumSquared)
                return category;
        return 4;
    }

    public bool TryGetOpacity(byte category, double distanceSquared, out float opacity,
        bool bypassDistance = false, bool useSseSquareRoot = true)
    {
        opacity = 1f;
        if (!ObjectFade || bypassDistance || category > 4)
            return true;
        var limits = GetLimits(category);
        if (!double.IsFinite(distanceSquared) || distanceSquared > limits.MaximumSquared)
            return false;
        if (distanceSquared <= limits.MinimumSquared)
            return true;
        // 0x791DCB..0x791DE7 selects stored-float SQRTSS or extended FSQRT.
        var distance = useSseSquareRoot ? MathF.Sqrt((float)distanceSquared) : Math.Sqrt(distanceSquared);
        var alpha = 1d - (distance - limits.Minimum) / (double)limits.Range;
        if (alpha > 0.99f)
            return true;
        opacity = (float)alpha;
        return alpha > 0.01f;
    }

    public static double DistanceSquared(Vector3 center, Vector3 eye)
    {
        var x = (double)center.X - eye.X;
        var y = (double)center.Y - eye.Y;
        var z = (double)center.Z - eye.Z;
        return x * x + (y * y + z * z);
    }

    public static float GroupDistance(in BoundingBox bounds, Vector3 eye, Vector3 cameraForward)
    {
        var length = Math.Sqrt((double)cameraForward.X * cameraForward.X +
            (double)cameraForward.Y * cameraForward.Y + (double)cameraForward.Z * cameraForward.Z);
        if (!double.IsFinite(length) || length == 0d) return 0f;
        var x = cameraForward.X / length;
        var y = cameraForward.Y / length;
        var z = cameraForward.Z / length;
        var direction = new Vector3((float)x, (float)y, (float)z);
        var corner = new Vector3(direction.X >= 0f ? bounds.Min.X : bounds.Max.X,
            direction.Y >= 0f ? bounds.Min.Y : bounds.Max.Y, direction.Z >= 0f ? bounds.Min.Z : bounds.Max.Z);
        // 0x795586 stores plane W before 0x7993C9 adds it to the corner dot.
        var w = (float)-(x * eye.X + y * eye.Y + z * eye.Z);
        return (float)((double)direction.Z * corner.Z + (double)direction.Y * corner.Y +
            (double)direction.X * corner.X + w);
    }
}
