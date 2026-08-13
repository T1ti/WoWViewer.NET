#ifndef ADT_LAYER_COUNT
#define ADT_LAYER_COUNT 8
#endif

#ifndef ADT_USE_HEIGHT_TEXTURES
#define ADT_USE_HEIGHT_TEXTURES 1
#endif

cbuffer PerObject : register(b0)
{
    float4x4 model_matrix;
    float4x4 projection_matrix;
    float4x4 rotation_matrix;
    float3 firstPos;
    float _adtObjectPad;
}

cbuffer LayerData : register(b1)
{
    int layerCount;
    float3 lightDirection;
    float3 ambientColor;
    float _adtLayerPad0;
    float3 diffuseColor;
    float _adtLayerPad1;
    float4 heightScales[2]; // [0].xyzw = indices 0-3, [1].xyzw = indices 4-7
    float4 heightOffsets[2];
    float4 layerScales[2];
}

cbuffer AlphaSliceData : register(b2)
{
    uint4 alphaSliceIndices[128];
}

struct ChunkLayerData
{
    float4 heightScales0;
    float4 heightScales1;
    float4 heightOffsets0;
    float4 heightOffsets1;
    float4 layerScales0;
    float4 layerScales1;
};

cbuffer ChunkLayerDataBuffer : register(b3)
{
    ChunkLayerData chunkLayerData[256];
}

Texture2D diffuseLayers[8] : register(t0); // t0..t7
Texture2D heightLayers[8] : register(t8); // t8..t15
Texture2DArray alphaLayers : register(t16);

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
    nointerpolation uint ChunkIndex : TEXCOORD1;
};

VSOut VS_Main(VSIn input, uint vertexId : SV_VertexID)
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
    o.ChunkIndex = vertexId / 145;
    return o;
}

float GetChunk8(float4 first, float4 second, uint i)
{
    if (i < 4)
        return first[i];
    else
        return second[i - 4];
}

uint2 GetAlphaSlices(uint chunkIndex)
{
    uint4 packedSlices = alphaSliceIndices[chunkIndex / 2];
    return (chunkIndex & 1) == 0 ? packedSlices.xy : packedSlices.zw;
}

float4 PS_Main(VSOut i) : SV_Target
{
    float4 in_vertexColor = i.VColor;
    float2 uvMod = frac(i.TexCoord);
    uint2 alphaSlices = GetAlphaSlices(i.ChunkIndex);
    ChunkLayerData chunkLayers = chunkLayerData[i.ChunkIndex];

#if ADT_LAYER_COUNT > 1
    float4 alpha0 = alphaLayers.Sample(linearClamp, float3(uvMod, alphaSlices.x));
#else
    float4 alpha0 = float4(0.0f, 0.0f, 0.0f, 0.0f);
#endif
#if ADT_LAYER_COUNT > 4
    float4 alpha1 = alphaLayers.Sample(linearClamp, float3(uvMod, alphaSlices.y));
#else
    float4 alpha1 = float4(0.0f, 0.0f, 0.0f, 0.0f);
#endif

    float alphas[8];
    alphas[0] = 1.0f;
    alphas[1] = alpha0.g;
    alphas[2] = alpha0.b;
    alphas[3] = alpha0.a;
    alphas[4] = alpha1.r;
    alphas[5] = alpha1.g;
    alphas[6] = alpha1.b;
    alphas[7] = alpha1.a;

    uint idx;
    float alpha_sum = 0.0f;
    [unroll]
    for (idx = 1; idx < ADT_LAYER_COUNT; idx++)
        alpha_sum += alphas[idx];

    float layer_weights[ADT_LAYER_COUNT];
    layer_weights[0] = 1.0f - saturate(alpha_sum);
    [unroll]
    for (idx = 1; idx < ADT_LAYER_COUNT; idx++)
        layer_weights[idx] = alphas[idx];

    float layer_pcts[ADT_LAYER_COUNT];
#if ADT_USE_HEIGHT_TEXTURES
    [unroll]
    for (idx = 0; idx < ADT_LAYER_COUNT; idx++)
    {
        float2 tc = i.TexCoord * (8.0f / GetChunk8(
            chunkLayers.layerScales0, chunkLayers.layerScales1, idx));
        float height_val = heightLayers[idx].Sample(linearWrap, tc).a;
        layer_pcts[idx] = layer_weights[idx] *
            (height_val * GetChunk8(
                chunkLayers.heightScales0, chunkLayers.heightScales1, idx) +
             GetChunk8(chunkLayers.heightOffsets0, chunkLayers.heightOffsets1, idx));
    }

    float max_pct = 0.0f;
    [unroll]
    for (idx = 0; idx < ADT_LAYER_COUNT; idx++)
        max_pct = max(max_pct, layer_pcts[idx]);

    [unroll]
    for (idx = 0; idx < ADT_LAYER_COUNT; idx++)
        layer_pcts[idx] *= 1.0f - saturate(max_pct - layer_pcts[idx]);
#else
    [unroll]
    for (idx = 0; idx < ADT_LAYER_COUNT; idx++)
        layer_pcts[idx] = layer_weights[idx];
#endif

    float pct_sum = 0.0f;
    [unroll]
    for (idx = 0; idx < ADT_LAYER_COUNT; idx++)
        pct_sum += layer_pcts[idx];

    [unroll]
    for (idx = 0; idx < ADT_LAYER_COUNT; idx++)
        layer_pcts[idx] /= max(pct_sum, 0.000001f);

    float3 final_color = float3(0.0f, 0.0f, 0.0f);
    [unroll]
    for (idx = 0; idx < ADT_LAYER_COUNT; idx++)
    {
        float2 tc = i.TexCoord * (8.0f / GetChunk8(
            chunkLayers.layerScales0, chunkLayers.layerScales1, idx));
        float4 layer_sample = diffuseLayers[idx].Sample(linearWrap, tc);
        final_color += layer_sample.rgb * layer_pcts[idx];
    }

    float diffuse = max(dot(normalize(i.Normal), normalize(lightDirection)), 0.0f);
    float3 lighting = saturate(ambientColor + diffuseColor * diffuse);
    return float4(final_color * in_vertexColor.rgb * 2.0f * lighting, 1.0f);
}
