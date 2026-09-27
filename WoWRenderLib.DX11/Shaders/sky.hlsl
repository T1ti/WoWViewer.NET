cbuffer SkyParameters : register(b0)
{
    float4 skyTopColor;
    float4 skyMiddleColor;
    float4 skyBand1Color;
    float4 skyBand2Color;
    float4 skySmogColor;
    float4 skyFogColor;
    float4 cameraFront;
    float4 cameraRight;
    float4 cameraUp;
    float4 projectionScale;
    // x: DNSky glow strength, y: sun azimuth in renderer world axes.
    float4 skyGlowParameters;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

VSOutput VS_Main(uint vertexId : SV_VertexID)
{
    VSOutput output;
    float2 position = float2((vertexId << 1) & 2, vertexId & 2);
    output.TexCoord = position;
    output.Position = float4(position * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    return output;
}

float SkyAzimuthGlow(float phase)
{
    // DNSky's six circular azimuth keys, rotated so the sunward ray is 0.125.
    phase = frac(phase);
    if (phase < 0.125)
        return phase / 0.25 + 0.5;
    if (phase < 0.375)
        return (0.375 - phase) / 0.25;
    if (phase < 0.5)
        return -(phase - 0.375) * 4.0;
    if (phase < 0.625)
        return -0.5 - (phase - 0.5) * 1.6;
    if (phase < 0.75)
        return -0.7 + (phase - 0.625) * 1.6;
    if (phase < 0.875)
        return -0.5 + (phase - 0.75) * 4.0;
    return (phase - 0.875) * 4.0;
}

float3 EvaluateSkyRing(float3 band, float azimuthGlow)
{
    float glow = skyGlowParameters.x;
    float3 washed = lerp(band, skyMiddleColor.rgb, glow);
    if (azimuthGlow >= 0.0)
        return lerp(band, washed, (1.0 - azimuthGlow) * glow);
    float3 towardZenith = lerp(washed, skyTopColor.rgb, 0.7 * glow);
    return lerp(washed, towardZenith, -azimuthGlow * glow);
}

float3 EvaluateSkyGradient(float3 direction)
{
    float elevation = direction.z;
    float3 middle = skyMiddleColor.rgb;
    float3 band1 = skyBand1Color.rgb;
    float3 band2 = skyBand2Color.rgb;
    float3 smog = skySmogColor.rgb;
    if (skyGlowParameters.x != 0.0 && dot(direction.xy, direction.xy) > 0.000001)
    {
        float rayAzimuth = atan2(direction.y, direction.x);
        float phase = 0.125 + (skyGlowParameters.y - rayAzimuth) * 0.159154943;
        float azimuthGlow = SkyAzimuthGlow(phase);
        middle = EvaluateSkyRing(middle, azimuthGlow);
        band1 = EvaluateSkyRing(band1, azimuthGlow);
        band2 = EvaluateSkyRing(band2, azimuthGlow);
        smog = EvaluateSkyRing(smog, azimuthGlow);
    }
    // Elevations match the normalized rings in the client sky cone.
    if (elevation >= 0.2728)
        return lerp(middle, skyTopColor.rgb, saturate((elevation - 0.2728) / 0.7272));
    if (elevation >= 0.1479)
        return lerp(band1, middle, (elevation - 0.1479) / 0.1249);
    if (elevation >= 0.03765)
        return lerp(band2, band1, (elevation - 0.03765) / 0.11025);
    if (elevation >= 0.0206)
        return lerp(smog, band2, (elevation - 0.0206) / 0.01705);
    if (elevation >= -0.00727)
        return lerp(skyFogColor.rgb, smog, (elevation + 0.00727) / 0.02787);
    return skyFogColor.rgb;
}

float4 PS_Main(VSOutput input) : SV_TARGET
{
    float2 ndc = float2(input.TexCoord.x * 2.0 - 1.0, 1.0 - input.TexCoord.y * 2.0);
    float3 direction = normalize(
        cameraFront.xyz +
        cameraRight.xyz * ndc.x * projectionScale.x +
        cameraUp.xyz * ndc.y * projectionScale.y);
    return float4(EvaluateSkyGradient(direction), 1.0);
}
