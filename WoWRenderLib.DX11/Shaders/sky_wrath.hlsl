cbuffer SkyParameters : register(b0)
{
    float4 skyTopColor;
    float4 skyMiddleColor;
    float4 skyBand1Color;
    float4 skyBand2Color;
    float4 skySmogColor;
    float4 skyFogColor;
    float4 cameraFront;
    float4 cameraRight;
    float4 cameraUp;
    float4 projectionScale;
    float4 skyGlowParameters;
};

struct VSInput
{
    float3 Position : POSITION;
    float4 Color : COLOR0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
};

VSOutput VS_Main(VSInput input)
{
    VSOutput output;
    // DNSky is centred on the camera. Push its projected depth to the back
    // while retaining the client's 24-segment triangle geometry and colors.
    float3 viewPosition = float3(
        dot(input.Position, cameraRight.xyz),
        dot(input.Position, cameraUp.xyz),
        dot(input.Position, cameraFront.xyz));
    output.Position = float4(
        viewPosition.x / projectionScale.x,
        viewPosition.y / projectionScale.y,
        viewPosition.z * skyGlowParameters.z,
        viewPosition.z);
    output.Color = input.Color;
    return output;
}

float4 PS_Main(VSOutput input) : SV_TARGET
{
    return input.Color;
}
