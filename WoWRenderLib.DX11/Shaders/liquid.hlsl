cbuffer PerObject : register(b0)
{
    float4x4 model_matrix;
    float4x4 view_matrix;
    float4x4 projection_matrix;
    float4 shallowColor;
    float4 deepColor;
    float4 flowParameters;   // time, UV scale, direction, speed
    float4 familyParameters; // water, emissive, reserved, reserved
    float4 lightingAmbient;
    float4 lightingDiffuse;
    float4 oceanCloseColor;
    float4 oceanFarColor;
    float4 riverCloseColor;
    float4 riverFarColor;
    float4 liquidColorParameters; // use LightData colors, river flag, reserved
    float4 depthCoefficients;     // LiquidType.Coefficient[0..3]
    float4 lightDirection;        // normalized world-space exterior light
    float4 liquidAlphaParameters; // ocean shallow/deep, river shallow/deep
};

Texture2D liquidTexture : register(t0);
SamplerState linearWrap : register(s0);

struct VSIn
{
    float3 position : POSITION;
    float depth : TEXCOORD0;
    float2 texCoord : TEXCOORD1;
    float2 cellCoord : TEXCOORD2;
};

struct VSOut
{
    float4 position : SV_POSITION;
    float depth : TEXCOORD0;
    float2 texCoord : TEXCOORD1;
    float2 cellCoord : TEXCOORD2;
    float3 normal : NORMAL;
};

VSOut VS_Main(VSIn input)
{
    VSOut output;
    float4 worldPosition = mul(model_matrix, float4(input.position, 1.0f));
    float4 viewPosition = mul(view_matrix, worldPosition);
    output.position = mul(projection_matrix, viewPosition);
    output.depth = saturate(input.depth);
    output.cellCoord = input.cellCoord;

    float angle = flowParameters.z;
    float2 flow = float2(cos(angle), sin(angle)) *
        ((flowParameters.w + 0.015f) * flowParameters.x);
    output.texCoord = input.texCoord * max(flowParameters.y, 0.001f) + flow;
    output.normal = normalize(mul((float3x3)model_matrix, float3(0.0f, 0.0f, 1.0f)));
    return output;
}

float4 PS_Main(VSOut input) : SV_Target
{
    float4 sampled = liquidTexture.Sample(linearWrap, input.texCoord);
    bool isWater = familyParameters.x > 0.5f;
    // WowLib forwards the MH2O transparency/depth byte unchanged as byte / 255.
    // The reference viewer uses that value from close/shallow to far/deep; it
    // must not be inverted here or deep ocean will receive the coastal color.
    float depthFactor = saturate(input.depth);
    float depthSquared = depthFactor * depthFactor;
    float depthMix = saturate(
        depthCoefficients.x +
        depthCoefficients.y * depthFactor +
        depthCoefficients.z * depthSquared +
        depthCoefficients.w * depthSquared * depthFactor);
    float4 surface = lerp(shallowColor, deepColor, depthMix);
    if (isWater && liquidColorParameters.x > 0.5f)
    {
        float3 closeColor = liquidColorParameters.y > 0.5f
            ? riverCloseColor.rgb
            : oceanCloseColor.rgb;
        float3 farColor = liquidColorParameters.y > 0.5f
            ? riverFarColor.rgb
            : oceanFarColor.rgb;
        // LightData's close/far colors follow the same MH2O convention as the
        // reference viewer: zero is close/shallow and 255 is far/deep.
        surface.rgb = lerp(closeColor, farColor, depthMix);
    }
    float3 litSurface = surface.rgb * sampled.rgb;
    float3 normalizedLight = normalize(lightDirection.xyz);
    float directional = 0.35f + 0.65f * max(dot(input.normal, normalizedLight), 0.0f);
    float3 lighting = lightingAmbient.rgb + lightingDiffuse.rgb * directional;
    litSurface *= max(lighting, 0.05.xxx);

    if (familyParameters.y > 0.5f)
    {
        // Magma/fel-like families intentionally remain opaque and emissive in
        // this first pass; specialized client material permutations are a
        // later phase.
        litSurface += surface.rgb * 0.35f;
        return float4(litSurface, 1.0f);
    }

    // This pass has no scene-color/refraction input. LightParams liquid alpha
    // and the animated texture alpha therefore cannot be used as framebuffer
    // coverage: that was the regression that reduced water to a moving ink
    // mask. Coverage remains the material fallback used by the original
    // simple forward path; MH2O depth only adjusts its shallow/deep response.
    float finalCoverage = saturate(surface.a * lerp(0.75f, 1.0f, depthMix));
    return float4(
        litSurface,
        finalCoverage);
}
