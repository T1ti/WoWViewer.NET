cbuffer PerObject : register(b0)
{
    float4x4 projection_matrix;
    float4x4 view_matrix;
    float4x4 model_matrix;
    float4x4 texMatrix1;
    float4x4 texMatrix2;
    int vertexShader;
    int pixelShader;
    int hasTexMatrix1;
    int hasTexMatrix2;
    float3 lightDirection;
    float alphaRef;
    float blendMode;
    float3 _pad;
    float3 ambientColor;
    float _pad1;
    float3 diffuseColor;
    float _pad2;
};

Texture2D texture1 : register(t0);
Texture2D texture2 : register(t1);
Texture2D texture3 : register(t2);
Texture2D texture4 : register(t3);
SamplerState texSampler : register(s0);

struct VSInput
{
    // Buffer 0
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 texCoord1 : TEXCOORD0;
    float2 texCoord2 : TEXCOORD1;
    
    // Buffer 1
    float4 instanceRow0 : TEXCOORD2;
    float4 instanceRow1 : TEXCOORD3;
    float4 instanceRow2 : TEXCOORD4;
    float4 instanceRow3 : TEXCOORD5;
};

struct VSOutput
{
    float4 position : SV_POSITION;
    float2 TexCoord1 : TEXCOORD0;
    float2 TexCoord2 : TEXCOORD1;
    float2 TexCoord3 : TEXCOORD2;
    float3 Normal : TEXCOORD3;
    float EdgeFade : TEXCOORD4;
    float3 LitColor : TEXCOORD5;
};

// Environment-map coordinates used by the Wisp/WebWowViewer shader family.
// The view-space position is reflected around the view-space normal and then
// projected onto the 2D environment map (the old DX11 path left these UVs at
// (0, 0), which made all environment stages sample one texel).
float2 posToTexCoord(float3 vertexPosInView, float3 normal)
{
    float3 viewVec = normalize(vertexPosInView);
    float3 reflection = reflect(viewVec, normalize(normal));
    float3 projected = float3(reflection.xy, reflection.z + 1.0f);
    return normalize(projected).xy * 0.5f + float2(0.5f, 0.5f);
}

VSOutput VS_Main(VSInput input)
{
    VSOutput output;

    float4x4 instanceMatrix = float4x4(
        input.instanceRow0,
        input.instanceRow1,
        input.instanceRow2,
        input.instanceRow3
    );
    
    instanceMatrix = transpose(instanceMatrix);
    
    float4 worldPos = mul(instanceMatrix, float4(input.position, 1.0));
    output.position = mul(projection_matrix, mul(view_matrix, worldPos));

    // M2 instances carry the model transform in the second vertex stream.
    // Use it for normals as well as positions; using model_matrix (identity)
    // here caused lighting to ignore per-instance rotation and scale.
    float4x4 modelViewMatrix = mul(view_matrix, instanceMatrix);
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
    output.Normal = normalize(mul(normalMatrix, input.normal));

    // Wisp's Diffuse_* vertex shaders carry a clamped lighting term into the
    // combiner stage. Keep the same neutral ambient/diffuse balance here so
    // textures are not multiplied by the old unbounded (1 + N.L) factor.
    float nDotL = max(dot(output.Normal, normalize(lightDirection)), 0.0f);
    output.LitColor = saturate(ambientColor + diffuseColor * nDotL);

    float4x4 textureMatrix1 = hasTexMatrix1 != 0 ? texMatrix1 : float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
    float4x4 textureMatrix2 = hasTexMatrix2 != 0 ? texMatrix2 : float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

    float2 envCoord = posToTexCoord(mul(view_matrix, worldPos).xyz, output.Normal);
    output.EdgeFade = 1.0;

    output.TexCoord1 = input.texCoord1;
    output.TexCoord2 = float2(0.0, 0.0);
    output.TexCoord3 = float2(0.0, 0.0);

    switch (vertexShader)
    {
        case 0: // Diffuse_T1
            output.TexCoord1 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            break;

        case 1: // Diffuse_Env
            output.TexCoord1 = envCoord;
            break;

        case 2: // Diffuse_T1_T2
            output.TexCoord1 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            output.TexCoord2 = mul(textureMatrix2, float4(input.texCoord2, 0.0, 1.0)).xy;
            break;

        case 3: // Diffuse_T1_Env
            output.TexCoord1 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            output.TexCoord2 = envCoord;
            break;

        case 4: // Diffuse_Env_T1
            output.TexCoord1 = envCoord;
            output.TexCoord2 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            break;

        case 5: // Diffuse_Env_Env
            output.TexCoord1 = envCoord;
            output.TexCoord2 = envCoord;
            break;

        case 6: // Diffuse_T1_Env_T1
            output.TexCoord1 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            output.TexCoord2 = envCoord;
            output.TexCoord3 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            break;

        case 7: // Diffuse_T1_T1
            output.TexCoord1 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            output.TexCoord2 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            break;

        case 8: // Diffuse_T1_T1_T1
            output.TexCoord1 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            output.TexCoord2 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            output.TexCoord3 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            break;

        case 9: // Diffuse_EdgeFade_T1
            output.TexCoord1 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            break;

        case 10: // Diffuse_T2
            output.TexCoord1 = mul(textureMatrix2, float4(input.texCoord2, 0.0, 1.0)).xy;
            break;

        case 11: // Diffuse_T1_Env_T2
            output.TexCoord1 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            output.TexCoord2 = envCoord;
            output.TexCoord3 = mul(textureMatrix2, float4(input.texCoord2, 0.0, 1.0)).xy;
            break;

        case 12: // Diffuse_EdgeFade_T1_T2
            output.TexCoord1 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            output.TexCoord2 = mul(textureMatrix2, float4(input.texCoord2, 0.0, 1.0)).xy;
            break;

        case 13: // Diffuse_EdgeFade_Env
            output.TexCoord1 = envCoord;
            break;

        case 14: // Diffuse_T1_T2_T1
            output.TexCoord1 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            output.TexCoord2 = mul(textureMatrix2, float4(input.texCoord2, 0.0, 1.0)).xy;
            output.TexCoord3 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            break;

        case 15: // Diffuse_T1_T2_T3
            output.TexCoord1 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            output.TexCoord2 = mul(textureMatrix2, float4(input.texCoord2, 0.0, 1.0)).xy;
            output.TexCoord3 = output.TexCoord2;
            break;

        case 16: // Color_T1_T2_T3
            output.TexCoord1 = mul(textureMatrix2, float4(input.texCoord2, 0.0, 1.0)).xy;
            output.TexCoord2 = float2(0.0, 0.0);
            output.TexCoord3 = float2(0.0, 0.0);
            break;

        case 17: // BW_Diffuse_T1
        case 18: // BW_Diffuse_T1_T2
            output.TexCoord1 = mul(textureMatrix1, float4(input.texCoord1, 0.0, 1.0)).xy;
            break;

        default:
            output.TexCoord1 = input.texCoord1;
            break;
    }

    return output;
}

float4 PS_Main(VSOutput input) : SV_TARGET
{
    float4 MeshColor = float4(1.0, 1.0, 1.0, 1.0);
    float4 TexSampleAlpha = float4(1.0, 1.0, 1.0, 1.0);

    int iBlendMode = (int) blendMode;
    int iPixelShader = (int) pixelShader;

    float2 uv1 = input.TexCoord1;
    float2 uv2 = input.TexCoord2;
    float2 uv3 = input.TexCoord3;

    if (iPixelShader == 26 || iPixelShader == 27 || iPixelShader == 28)
    {
        uv2 = input.TexCoord1;
        uv3 = input.TexCoord1;
    }

    float4 tex1 = texture1.Sample(texSampler, uv1);
    float4 tex2 = texture2.Sample(texSampler, uv2);
    float4 tex3 = texture3.Sample(texSampler, uv3);
    float4 tex4 = texture4.Sample(texSampler, input.TexCoord2);

    float3 mesh_color = MeshColor.rgb;
    float mesh_opacity = MeshColor.a * input.EdgeFade;

    float3 mat_diffuse = float3(0.0, 0.0, 0.0);
    float3 specular = float3(0.0, 0.0, 0.0);
    float discard_alpha = 1.0;
    bool can_discard = false;

    switch (iPixelShader)
    {
        case 0: // Combiners_Opaque
            mat_diffuse = mesh_color * tex1.rgb;
            break;

        case 1: // Combiners_Mod
            mat_diffuse = mesh_color * tex1.rgb;
            discard_alpha = tex1.a;
            can_discard = true;
            break;

        case 2: // Combiners_Opaque_Mod
            mat_diffuse = mesh_color * tex1.rgb * tex2.rgb;
            discard_alpha = tex2.a;
            can_discard = true;
            break;

        case 3: // Combiners_Opaque_Mod2x
            mat_diffuse = mesh_color * tex1.rgb * tex2.rgb * 2.0;
            discard_alpha = tex2.a * 2.0;
            can_discard = true;
            break;

        case 4: // Combiners_Opaque_Mod2xNA
            mat_diffuse = mesh_color * tex1.rgb * tex2.rgb * 2.0;
            break;

        case 5: // Combiners_Opaque_Opaque
            mat_diffuse = mesh_color * tex1.rgb * tex2.rgb;
            break;

        case 6: // Combiners_Mod_Mod
            mat_diffuse = mesh_color * tex1.rgb * tex2.rgb;
            discard_alpha = tex1.a * tex2.a;
            can_discard = true;
            break;

        case 7: // Combiners_Mod_Mod2x
            mat_diffuse = mesh_color * tex1.rgb * tex2.rgb * 2.0;
            discard_alpha = tex1.a * tex2.a * 2.0;
            can_discard = true;
            break;

        case 8: // Combiners_Mod_Add
            mat_diffuse = mesh_color * tex1.rgb;
            discard_alpha = tex1.a + tex2.a;
            can_discard = true;
            specular = tex2.rgb;
            break;

        case 9: // Combiners_Mod_Mod2xNA
            mat_diffuse = mesh_color * tex1.rgb * tex2.rgb * 2.0;
            discard_alpha = tex1.a;
            can_discard = true;
            break;

        case 10: // Combiners_Mod_AddNA
            mat_diffuse = mesh_color * tex1.rgb;
            discard_alpha = tex1.a;
            can_discard = true;
            specular = tex2.rgb;
            break;

        case 11: // Combiners_Mod_Opaque
            mat_diffuse = mesh_color * tex1.rgb * tex2.rgb;
            discard_alpha = tex1.a;
            can_discard = true;
            break;

        case 12: // Combiners_Opaque_Mod2xNA_Alpha
            mat_diffuse = mesh_color * lerp(tex1.rgb * tex2.rgb * 2.0, tex1.rgb, tex1.a);
            break;

        case 13: // Combiners_Opaque_AddAlpha
            mat_diffuse = mesh_color * tex1.rgb;
            specular = tex2.rgb * tex2.a;
            break;

        case 14: // Combiners_Opaque_AddAlpha_Alpha
            mat_diffuse = mesh_color * tex1.rgb;
            specular = tex2.rgb * tex2.a * (1.0 - tex1.a);
            break;

        case 15: // Combiners_Opaque_Mod2xNA_Alpha_Add
            mat_diffuse = mesh_color * lerp(tex1.rgb * tex2.rgb * 2.0, tex1.rgb, tex1.a);
            specular = tex3.rgb * tex3.a * TexSampleAlpha.b;
            break;

        case 16: // Combiners_Mod_AddAlpha
            mat_diffuse = mesh_color * tex1.rgb;
            discard_alpha = tex1.a;
            can_discard = true;
            specular = tex2.rgb * tex2.a;
            break;

        case 17: // Combiners_Mod_AddAlpha_Alpha
            mat_diffuse = mesh_color * tex1.rgb;
            discard_alpha = tex1.a + tex2.a * (0.3 * tex2.r + 0.59 * tex2.g + 0.11 * tex2.b);
            can_discard = true;
            specular = tex2.rgb * tex2.a * (1.0 - tex1.a);
            break;

        case 18: // Combiners_Opaque_Alpha_Alpha
            mat_diffuse = mesh_color * lerp(lerp(tex1.rgb, tex2.rgb, tex2.a), tex1.rgb, tex1.a);
            break;

        case 19: // Combiners_Opaque_Mod2xNA_Alpha_3s
            mat_diffuse = mesh_color * lerp(tex1.rgb * tex2.rgb * 2.0, tex3.rgb, tex3.a);
            break;

        case 20: // Combiners_Opaque_AddAlpha_Wgt
            mat_diffuse = mesh_color * tex1.rgb;
            specular = tex2.rgb * tex2.a * TexSampleAlpha.g;
            break;

        case 21: // Combiners_Mod_Add_Alpha
            mat_diffuse = mesh_color * tex1.rgb;
            discard_alpha = tex1.a + tex2.a;
            can_discard = true;
            specular = tex2.rgb * (1.0 - tex1.a);
            break;

        case 22: // Combiners_Opaque_ModNA_Alpha
            mat_diffuse = mesh_color * lerp(tex1.rgb * tex2.rgb, tex1.rgb, tex1.a);
            break;

        case 23: // Combiners_Mod_AddAlpha_Wgt
            mat_diffuse = mesh_color * tex1.rgb;
            discard_alpha = tex1.a;
            can_discard = true;
            specular = tex2.rgb * tex2.a * TexSampleAlpha.g;
            break;

        case 24: // Combiners_Opaque_Mod_Add_Wgt
            mat_diffuse = mesh_color * lerp(tex1.rgb, tex2.rgb, tex2.a);
            specular = tex1.rgb * tex1.a * TexSampleAlpha.r;
            break;

        case 25: // Combiners_Opaque_Mod2xNA_Alpha_UnshAlpha
        {
                float glow_opacity = clamp(tex3.a * TexSampleAlpha.b, 0.0, 1.0);
                mat_diffuse = mesh_color * lerp(tex1.rgb * tex2.rgb * 2.0, tex1.rgb, tex1.a) * (1.0 - glow_opacity);
                specular = tex3.rgb * glow_opacity;
                break;
            }

        case 26: // Combiners_Mod_Dual_Crossfade
        {
                float4 mixed = lerp(lerp(tex1, tex2, clamp(TexSampleAlpha.g, 0.0, 1.0)), tex3, clamp(TexSampleAlpha.b, 0.0, 1.0));
                mat_diffuse = mesh_color * mixed.rgb;
                discard_alpha = mixed.a;
                can_discard = true;
                break;
            }

        case 27: // Combiners_Opaque_Mod2xNA_Alpha_Alpha
            mat_diffuse = mesh_color * lerp(lerp(tex1.rgb * tex2.rgb * 2.0, tex3.rgb, tex3.a), tex1.rgb, tex1.a);
            break;

        case 28: // Combiners_Mod_Masked_Dual_Crossfade
        {
                float4 mixed = lerp(lerp(tex1, tex2, clamp(TexSampleAlpha.g, 0.0, 1.0)), tex3, clamp(TexSampleAlpha.b, 0.0, 1.0));
                mat_diffuse = mesh_color * mixed.rgb;
                discard_alpha = mixed.a * tex4.a;
                can_discard = true;
                break;
            }

        case 29: // Combiners_Opaque_Alpha
            mat_diffuse = mesh_color * lerp(tex1.rgb, tex2.rgb, tex2.a);
            break;

        case 30: // Guild
        {
                float3 generic0 = float3(1.0, 1.0, 1.0);
                float3 generic1 = float3(1.0, 1.0, 1.0);
                float3 generic2 = float3(1.0, 1.0, 1.0);
                mat_diffuse = mesh_color * lerp(tex1.rgb * lerp(generic0, tex2.rgb * generic1, tex2.a), tex3.rgb * generic2, tex3.a);
                discard_alpha = tex1.a;
                can_discard = true;
                break;
            }

        case 31: // Guild_NoBorder
        {
                float3 generic0 = float3(1.0, 1.0, 1.0);
                float3 generic1 = float3(1.0, 1.0, 1.0);
                mat_diffuse = mesh_color * tex1.rgb * lerp(generic0, tex2.rgb * generic1, tex2.a);
                discard_alpha = tex1.a;
                can_discard = true;
                break;
            }

        case 32: // Guild_Opaque
        {
                float3 generic0 = float3(1.0, 1.0, 1.0);
                float3 generic1 = float3(1.0, 1.0, 1.0);
                float3 generic2 = float3(1.0, 1.0, 1.0);
                mat_diffuse = mesh_color * lerp(tex1.rgb * lerp(generic0, tex2.rgb * generic1, tex2.a), tex3.rgb * generic2, tex3.a);
                break;
            }

        case 33: // Combiners_Mod_Depth
            mat_diffuse = mesh_color * tex1.rgb;
            discard_alpha = tex1.a;
            can_discard = true;
            break;

        case 34: // Illum
            discard_alpha = tex1.a;
            can_discard = true;
            break;

        case 35: // Combiners_Mod_Mod_Mod_Const
        {
                float4 generic0 = float4(1.0, 1.0, 1.0, 1.0);
                float4 combined = tex1 * tex2 * tex3 * generic0;
                mat_diffuse = mesh_color * combined.rgb;
                discard_alpha = combined.a;
                can_discard = true;
                break;
            }

        case 36: // Combiners_Mod_Mod_Depth
            mat_diffuse = mesh_color * tex1.rgb * tex2.rgb;
            discard_alpha = tex1.a * tex2.a;
            can_discard = true;
            break;

        default:
            mat_diffuse = mesh_color * tex1.rgb;
            break;
    }

    float final_opacity;
    bool do_discard = false;

    if (iBlendMode == 13)
    {
        final_opacity = discard_alpha * mesh_opacity;
    }
    else if (iBlendMode == 1)
    {
        final_opacity = mesh_opacity;
        if (can_discard && discard_alpha < alphaRef)
            do_discard = true;
    }
    else if (iBlendMode == 0)
    {
        final_opacity = mesh_opacity;
    }
    else if (iBlendMode == 4 || iBlendMode == 5)
    {
        final_opacity = discard_alpha * mesh_opacity;
        if (can_discard && discard_alpha < alphaRef)
            do_discard = true;
    }
    else
    {
        final_opacity = discard_alpha * mesh_opacity;
    }

    if (do_discard)
        discard;

    float3 lit_color = mat_diffuse * input.LitColor;
    // lit_color += specular; // uncomment when ready

    return float4(lit_color, final_opacity);
}
