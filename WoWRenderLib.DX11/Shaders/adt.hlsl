cbuffer PerObject : register(b0)
{
    float4x4 model_matrix;
    float4x4 projection_matrix;
    float4x4 rotation_matrix;
    float3 firstPos;
    float _pad0;
}

cbuffer LayerData : register(b1)
{
    int layerCount;
    float3 lightDirection;
    float4 heightScales[2]; // [0].xyzw = indices 0-3, [1].xyzw = indices 4-7
    float4 heightOffsets[2];
    float4 layerScales[2];
}

Texture2D diffuseLayers[8] : register(t0); // t0..t7
Texture2D heightLayers[8] : register(t8); // t8..t15
Texture2D alphaLayers[2] : register(t16); // t16..t17

SamplerState linearWrap : register(s0);
SamplerState linearClamp : register(s1);

float Get8(float4 arr[2], uint i)
{
    if (i < 4)
        return arr[0][i];
    else
        return arr[1][i - 4];
}

struct VSIn
{
    float3 position : POSITION;
    float3 normal   : NORMAL;
    float2 texCoord : TEXCOORD0;
    float4 color    : COLOR0;
};

struct VSOut
{
    float4 pos     : SV_POSITION;
    float2 TexCoord: TEXCOORD0;
    float4 VColor  : COLOR0;
    float3 Normal  : NORMAL;
};

VSOut VS_Main(VSIn input)
{
    VSOut o;
    float3 posOffset = input.position;
    float4 worldPos = mul(rotation_matrix, float4(posOffset, 1.0f));
    worldPos = mul(model_matrix, worldPos);
    o.pos = mul(projection_matrix, worldPos);
    o.TexCoord = input.texCoord;
    float3x3 normalMatrix = (float3x3) model_matrix;
    o.Normal = normalize(mul(normalMatrix, input.normal));
    o.VColor = input.color;
    return o;
}

float4 PS_Main(VSOut i) : SV_Target
{
    float4 in_vertexColor = i.VColor;

    float2 uvMod = frac(i.TexCoord);
    float4 alpha0 = alphaLayers[0].Sample(linearClamp, uvMod);
    float4 alpha1 = alphaLayers[1].Sample(linearClamp, uvMod);

    float alphas[8];
    alphas[0] = 1.0f;
    alphas[1] = alpha0.g;
    alphas[2] = alpha0.b;
    alphas[3] = alpha0.a;
    alphas[4] = alpha1.r;
    alphas[5] = alpha1.g;
    alphas[6] = alpha1.b;
    alphas[7] = alpha1.a;

    float alpha_sum = alphas[1] + alphas[2] + alphas[3] + alphas[4]
                    + alphas[5] + alphas[6] + alphas[7];

    float layer_weights[8];
    layer_weights[0] = 1.0f - saturate(alpha_sum);

    uint idx;
    [unroll]
    for (idx = 1; idx < 8; idx++)
        layer_weights[idx] = alphas[idx];

    float layer_pcts[8];
    [unroll]
    for (idx = 0; idx < 8; idx++)
    {
        float2 tc = i.TexCoord * (8.0f / Get8(layerScales, idx));
        float height_val = heightLayers[idx].Sample(linearWrap, tc).a;
        layer_pcts[idx] = layer_weights[idx] * (height_val * Get8(heightScales, idx) + Get8(heightOffsets, idx));
    }

    float max_pct = 0.0f;
    [unroll]
    for (idx = 0; idx < 8; idx++)
        max_pct = max(max_pct, layer_pcts[idx]);

    [unroll]
    for (idx = 0; idx < 8; idx++)
        layer_pcts[idx] = layer_pcts[idx] * (1.0f - saturate(max_pct - layer_pcts[idx]));

    float pct_sum = 0.0f;
    [unroll]
    for (idx = 0; idx < 8; idx++)
        pct_sum += layer_pcts[idx];

    [unroll]
    for (idx = 0; idx < 8; idx++)
        layer_pcts[idx] = layer_pcts[idx] / pct_sum;

    float3 final_color = float3(0.0f, 0.0f, 0.0f);
    [unroll]
    for (idx = 0; idx < 8; idx++)
    {
        float2 tc = i.TexCoord * (8.0f / Get8(layerScales, idx));
        float4 layer_sample = diffuseLayers[idx].Sample(linearWrap, tc);
        final_color += layer_sample.rgb * layer_pcts[idx];
    }

    float diffuse = max(dot(normalize(i.Normal), normalize(lightDirection)), 0.0f);
    float ambientStrength = 0.3f;
    float3 ambient = ambientStrength * float3(1.0f, 1.0f, 1.0f);
    float3 lighting = ambient + diffuse;

    return float4(final_color * in_vertexColor.rgb * 2.0f * lighting, 1.0f);
}
