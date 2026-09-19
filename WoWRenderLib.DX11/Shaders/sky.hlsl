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

float3 EvaluateSkyGradient(float elevation)
{
    // Elevations match the normalized rings in the client sky cone.
    if (elevation >= 0.2728)
        return lerp(skyMiddleColor.rgb, skyTopColor.rgb, saturate((elevation - 0.2728) / 0.7272));
    if (elevation >= 0.1479)
        return lerp(skyBand1Color.rgb, skyMiddleColor.rgb, (elevation - 0.1479) / 0.1249);
    if (elevation >= 0.03765)
        return lerp(skyBand2Color.rgb, skyBand1Color.rgb, (elevation - 0.03765) / 0.11025);
    if (elevation >= 0.0206)
        return lerp(skySmogColor.rgb, skyBand2Color.rgb, (elevation - 0.0206) / 0.01705);
    if (elevation >= -0.00727)
        return lerp(skyFogColor.rgb, skySmogColor.rgb, (elevation + 0.00727) / 0.02787);
    return skyFogColor.rgb;
}

float4 PS_Main(VSOutput input) : SV_TARGET
{
    float2 ndc = float2(input.TexCoord.x * 2.0 - 1.0, 1.0 - input.TexCoord.y * 2.0);
    float3 direction = normalize(
        cameraFront.xyz +
        cameraRight.xyz * ndc.x * projectionScale.x +
        cameraUp.xyz * ndc.y * projectionScale.y);
    return float4(EvaluateSkyGradient(direction.z), 1.0);
}
