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
    float4 skyGlowParameters;
};

Texture2D cloudTexture : register(t0);
SamplerState cloudSampler : register(s0);

struct VSInput
{
    float3 Position : POSITION;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
};

VSOutput VS_Main(VSInput input)
{
    VSOutput output;
    float3 viewPosition = float3(
        dot(input.Position, cameraRight.xyz),
        dot(input.Position, cameraUp.xyz),
        dot(input.Position, cameraFront.xyz));
    output.Position = float4(
        viewPosition.x / projectionScale.x,
        viewPosition.y / projectionScale.y,
        viewPosition.z * skyGlowParameters.z,
        viewPosition.z);
    output.TexCoord = input.TexCoord;
    output.Color = input.Color;
    return output;
}

float4 PS_Main(VSOutput input) : SV_TARGET
{
    return cloudTexture.Sample(cloudSampler, input.TexCoord) * input.Color;
}
