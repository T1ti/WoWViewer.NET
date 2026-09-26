cbuffer PerFrame : register(b0)
{
    float4x4 projection_matrix;
    float4x4 view_matrix;
};

struct VSIn
{
    float3 position : POSITION;
    float4 instanceRow0 : TEXCOORD0;
    float4 instanceRow1 : TEXCOORD1;
    float4 instanceRow2 : TEXCOORD2;
    float4 instanceRow3 : TEXCOORD3;
    float4 color : TEXCOORD4;
};
struct VSOut
{
    float4 pos : SV_POSITION;
    float4 color : COLOR0;
};

VSOut VS_Main(VSIn input)
{
    VSOut o;
    // The instance stream contains System.Numerics row-vector matrices.
    float4x4 model_matrix = transpose(float4x4(
        input.instanceRow0, input.instanceRow1,
        input.instanceRow2, input.instanceRow3));
    float4 worldPos = mul(model_matrix, float4(input.position, 1.0f));
    float4 viewPos = mul(view_matrix, worldPos);
    o.pos = mul(projection_matrix, viewPos);
    o.color = input.color;
    return o;
}

float4 PS_Main(VSOut i) : SV_Target
{
    return i.color;
}
