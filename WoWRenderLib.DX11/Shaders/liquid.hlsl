cbuffer PerObject : register(b0)
{
    float4x4 model_matrix;
    float4x4 view_matrix;
    float4x4 projection_matrix;
    float4 shallowColor;
    float4 deepColor;
    float4 flowParameters;   // time, UV scale, direction, speed
    float4 familyParameters; // water, emissive, real texture loaded, reserved
    float4 lightingAmbient;
    float4 lightingDiffuse;
    float4 oceanCloseColor;
    float4 oceanFarColor;
    float4 riverCloseColor;
    float4 riverFarColor;
    float4 liquidColorParameters; // use LightData colors, river flag, use LightParams alpha, reserved
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
    if (isWater && liquidColorParameters.z > 0.5f)
    {
        float closeAlpha = liquidColorParameters.y > 0.5f
            ? liquidAlphaParameters.z
            : liquidAlphaParameters.x;
        float farAlpha = liquidColorParameters.y > 0.5f
            ? liquidAlphaParameters.w
            : liquidAlphaParameters.y;
        surface.a = lerp(closeAlpha, farAlpha, depthMix);
    }

    // LiquidType's animated water texture is wave data, not an RGB albedo.
    // Preserve the LightData hue and use only its luminance for subtle wave
    // brightness. An unresolved asset still uses its full magenta diagnostic.
    bool hasLoadedTexture = familyParameters.z > 0.5f;
    float waveLuminance = dot(sampled.rgb, float3(0.2126f, 0.7152f, 0.0722f));
    float waveDetail = lerp(0.9f, 1.1f, saturate(waveLuminance));
    float3 litSurface = isWater && hasLoadedTexture
        ? surface.rgb * waveDetail
        : surface.rgb * sampled.rgb;
    float3 normalizedLight = normalize(lightDirection.xyz);
    // Match the reference exterior-light composition: the LightData liquid
    // tint is modulated by ambient plus the Lambertian direct contribution.
    float directional = max(dot(input.normal, normalizedLight), 0.0f);
    float3 lighting = lightingAmbient.rgb + lightingDiffuse.rgb * directional;
    litSurface *= lighting;

    if (familyParameters.y > 0.5f)
    {
        // Magma/fel-like families intentionally remain opaque and emissive in
        // this first pass; specialized client material permutations are a
        // later phase.
        litSurface += surface.rgb * 0.35f;
        return float4(litSurface, 1.0f);
    }

    // Source-alpha blending is the simple-pass equivalent of mixing the water
    // tint over the existing scene. Never multiply by sampled.a: its sparse
    // wave mask caused the transparency regression.
    float finalCoverage = saturate(surface.a);
    return float4(
        litSurface,
        finalCoverage);
}
