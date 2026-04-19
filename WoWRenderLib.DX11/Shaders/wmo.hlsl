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

    float4x4 modelViewMatrix = mul(view_matrix, model_matrix);
    float3x3 mv3 = (float3x3) modelViewMatrix;

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
        o.TexCoord2 = reflect(normalize(float3(1.0f, 1.0f, 1.0f)), input.normal).xy; // TODO: replace float3(1) with cameraPoint
        o.TexCoord3 = input.texCoord3;
    }
    else if (vertexShader == 2) // MapObjDiffuse_T1_T2
    {
        o.TexCoord = input.texCoord;
        o.TexCoord2 = input.texCoord2;
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
        o.TexCoord3 = reflect(normalize(float3(1.0f, 1.0f, 1.0f)), input.normal).xy; // TODO: replace float3(1) with cameraPoint
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
    float finalOpacity = 0.0f;

    if (pixelShader == -1)
    {
        matDiffuse = tex.rgb * tex2.rgb;
        finalOpacity = tex.a;
    }
    else if (pixelShader == 0) // MapObjDiffuse
    {
        matDiffuse = tex.rgb;
        finalOpacity = tex.a;
    }
    else if (pixelShader == 1) // MapObjSpecular
    {
        matDiffuse = tex.rgb;
        // spec = calcSpec(tex.a);  // TODO: implement specular
        finalOpacity = tex.a;
    }
    else if (pixelShader == 2) // MapObjMetal
    {
        matDiffuse = tex.rgb;
        // spec = calcSpec(((tex * 4.0) * tex.a).x);  // TODO
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
        float3 layer1 = tex.rgb;
        float3 layer2 = lerp(layer1, tex2.rgb, tex2.a);
        matDiffuse = lerp(layer2, layer1, i.vColor2.a);
        finalOpacity = tex.a;
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
    else if (pixelShader == 19) // MapObjParallax  (TODO: full implementation)
    {
        matDiffuse = float3(0.0f, 0.0f, 0.0f);
        finalOpacity = 0.0f;
    }
    else if (pixelShader == 20) // MapObjUnkShader
    {
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

        float maxAlpha = max(alphaVec.r, max(alphaVec.g, max(alphaVec.r, alphaVec.a)));
        float4 alphaVec2 = 1.0f - saturate(float4(maxAlpha, maxAlpha, maxAlpha, maxAlpha) - alphaVec);
        alphaVec2 *= alphaVec;

        float4 alphaVec2Normalized = alphaVec2 * (1.0f / dot(alphaVec2, float4(1.0f, 1.0f, 1.0f, 1.0f)));

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

    return float4(matDiffuse * lighting + emissive, finalOpacity);
}
