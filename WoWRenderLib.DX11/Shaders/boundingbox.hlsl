cbuffer PerObject : register(b0)
{
    float4x4 projection_matrix;
    float4x4 view_matrix;
    float4x4 model_matrix;
    float4 color;
};

struct VSIn
{
    float3 position : POSITION;
};
struct VSOut
{
    float4 pos : SV_POSITION;
    float4 color : COLOR0;
};

VSOut VS_Main(VSIn input)
{
    VSOut o;
    float4 worldPos = mul(model_matrix, float4(input.position, 1.0f));
    float4 viewPos = mul(view_matrix, worldPos);
    o.pos = mul(projection_matrix, viewPos);
    o.color = color;
    return o;
}

float4 PS_Main(VSOut i) : SV_Target
{
    return i.color;
}
