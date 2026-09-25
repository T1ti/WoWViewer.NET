using System.Numerics;

namespace WoWRenderLib.DX11.Renderer;

public static class ScreenSpaceCulling
{
    public static bool IntersectsRenderDistance(
        Vector3 cameraPosition,
        Vector3 sphereCenter,
        float sphereRadius,
        float renderDistance)
        => IntersectsRenderDistanceSquared(
            Vector3.DistanceSquared(cameraPosition, sphereCenter), sphereRadius, renderDistance);

    public static bool IntersectsRenderDistanceSquared(
        float distanceSquared,
        float sphereRadius,
        float renderDistance)
    {
        var maxDistance = Math.Max(0f, renderDistance) + Math.Max(0f, sphereRadius);
        return distanceSquared <= maxDistance * maxDistance;
    }

    public static bool IsFullyWithinRenderDistance(
        Vector3 cameraPosition,
        Vector3 sphereCenter,
        float sphereRadius,
        float renderDistance)
    {
        var innerDistance = Math.Max(0f, renderDistance) - Math.Max(0f, sphereRadius);
        return innerDistance >= 0f &&
            Vector3.DistanceSquared(cameraPosition, sphereCenter) <= innerDistance * innerDistance;
    }

    public static float EstimateProjectedDiameterPixels(
        Vector3 cameraPosition,
        Vector3 cameraForward,
        Vector3 sphereCenter,
        float sphereRadius,
        float verticalProjectionScale,
        uint viewportHeight)
    {
        if (sphereRadius <= 0f || verticalProjectionScale <= 0f || viewportHeight == 0)
            return 0f;

        var forward = cameraForward.LengthSquared() > float.Epsilon
            ? Vector3.Normalize(cameraForward)
            : Vector3.UnitX;
        return EstimateProjectedDiameterPixelsNormalized(
            cameraPosition,
            forward,
            sphereCenter,
            sphereRadius,
            verticalProjectionScale,
            viewportHeight);
    }

    public static float EstimateProjectedDiameterPixelsNormalized(
        Vector3 cameraPosition,
        Vector3 normalizedCameraForward,
        Vector3 sphereCenter,
        float sphereRadius,
        float verticalProjectionScale,
        uint viewportHeight)
    {
        if (sphereRadius <= 0f || verticalProjectionScale <= 0f || viewportHeight == 0)
            return 0f;

        var centerDepth = Vector3.Dot(sphereCenter - cameraPosition, normalizedCameraForward);
        if (centerDepth <= sphereRadius)
            return float.PositiveInfinity;

        var nearestDepth = MathF.Max(1f, centerDepth - sphereRadius);
        return sphereRadius * verticalProjectionScale * viewportHeight / nearestDepth;
    }

    public static bool IsBelowPixelThreshold(
        Vector3 cameraPosition,
        Vector3 cameraForward,
        Vector3 sphereCenter,
        float sphereRadius,
        float verticalProjectionScale,
        uint viewportHeight,
        float minimumDiameterPixels) =>
        minimumDiameterPixels > 0f &&
        EstimateProjectedDiameterPixels(
            cameraPosition,
            cameraForward,
            sphereCenter,
            sphereRadius,
            verticalProjectionScale,
            viewportHeight) < minimumDiameterPixels;

    public static bool IsBelowPixelThresholdNormalized(
        Vector3 cameraPosition,
        Vector3 normalizedCameraForward,
        Vector3 sphereCenter,
        float sphereRadius,
        float verticalProjectionScale,
        uint viewportHeight,
        float minimumDiameterPixels) =>
        minimumDiameterPixels > 0f &&
        EstimateProjectedDiameterPixelsNormalized(
            cameraPosition,
            normalizedCameraForward,
            sphereCenter,
            sphereRadius,
            verticalProjectionScale,
            viewportHeight) < minimumDiameterPixels;

    /// <summary>Fast threshold check when the camera-to-sphere offset is already available.</summary>
    public static bool IsBelowPixelThresholdNormalizedFromOffset(
        Vector3 cameraToSphere,
        Vector3 normalizedCameraForward,
        float sphereRadius,
        float verticalProjectionScale,
        uint viewportHeight,
        float minimumDiameterPixels)
    {
        if (!(minimumDiameterPixels > 0f))
            return false;
        if (sphereRadius <= 0f || verticalProjectionScale <= 0f || viewportHeight == 0)
            return true;

        var centerDepth = Vector3.Dot(cameraToSphere, normalizedCameraForward);
        if (centerDepth <= sphereRadius)
            return false;

        var nearestDepth = MathF.Max(1f, centerDepth - sphereRadius);
        return sphereRadius * verticalProjectionScale * viewportHeight <
            minimumDiameterPixels * nearestDepth;
    }
}
