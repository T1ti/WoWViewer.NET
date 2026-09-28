cbuffer CelestialParameters : register(b0)
{
    float4 bodyCenter;
    float4 billboardHorizontal;
    float4 billboardVertical;
    float4 cameraRight;
    float4 cameraUp;
    float4 cameraFront;
    float4 projectionTerms;
};

Texture2D bodyTexture : register(t0);
SamplerState bodySampler : register(s0);

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
    float3 relativePosition = bodyCenter.xyz +
        billboardHorizontal.xyz * input.Position.y +
        billboardVertical.xyz * input.Position.z;
    float3 viewPosition = float3(
        dot(relativePosition, cameraRight.xyz),
        dot(relativePosition, cameraUp.xyz),
        dot(relativePosition, cameraFront.xyz));
    output.Position = float4(
        viewPosition.x * projectionTerms.x,
        viewPosition.y * projectionTerms.y,
        viewPosition.z * projectionTerms.z + projectionTerms.w,
        viewPosition.z);
    output.TexCoord = input.TexCoord;
    output.Color = input.Color;
    return output;
}

float4 PS_Main(VSOutput input) : SV_TARGET
{
    return bodyTexture.Sample(bodySampler, input.TexCoord) * input.Color;
}
