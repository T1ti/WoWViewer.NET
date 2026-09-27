cbuffer GizmoParameters : register(b0)
{
    row_major float4x4 WorldViewProjection;
    float4 Highlight; // handle, mode, scale enabled, reserved
    row_major float4x4 ScreenToLocal;
    float4 CameraLocal;
};
struct VertexOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float3 LocalPosition : TEXCOORD0;
    nointerpolation uint Handle : TEXCOORD1;
};
VertexOutput VS_Main(float4 vertex : POSITION)
{
    VertexOutput output;
    uint handle = (uint)vertex.w;
    float3 position = vertex.xyz;
    if (handle == 8 || handle == 15)
        position = mul(float4(position, 0), ScreenToLocal).xyz;
    output.Position = mul(float4(position, 1), WorldViewProjection);
    output.LocalPosition = position;
    output.Handle = handle;
    float3 color = float3(0.95, 0.97, 1);
    if (handle == 1 || handle == 9 || handle == 12) color = float3(1, 0.22, 0.18);
    if (handle == 2 || handle == 10 || handle == 13) color = float3(0.25, 0.95, 0.36);
    if (handle == 3 || handle == 11 || handle == 14) color = float3(0.22, 0.55, 1);
    if (handle == 5) color = float3(0.9, 0.8, 0.25);
    if (handle == 6) color = float3(0.2, 0.8, 0.8);
    if (handle == 7) color = float3(0.8, 0.3, 0.8);
    if (handle == (uint)Highlight.x) color = float3(1, 0.88, 0.2);
    if (handle == 17) color = float3(1, 0.55, 0.05);
    output.Color = float4(color, handle == 17 ? 0.24 : 1);
    return output;
}
float4 PS_Main(VertexOutput input) : SV_TARGET
{
    uint handle = input.Handle;
    bool axisRing = (handle >= 9 && handle <= 11) ||
        (Highlight.y == 1 && handle >= 1 && handle <= 3);
    if (axisRing) clip(dot(input.LocalPosition, CameraLocal.xyz));
    if (Highlight.z == 0 && handle >= 12 && handle <= 15) discard;
    return input.Color;
}
