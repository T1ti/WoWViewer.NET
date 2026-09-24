// Full-screen 3.3.5 glow: half-size scene, two Gaussian blur passes, then
// clamp(mix(scene, blur, z) + blur * blur * w).
cbuffer GlowParameters : register(b0)
{
    float2 texelStep;
    float glowMix;
    float glowStrength;
    int passIndex;
    float3 padding;
};

Texture2D sourceTexture : register(t0);
Texture2D blurTexture : register(t1);
SamplerState linearSampler : register(s0);

struct VertexOutput
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

VertexOutput VS_Main(uint vertexId : SV_VertexID)
{
    VertexOutput output;
    output.uv = float2((vertexId << 1) & 2, vertexId & 2);
    output.position = float4(output.uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return output;
}

float4 PS_Main(VertexOutput input) : SV_TARGET
{
    float3 scene = sourceTexture.Sample(linearSampler, input.uv).rgb;
    if (passIndex == 0)
        return float4(scene, 1);

    if (passIndex == 3)
    {
        float3 blur = blurTexture.Sample(linearSampler, input.uv).rgb;
        return float4(saturate(lerp(scene, blur, glowMix) +
            blur * blur * glowStrength), 1);
    }

    // Radius 4, sigma 2, normalized for the centre and both sides.
    static const float weights[5] =
        { 0.20416369, 0.18017382, 0.12383154, 0.06628225, 0.02763055 };
    float3 result = scene * weights[0];
    [unroll]
    for (int tap = 1; tap <= 4; tap++)
    {
        float2 offset = texelStep * tap;
        result += sourceTexture.Sample(linearSampler, input.uv + offset).rgb * weights[tap];
        result += sourceTexture.Sample(linearSampler, input.uv - offset).rgb * weights[tap];
    }
    return float4(result, 1);
}
