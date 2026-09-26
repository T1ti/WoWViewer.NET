// Diagnostic WMO collision-only faces. The CPU expands each triangle once and
// supplies barycentrics, keeping the viewport pass to one draw per group.
cbuffer PerObject : register(b0)
{
    float4x4 projection_matrix;
    float4x4 view_matrix;
};

struct VSIn
{
    float3 position : POSITION;
    float2 barycentric : TEXCOORD0;
    float4 instanceRow0 : TEXCOORD1;
    float4 instanceRow1 : TEXCOORD2;
    float4 instanceRow2 : TEXCOORD3;
    float4 instanceRow3 : TEXCOORD4;
};

struct VSOut
{
    float4 position : SV_POSITION;
    float3 barycentric : TEXCOORD0;
};

VSOut VS_Main(VSIn input)
{
    VSOut output;
    float4x4 instanceMatrix = transpose(float4x4(
        input.instanceRow0, input.instanceRow1,
        input.instanceRow2, input.instanceRow3));
    float4 worldPosition = mul(instanceMatrix, float4(input.position, 1.0f));
    output.position = mul(projection_matrix, mul(view_matrix, worldPosition));
    output.barycentric = float3(input.barycentric,
        1.0f - input.barycentric.x - input.barycentric.y);
    return output;
}

float4 PS_Main(VSOut input) : SV_TARGET
{
    // Derivatives keep the three dark edges close to a fixed screen width.
    float3 width = max(fwidth(input.barycentric) * 1.4f, 0.0001f);
    float3 interior = smoothstep(0.0f, width, input.barycentric);
    float fill = min(interior.x, min(interior.y, interior.z));
    return float4(lerp(float3(0.20f, 0.20f, 0.20f),
                       float3(0.52f, 0.52f, 0.52f), fill), 1.0f);
}
