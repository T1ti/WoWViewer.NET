// Material alpha participates in scene rendering, but a completed world view
// is opaque. The alpha-only write mask preserves the finished RGB exactly.
float4 VS_Main(uint vertexId : SV_VertexID) : SV_POSITION
{
    float2 corner = float2((vertexId << 1) & 2, vertexId & 2);
    return float4(corner * float2(2, -2) + float2(-1, 1), 0, 1);
}

float4 PS_Main() : SV_TARGET
{
    return float4(0, 0, 0, 1);
}
