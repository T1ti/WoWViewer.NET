cbuffer PerObject : register(b0)
{
    float4x4 model_matrix;
    float4x4 view_matrix;
    float4x4 projection_matrix;
    float4 shallowColor;
    float4 deepColor;
    float4 flowParameters;   // time, UV scale, direction, speed
    float4 familyParameters; // water, emissive, real texture loaded, WMO liquid
    float4 lightingAmbient;
    float4 lightingDiffuse;
    float4 oceanCloseColor;
    float4 oceanFarColor;
    float4 riverCloseColor;
    float4 riverFarColor;
    float4 liquidColorParameters; // use LightData colors, river flag, use LightParams alpha, WMO interior
    float4 depthCoefficients;     // LiquidType.Coefficient[0..3]
    float4 lightDirection;        // normalized world-space exterior light
    float4 liquidAlphaParameters; // ocean shallow/deep, river shallow/deep
    float4 wmoWaterColor; // exterior environment tint, or white indoors
    float4 wmoParameters; // basic class, texture rotation, shallow/deep alpha
    float4 nativeParameters; // 12340 program, UV scale, rotation, gradient depth scale
    float4 nativeOffset; // magma scroll, gradient U, generated WMO UV
    float4 nativeVertexColor;
    float4 nativeSpecular; // CM2Lighting specular RGB, exponent 6
};

cbuffer WrathFog : register(b4)
{
    float4 fogParameters;
    float4 fogColor;
};

Texture2D liquidTexture : register(t0);
Texture2D depthGradient : register(t1);
SamplerState linearWrap : register(s0);
SamplerState linearClamp : register(s1);

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
    float fogVisibility : TEXCOORD3;
    float4 primaryColor : COLOR0;
    float3 secondaryColor : COLOR1;
    float2 gradientCoord : TEXCOORD4;
};

VSOut VS_Main(VSIn input)
{
    VSOut output;
    float4 worldPosition = mul(model_matrix, float4(input.position, 1.0f));
    float4 viewPosition = mul(view_matrix, worldPosition);
    output.position = mul(projection_matrix, viewPosition);
    float linearVisibility = max(viewPosition.z * fogParameters.x + fogParameters.y, 0.0f);
    output.fogVisibility = fogParameters.w > 0.5f
        ? min(pow(linearVisibility, fogParameters.z), 1.0f) : 1.0f;
    output.depth = saturate(input.depth);
    output.cellCoord = input.cellCoord;
    output.primaryColor = 0;
    output.secondaryColor = 0;
    output.gradientCoord = 0;

    if (nativeParameters.x > 0.5f)
    {
        float2 uv = input.texCoord;
        // The native WMO emitter generates coordinates after applying the
        // placement's rotation, with its translation removed.
        if (nativeOffset.w > 0.5f)
            uv = mul((float3x3)model_matrix, float3(uv, 0)).xy;
        output.normal = mul((float3x3)model_matrix, float3(0, 0, 1));
        output.gradientCoord = float2(nativeOffset.z, input.depth * nativeParameters.w);
        if (nativeParameters.x > 2.5f)
        {
            output.texCoord = uv + nativeOffset.xy;
            output.primaryColor = nativeVertexColor;
        }
        else
        {
            float cs = cos(nativeParameters.z), sn = sin(nativeParameters.z);
            uv *= nativeParameters.y;
            output.texCoord = float2(uv.x * cs - uv.y * sn, uv.x * sn + uv.y * cs);
            float3 toLight = lightDirection.xyz;
            float3 ambient = lightingAmbient.rgb, diffuse = lightingDiffuse.rgb;
            if (liquidColorParameters.w > 0.5f)
            {
                toLight = float3(0, 0, 1);
                ambient = 0;
                diffuse = 1;
            }
            // vsLiquidWater*: primary = vertex * (ambient + diffuse * saturate(N.L)).
            // Normal transformation is not normalized by the reference VS.
            output.primaryColor = nativeVertexColor * float4(
                ambient + diffuse * saturate(dot(output.normal, toLight)), 1);
            if (nativeParameters.x > 1.5f)
            {
                float3 normalView = mul((float3x3)view_matrix, output.normal);
                float3 lightView = mul((float3x3)view_matrix, toLight);
                float3 halfVector = normalize(lightView - normalize(viewPosition.xyz));
                output.secondaryColor = nativeSpecular.rgb * pow(
                    max(dot(normalView, halfVector), 0), nativeSpecular.w);
            }
        }
        return output;
    }

    if (familyParameters.w > 0.5f)
    {
        if (wmoParameters.x >= 2.0f)
            output.texCoord = input.texCoord;
        else
        {
            float2 p = input.texCoord * flowParameters.y;
            float cs = cos(wmoParameters.y);
            float sn = sin(wmoParameters.y);
            output.texCoord = float2(p.x * cs - p.y * sn,
                p.x * sn + p.y * cs);
        }
    }
    else
    {
        float angle = flowParameters.z;
        float2 flow = float2(cos(angle), sin(angle)) *
            ((flowParameters.w + 0.015f) * flowParameters.x);
        output.texCoord = input.texCoord * max(flowParameters.y, 0.001f) + flow;
    }
    output.normal = normalize(mul((float3x3)model_matrix, float3(0.0f, 0.0f, 1.0f)));
    return output;
}

float4 PS_Main(VSOut input) : SV_Target
{
    if (nativeParameters.x > 0.5f)
    {
        float4 wave = liquidTexture.Sample(linearWrap, input.texCoord);
        float3 rgb;
        float alpha;
        if (nativeParameters.x > 2.5f)
        {
            // psLiquidMagma: vertex RGB * texture RGB; alpha is always one.
            rgb = input.primaryColor.rgb * wave.rgb;
            alpha = 1;
        }
        else
        {
            float4 gradient = depthGradient.Sample(linearClamp, input.gradientCoord);
            // psLiquidWaterNoSpec: primary * depth gradient + surface RGB.
            rgb = input.primaryColor.rgb * gradient.rgb + wave.rgb;
            alpha = input.primaryColor.a * gradient.a;
            // psLiquidWater: the surface alpha scales only this additive glint.
            if (nativeParameters.x > 1.5f)
                rgb += wave.a * (input.secondaryColor + 0.25f);
        }
        return float4(lerp(fogColor.rgb, rgb, input.fogVisibility), alpha);
    }
    float4 sampled = familyParameters.w > 0.5f && familyParameters.z < 0.5f
        ? float4(1.0f, 1.0f, 1.0f, 1.0f)
        : liquidTexture.Sample(linearWrap, input.texCoord);
    if (familyParameters.w > 0.5f)
    {
        if (familyParameters.z < 0.5f)
            return float4(lerp(fogColor.rgb, shallowColor.rgb, input.fogVisibility), 1.0f);
        if (wmoParameters.x >= 2.0f)
            return float4(lerp(fogColor.rgb, shallowColor.rgb * sampled.rgb,
                input.fogVisibility), 1.0f);
        return float4(lerp(fogColor.rgb,
            sampled.rgb + shallowColor.rgb * wmoWaterColor.rgb,
            input.fogVisibility),
            lerp(wmoParameters.z, wmoParameters.w, saturate(input.depth)));
    }
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
    if (liquidColorParameters.w > 0.5f)
        lighting = float3(1.0f, 1.0f, 1.0f);
    litSurface *= lighting;

    if (familyParameters.y > 0.5f)
    {
        // Magma/fel-like families intentionally remain opaque and emissive in
        // this first pass; specialized client material permutations are a
        // later phase.
        litSurface += surface.rgb * 0.35f;
        return float4(lerp(fogColor.rgb, litSurface, input.fogVisibility), 1.0f);
    }

    // Source-alpha blending is the simple-pass equivalent of mixing the water
    // tint over the existing scene. Never multiply by sampled.a: its sparse
    // wave mask caused the transparency regression.
    float finalCoverage = saturate(surface.a);
    return float4(
        lerp(fogColor.rgb, litSurface, input.fogVisibility),
        finalCoverage);
}
