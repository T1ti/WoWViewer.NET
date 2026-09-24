cbuffer RibbonConstants : register(b0)
{
    float4x4 projectionMatrix;
    float4x4 viewMatrix;
    float4x4 modelMatrix;
    float alphaReference;
    float3 padding;
};

Texture2D ribbonTexture : register(t0);
SamplerState ribbonSampler : register(s0);

struct VSInput
{
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
};

struct VSOutput
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
};

VSOutput VS_Main(VSInput input)
{
    VSOutput output;
    output.position = mul(projectionMatrix,
        mul(viewMatrix, mul(modelMatrix, float4(input.position, 1.0))));
    output.uv = input.uv;
    output.color = input.color;
    return output;
}

float4 PS_Main(VSOutput input) : SV_TARGET
{
    float4 color = ribbonTexture.Sample(ribbonSampler, input.uv) * input.color;
    if (alphaReference >= 0.0 && color.a < alphaReference)
        discard;
    return color;
}
