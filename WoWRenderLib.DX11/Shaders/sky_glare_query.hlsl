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

struct VSInput
{
    float3 Position : POSITION;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
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
        viewPosition.z * projectionTerms.z,
        viewPosition.z);
    return output;
}

float4 PS_Main(VSOutput input) : SV_TARGET
{
    return 1.0;
}
