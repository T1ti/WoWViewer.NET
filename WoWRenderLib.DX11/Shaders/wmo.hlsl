// TODO(WMO): Recover the client's material-specific specular program. Wisp's
// basic shader is an approximation; keep its reference path below disabled.
#define WMO_SPECULAR_ENABLED 0

cbuffer PerObject : register(b0)
{
    float4x4 projection_matrix;
    float4x4 view_matrix;
    float4x4 model_matrix;

    int vertexShader;
    int pixelShader;
    float2 _pad0;

    float3 lightDirection;
    float alphaRef;
    float3 ambientColor;
    float _pad1;
    float3 diffuseColor;
    int useLegacyLighting;
    float3 sidnColor;
    float _pad2;
    float3 specularColor;
    int lightingMode;
    float3 rootAmbientColor;
    int unifiedMocv;
    float3 windowAmbientColor;
    float _pad3;
    float3 windowDiffuseColor;
    float _pad4;
}


Texture2D texture1 : register(t0);
Texture2D texture2 : register(t1);
Texture2D texture3 : register(t2);
Texture2D texture4 : register(t3);
Texture2D texture5 : register(t4);
Texture2D texture6 : register(t5);
Texture2D texture7 : register(t6);
Texture2D texture8 : register(t7);
Texture2D texture9 : register(t8);

SamplerState linearWrap : register(s0);

struct VSIn
{
    // Buffer 0
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 texCoord : TEXCOORD0;
    float2 texCoord2 : TEXCOORD1;
    float2 texCoord3 : TEXCOORD2;
    float2 texCoord4 : TEXCOORD3;
    float4 color1 : COLOR0;
    float4 color2 : COLOR1;
    float4 color3 : COLOR2;
    
    // Buffer 1
    float4 instanceRow0 : TEXCOORD4;
    float4 instanceRow1 : TEXCOORD5;
    float4 instanceRow2 : TEXCOORD6;
    float4 instanceRow3 : TEXCOORD7;
};

struct VSOut
{
    float4 pos : SV_POSITION;
    float3 Normal : NORMAL;
    float2 TexCoord : TEXCOORD0;
    float2 TexCoord2 : TEXCOORD1;
    float2 TexCoord3 : TEXCOORD2;
    float2 TexCoord4 : TEXCOORD3;
    float4 vColor1 : COLOR0;
    float4 vColor2 : COLOR1;
    float4 vColor3 : COLOR2;
    float3 LitColor : TEXCOORD4;
    float3 SpecularColor : TEXCOORD5;
};

float2 posToTexCoord(float3 vertexPosInView, float3 n)
{
    float3 viewVecNormalized = normalize(vertexPosInView);
    float3 reflection = reflect(viewVecNormalized, normalize(n));
    float3 temp = float3(reflection.x, reflection.y, reflection.z + 1.0f);
    return normalize(temp).xy * 0.5f + float2(0.5f, 0.5f);
}

VSOut VS_Main(VSIn input)
{
    VSOut o;

    float4x4 instanceMatrix = float4x4(
        input.instanceRow0,
        input.instanceRow1,
        input.instanceRow2,
        input.instanceRow3
    );
    
    instanceMatrix = transpose(instanceMatrix);

    float4 worldPos = mul(instanceMatrix, float4(input.position, 1.0f));
    
    float4 viewPos = mul(view_matrix, worldPos);
    o.pos = mul(projection_matrix, viewPos);

    // The sun direction is in world space. Keep the normal in that space for
    // lighting; only the reflection lookup needs a view-space normal.
    float3x3 mv3 = (float3x3) instanceMatrix;

    float3x3 invMV3;
    invMV3[0][0] = mv3[1][1] * mv3[2][2] - mv3[1][2] * mv3[2][1];
    invMV3[0][1] = -(mv3[0][1] * mv3[2][2] - mv3[0][2] * mv3[2][1]);
    invMV3[0][2] = mv3[0][1] * mv3[1][2] - mv3[0][2] * mv3[1][1];
    invMV3[1][0] = -(mv3[1][0] * mv3[2][2] - mv3[1][2] * mv3[2][0]);
    invMV3[1][1] = mv3[0][0] * mv3[2][2] - mv3[0][2] * mv3[2][0];
    invMV3[1][2] = -(mv3[0][0] * mv3[1][2] - mv3[0][2] * mv3[1][0]);
    invMV3[2][0] = mv3[1][0] * mv3[2][1] - mv3[1][1] * mv3[2][0];
    invMV3[2][1] = -(mv3[0][0] * mv3[2][1] - mv3[0][1] * mv3[2][0]);
    invMV3[2][2] = mv3[0][0] * mv3[1][1] - mv3[0][1] * mv3[1][0];

    float det = mv3[0][0] * invMV3[0][0]
          + mv3[0][1] * invMV3[1][0]
          + mv3[0][2] * invMV3[2][0];
    invMV3 = invMV3 * (1.0f / det);

    float3x3 normalMatrix = transpose(invMV3);
    o.Normal = normalize(mul(normalMatrix, input.normal));
    float3 viewNormal = normalize(mul((float3x3) view_matrix, o.Normal));

    // Missing primary MOCV was filled at load time; a genuine black MOCV
    // remains black. Wisp's legacy MapObj programs combine per vertex.
    float3 mocv = input.color1.rgb;
    float nDotL = max(dot(o.Normal, normalize(lightDirection)), 0.0f);
    if (useLegacyLighting != 0)
    {
        float3 bankAmbient = lightingMode == 3 ? rootAmbientColor
            : lightingMode == 2 ? windowAmbientColor : ambientColor;
        float3 bankDiffuse = lightingMode == 3 ? float3(0.0f, 0.0f, 0.0f)
            : lightingMode == 2 ? windowDiffuseColor : diffuseColor;
        float3 lightTerm = saturate(bankAmbient + bankDiffuse * nDotL);
        // Wisp's c29 is already halved in byte space on the CPU. Combining it
        // here lets the final 2x texture modulation tint the night glow.
        // Noggit reference: add full SIDN RGB after texturing instead.
        o.LitColor = lightingMode == 0 ? mocv
            : unifiedMocv != 0 ? saturate(0.5f * lightTerm + mocv + sidnColor)
            : saturate(mocv * lightTerm + sidnColor);
    }
    else
    {
        float3 lightTerm = saturate(ambientColor + diffuseColor * nDotL);
        o.LitColor = saturate(mocv * lightTerm * 2.0f);
    }

    o.SpecularColor = float3(0.0f, 0.0f, 0.0f);
#if WMO_SPECULAR_ENABLED
    if (useLegacyLighting != 0 && (pixelShader == 1 || pixelShader == 2) && lightingMode != 0)
    {
        float3 viewDirection = length(viewPos.xyz) > 0.0f ? normalize(viewPos.xyz) : float3(0.0f, 0.0f, 0.0f);
        // Intentional direction difference from Wisp's wmo_basic.vert:
        // float3 viewLight = normalize(mul((float3x3)view_matrix, normalize(lightDirection))); // Wisp reference
        // Our renderer reverses that vector for the requested specular source
        // direction in its world axes.
        float3 viewLight = normalize(mul((float3x3)view_matrix, -normalize(lightDirection)));
        float3 halfDirection = viewDirection + viewLight;
        if (dot(halfDirection, halfDirection) > 0.0f)
        {
            float specular = pow(max(dot(viewNormal, -normalize(halfDirection)), 0.0f), 14.0f);
            // Wisp wmo_basic.vert: v_secondary.rgb = spec * u_specular.
            // o.SpecularColor = specular * specularColor; // Wisp reference
            // Intentional difference: reject back-facing highlights and lower
            // their peak strength; Wisp's basic material shader leaves both out.
            o.SpecularColor = specular * specularColor * nDotL * 0.35f;
        }
    }
#endif

    o.vColor1 = input.color1;
    o.vColor2 = input.color2;
    o.vColor3 = input.color3;

    float3 viewSpacePos = viewPos.xyz;

    if (vertexShader == -1)
    {
        o.TexCoord = input.texCoord;
        o.TexCoord2 = input.texCoord2;
        o.TexCoord3 = input.texCoord3;
    }
    else if (vertexShader == 0) // MapObjDiffuse_T1
    {
        o.TexCoord = input.texCoord;
        o.TexCoord2 = input.texCoord2;
        o.TexCoord3 = input.texCoord3;
    }
    else if (vertexShader == 1) // MapObjDiffuse_T1_Refl
    {
        o.TexCoord = input.texCoord;
        // Wisp's legacy Env/EnvMetal shader samples reflect(view,norm).xy
        // directly. Keep the later client's sphere-map UV path separate.
        float3 viewVector = length(viewSpacePos) > 0.0f
            ? normalize(viewSpacePos) : float3(0.0f, 0.0f, 0.0f);
        o.TexCoord2 = useLegacyLighting != 0
            ? reflect(viewVector, viewNormal).xy
            : posToTexCoord(viewSpacePos, viewNormal);
        o.TexCoord3 = input.texCoord3;
    }
    else if (vertexShader == 2) // MapObjDiffuse_T1_T2
    {
        o.TexCoord = input.texCoord;
        // This shader is MapObjDiffuse_T1_Env_T2 in the WMO table: the
        // second stage is an environment map, not the model's second UV set.
        o.TexCoord2 = posToTexCoord(viewSpacePos, viewNormal);
        o.TexCoord3 = input.texCoord3;
    }
    else if (vertexShader == 3) // MapObjSpecular_T1
    {
        o.TexCoord = input.texCoord;
        o.TexCoord2 = input.texCoord2; // not used
        o.TexCoord3 = input.texCoord3; // not used
    }
    else if (vertexShader == 4) // MapObjDiffuse_Comp
    {
        o.TexCoord = input.texCoord;
        o.TexCoord2 = input.texCoord2;
        o.TexCoord3 = input.texCoord3; // not used
    }
    else if (vertexShader == 5) // MapObjDiffuse_Comp_Refl
    {
        o.TexCoord = input.texCoord;
        o.TexCoord2 = input.texCoord2;
        o.TexCoord3 = posToTexCoord(viewSpacePos, viewNormal);
    }
    else if (vertexShader == 6) // MapObjDiffuse_Comp_Terrain
    {
        o.TexCoord = input.texCoord;
        o.TexCoord2 = input.position.xy * -0.239999995f;
        o.TexCoord3 = input.texCoord3; // not used
    }
    else if (vertexShader == 7) // MapObjDiffuse_CompAlpha
    {
        o.TexCoord = input.texCoord;
        o.TexCoord2 = input.position.xy * -0.239999995f;
        o.TexCoord3 = input.texCoord3; // not used
    }
    else if (vertexShader == 8) // MapObjParallax
    {
        o.TexCoord = input.texCoord;
        o.TexCoord2 = input.texCoord2;
        o.TexCoord3 = input.texCoord3;
    }
    else // fallback
    {
        o.TexCoord = float2(0.0f, 1.0f);
        o.TexCoord2 = float2(0.0f, 1.0f);
        o.TexCoord3 = float2(0.0f, 1.0f);
    }

    o.TexCoord4 = input.texCoord4;

    return o;
}

float4 PS_Main(VSOut i) : SV_Target
{
    float4 tex = texture1.Sample(linearWrap, i.TexCoord);
    float4 tex2 = texture2.Sample(linearWrap, i.TexCoord2);
    float4 tex3 = texture3.Sample(linearWrap, i.TexCoord3);
    float4 tex4 = texture4.Sample(linearWrap, i.TexCoord4);

    float3 matDiffuse = float3(0.0f, 0.0f, 0.0f);
    float3 spec = float3(0.0f, 0.0f, 0.0f);
    float3 emissive = float3(0.0f, 0.0f, 0.0f);
    float distFade = 1.0f;
    float finalOpacity = 1.0f;

    if (pixelShader == -1)
    {
        matDiffuse = tex.rgb * tex2.rgb;
        finalOpacity = tex.a;
    }
    else if (pixelShader == 0) // MapObjDiffuse
    {
        // Wisp's wmo_basic.frag is texture * vertex colour * 2x. The old
        // DX11 path ignored MOCV and applied a second fragment normal light.
        matDiffuse = useLegacyLighting != 0 ? tex.rgb : tex.rgb * i.LitColor;
        finalOpacity = tex.a;
    }
    else if (pixelShader == 1) // MapObjSpecular
    {
        matDiffuse = tex.rgb;
#if WMO_SPECULAR_ENABLED
        // Wisp wmo_basic.frag adds v_secondary.rgb without a texture mask.
        // spec = i.SpecularColor; // Wisp reference
        // Intentional difference: this material uses base-texture alpha to
        // control where its highlight appears.
        spec = i.SpecularColor * saturate(tex.a);
#endif
        finalOpacity = tex.a;
    }
    else if (pixelShader == 2) // MapObjMetal
    {
        matDiffuse = tex.rgb;
#if WMO_SPECULAR_ENABLED
        // spec = i.SpecularColor; // Wisp basic shader reference
        // Intentional difference: MapObjMetal uses red * alpha * 4 as its
        // material mask, matching this renderer's material-family mapping.
        spec = i.SpecularColor * saturate(tex.r * tex.a * 4.0f);
#endif
        finalOpacity = tex.a;
    }
    else if (pixelShader == 3) // MapObjEnv
    {
        matDiffuse = tex.rgb;
        emissive = tex2.rgb * tex.a * distFade;
        finalOpacity = 1.0f;
    }
    else if (pixelShader == 4) // MapObjOpaque
    {
        matDiffuse = tex.rgb;
        finalOpacity = 1.0f;
    }
    else if (pixelShader == 5) // MapObjEnvMetal
    {
        matDiffuse = tex.rgb;
        emissive = ((tex.rgb * tex.a) * tex2.rgb) * distFade;
        finalOpacity = 1.0f;
    }
    else if (pixelShader == 6) // MapObjTwoLayerDiffuse
    {
        if (useLegacyLighting != 0)
        {
            // Wisp's legacy composite is mix(texture2, texture1, MOCV2.a).
            // The later client path below also uses texture2.a as a layer mask.
            matDiffuse = lerp(tex2.rgb, tex.rgb, i.vColor2.a);
            finalOpacity = lerp(tex2.a, tex.a, i.vColor2.a);
        }
        else
        {
            float3 layer2 = lerp(tex.rgb, tex2.rgb, tex2.a);
            matDiffuse = lerp(layer2, tex.rgb, i.vColor2.a);
            finalOpacity = tex.a;
        }
    }
    else if (pixelShader == 7) // MapObjTwoLayerEnvMetal
    {
        float4 colorMix = lerp(tex, tex, 1.0f - i.vColor2.a); // NOTE: both inputs are tex
        matDiffuse = colorMix.rgb;
        emissive = (colorMix.rgb * colorMix.a) * tex3.rgb * distFade;
        finalOpacity = tex.a;
    }
    else if (pixelShader == 8) // MapObjTwoLayerTerrain
    {
        float3 layer1 = tex.rgb;
        float3 layer2 = tex2.rgb;
        matDiffuse = lerp(layer2, layer1, i.vColor2.a);
        // spec = calcSpec(tex2.a * (1.0 - vColor2.a));  // TODO
        finalOpacity = tex.a;
    }
    else if (pixelShader == 9) // MapObjDiffuseEmissive
    {
        matDiffuse = tex.rgb;
        emissive = tex2.rgb * tex2.a * i.vColor2.a;
        finalOpacity = tex.a;
    }
    else if (pixelShader == 10) // MapObjMaskedEnvMetal
    {
        float mixFactor = saturate(tex3.a * i.vColor2.a);
        matDiffuse = lerp(
                              lerp((tex.rgb * tex2.rgb) * 2.0f, tex3.rgb, mixFactor),
                              tex.rgb,
                              tex.a
                          );
        finalOpacity = tex.a;
    }
    else if (pixelShader == 11) // MapObjEnvMetalEmissive
    {
        matDiffuse = tex.rgb;
        emissive = ((tex.rgb * tex.a) * tex2.rgb)
                     + ((tex3.rgb * tex3.a) * i.vColor2.a);
        finalOpacity = tex.a;
    }
    else if (pixelShader == 12) // MapObjTwoLayerDiffuseOpaque
    {
        matDiffuse = lerp(tex2.rgb, tex.rgb, i.vColor1.a);
        finalOpacity = 1.0f;
    }
    else if (pixelShader == 13) // MapObjTwoLayerDiffuseEmissive
    {
        float3 t1diffuse = tex2.rgb * (1.0f - tex2.a);
        matDiffuse = lerp(t1diffuse, tex.rgb, i.vColor2.a);
        emissive = (tex2.rgb * tex2.a) * (1.0f - i.vColor2.a);
        finalOpacity = tex.a;
    }
    else if (pixelShader == 14) // MapObjAdditiveMaskedEnvMetal
    {
        matDiffuse = lerp(
                           (tex.rgb * tex2.rgb * 2.0f) + (tex3.rgb * saturate(tex3.a * i.vColor2.a)),
                           tex.rgb,
                           tex.rrr
                       );
        finalOpacity = 1.0f;
    }
    else if (pixelShader == 15) // MapObjTwoLayerDiffuseMod2x
    {
        float3 layer1 = tex.rgb;
        float3 layer2 = lerp(layer1, tex2.rgb, tex2.aaa);
        float3 layer3 = lerp(layer2, layer1, i.vColor2.aaa);
        matDiffuse = layer3 * tex3.rgb * 2.0f;
        finalOpacity = tex.a;
    }
    else if (pixelShader == 16) // MapObjTwoLayerDiffuseMod2xNA
    {
        float3 layer1 = (tex.rgb * tex2.rgb) * 2.0f;
        matDiffuse = lerp(tex.rgb, layer1, i.vColor2.aaa);
        finalOpacity = tex.a;
    }
    else if (pixelShader == 17) // MapObjTwoLayerDiffuseAlpha
    {
        float3 layer1 = tex.rgb;
        float3 layer2 = lerp(layer1, tex2.rgb, tex2.aaa);
        float3 layer3 = lerp(layer2, layer1, tex3.aaa);
        matDiffuse = (layer3 * tex3.rgb) * 2.0f;
        finalOpacity = tex.a;
    }
    else if (pixelShader == 18) // MapObjLod
    {
        matDiffuse = tex.rgb;
        finalOpacity = tex.a;
    }
    else if (pixelShader == 19) // MapObjParallax
    {
        // TODO(WMO): Implement the version-specific parallax material program.
        matDiffuse = float3(0.0f, 0.0f, 0.0f);
        finalOpacity = 0.0f;
    }
    else if (pixelShader == 20) // MapObjUnkShader
    {
        // TODO(WMO): Decode shader 23's complete multi-layer color and
        // emissive formula. The first layer is currently a zero placeholder.
        float4 tex_1 = float4(0.0f, 0.0f, 0.0f, 0.0f);
        float4 tex_2 = texture2.Sample(linearWrap, i.TexCoord);
        float4 tex_3 = texture3.Sample(linearWrap, i.TexCoord2);
        float4 tex_4 = texture4.Sample(linearWrap, i.TexCoord3);
        float4 tex_5 = texture5.Sample(linearWrap, i.TexCoord4);

        float4 tex_6 = texture6.Sample(linearWrap, i.TexCoord);
        float4 tex_7 = texture7.Sample(linearWrap, i.TexCoord2);
        float4 tex_8 = texture8.Sample(linearWrap, i.TexCoord3);
        float4 tex_9 = texture9.Sample(linearWrap, i.TexCoord4);

        float secondColorSum = dot(i.vColor3.bgr, float3(1.0f, 1.0f, 1.0f));
        float4 alphaVec = max(
                              float4(tex_6.a, tex_7.a, tex_8.a, tex_9.a),
                              0.004f
                          )
                        * float4(i.vColor3.bgr, 1.0f - saturate(secondColorSum));

        float maxAlpha = max(max(alphaVec.r, alphaVec.g), max(alphaVec.b, alphaVec.a));
        float4 alphaVec2 = 1.0f - saturate(float4(maxAlpha, maxAlpha, maxAlpha, maxAlpha) - alphaVec);
        alphaVec2 *= alphaVec;

        float alphaWeightSum = dot(alphaVec2, float4(1.0f, 1.0f, 1.0f, 1.0f));
        float4 alphaVec2Normalized = alphaWeightSum > 0.0f
            ? alphaVec2 / alphaWeightSum : float4(1.0f, 0.0f, 0.0f, 0.0f);

        float4 texMixed = tex_2 * alphaVec2Normalized.r
                        + tex_3 * alphaVec2Normalized.g
                        + tex_4 * alphaVec2Normalized.b
                        + tex_5 * alphaVec2Normalized.a;

        emissive = (texMixed.a * tex_1.rgb) * texMixed.rgb;
        float3 diffuseColor = float3(0.0f, 0.0f, 0.0f);
        matDiffuse = lerp(texMixed.rgb, diffuseColor, i.vColor3.a);
        finalOpacity = texMixed.a;
    }

    float diffuse = max(dot(normalize(i.Normal), normalize(lightDirection)), 0.0f);
    float ambientStrength = 0.3f;
    float3 ambient = ambientStrength * float3(1.0f, 1.0f, 1.0f);
    float3 lighting = ambient + diffuse;

    // The older client modulates every WMO material family by the same
    // vertex lighting term. Keep the existing modern material path intact.
    if (useLegacyLighting != 0)
        lighting = i.LitColor * 2.0f;
    else if (pixelShader == 0)
        lighting = float3(1.0f, 1.0f, 1.0f);

    if (useLegacyLighting != 0)
    {
        // Wisp's alpha is texture alpha for diffuse/composite shaders and
        // opaque for the other legacy families, always weighted by MOCV alpha.
        if (pixelShader != 0 && pixelShader != 6)
            finalOpacity = 1.0f;
        finalOpacity *= i.vColor1.a;
    }

    // Alpha-key WMO materials disable blending, so low-alpha texels must be
    // discarded before they can write their dark RGB or occlude the scene.
    if (alphaRef >= 0.0f && finalOpacity < alphaRef)
        discard;
    if (alphaRef < 0.0f)
        finalOpacity = 1.0f;

    // TODO(WMO): Wisp applies directional shadow visibility before fog; this
    // pass currently has neither a WMO shadow receiver nor the material's
    // Unfogged (MOMT 0x2) fog selection. Add both with client-version scope.
    float3 finalRgb = matDiffuse * lighting + spec + emissive;
    // Noggit reference: finalRgb += sidnColor for every SIDN batch. Legacy
    // 3.3.5 uses Wisp's texture-tinted vertex c29 above; modern rendering
    // retains the existing final-color addition until its client is audited.
    if (useLegacyLighting == 0)
        finalRgb += sidnColor;
    if (useLegacyLighting != 0)
        finalRgb = saturate(finalRgb);
    return float4(finalRgb, finalOpacity);
}
