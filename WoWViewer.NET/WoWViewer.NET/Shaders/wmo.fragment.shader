#version 450

in vec3 Normal;
in vec2 TexCoord;
in vec2 TexCoord2;
in vec2 TexCoord3;
in vec2 TexCoord4;
in vec4 vColor1;
in vec4 vColor2;
in vec4 vColor3;
out vec4 outputColor;

uniform float pixelShader;
uniform float alphaRef;
uniform vec3 lightDirection;

layout(binding=0) uniform sampler2D texture1;
layout(binding=1) uniform sampler2D texture2;
layout(binding=2) uniform sampler2D texture3;
layout(binding=3) uniform sampler2D texture4;
layout(binding=4) uniform sampler2D texture5;
layout(binding=5) uniform sampler2D texture6;
layout(binding=6) uniform sampler2D texture7;
layout(binding=7) uniform sampler2D texture8;
layout(binding=8) uniform sampler2D texture9;

void main()
{
	int uPixelShader = int(pixelShader);
	vec4 tex = texture(texture1, TexCoord);
	vec4 tex2 = texture(texture2, TexCoord2);
	vec4 tex3 = texture(texture3, TexCoord3);
    vec4 tex4 = texture(texture4, TexCoord4);

	vec3 matDiffuse = vec3(0.0);
    vec3 spec = vec3(0.0);
    vec3 emissive = vec3(0.0);
    float distFade = 1.0;

	float finalOpacity = 0.0;

    if ( uPixelShader == -1 ) {
        matDiffuse = tex.rgb * tex2.rgb;
        finalOpacity = tex.a;
    } else if (uPixelShader == 0) { //MapObjDiffuse

        matDiffuse = tex.rgb;
        finalOpacity = tex.a;
    } else if (uPixelShader == 1) { //MapObjSpecular

        matDiffuse = tex.rgb;
        //spec = calcSpec(tex.a);

        finalOpacity = tex.a;
    } else if (uPixelShader == 2) { //MapObjMetal

        matDiffuse = tex.rgb ;
       // spec = calcSpec(((tex * 4.0) * tex.a).x);

        finalOpacity = tex.a;
    } else if (uPixelShader == 3) { //MapObjEnv
        matDiffuse = tex.rgb ;

        emissive = tex2.rgb * tex.a * distFade;
        finalOpacity = 1.0;

    } else if (uPixelShader == 4) { //MapObjOpaque

        matDiffuse = tex.rgb ;
        finalOpacity = 1.0;

    } else if (uPixelShader == 5) { //MapObjEnvMetal

        matDiffuse = tex.rgb ;
        emissive = (((tex.rgb * tex.a) * tex2.rgb) * distFade);

        finalOpacity = 1.0;

    } else if (uPixelShader == 6) { //MapObjTwoLayerDiffuse

        vec3 layer1 = tex.rgb;
        vec3 layer2 = mix(layer1, tex2.rgb, tex2.a);
        matDiffuse = mix(layer2, layer1, vColor2.a);

        finalOpacity = tex.a;
    } else if (uPixelShader == 7) { //MapObjTwoLayerEnvMetal

        vec4 colorMix = mix(tex, tex, 1.0 - vColor2.a);

        matDiffuse = colorMix.rgb ;
        emissive = (colorMix.rgb * colorMix.a) * tex3.rgb * distFade;

        finalOpacity = tex.a;

    } else if (uPixelShader == 8) { //MapObjTwoLayerTerrain

        vec3 layer1 = tex.rgb;
        vec3 layer2 = tex2.rgb;

        matDiffuse = mix(layer2, layer1, vColor2.a);
        //spec = calcSpec(tex2.a * (1.0 - vColor2.a));
        finalOpacity = tex.a;

    } else if (uPixelShader == 9) { //MapObjDiffuseEmissive
        matDiffuse = tex.rgb;
        emissive = tex2.rgb * tex2.a * vColor2.a;

        finalOpacity = tex.a;
    } else if (uPixelShader == 10) { //MapObjMaskedEnvMetal

        float mixFactor = clamp((tex3.a * vColor2.a), 0.0, 1.0);
        matDiffuse =
            mix(mix(((tex.rgb * tex2.rgb) * 2.0), tex3.rgb, mixFactor), tex.rgb, tex.a);

        finalOpacity = tex.a;

    } else if (uPixelShader == 11) { //MapObjEnvMetalEmissive
        matDiffuse = tex.rgb ;
        emissive =
            (
                ((tex.rgb * tex.a) * tex2.rgb) +
                ((tex3.rgb * tex3.a) * vColor2.a)
            );
        finalOpacity = tex.a;
       
    } else if (uPixelShader == 12) { //MapObjTwoLayerDiffuseOpaque
        matDiffuse = mix(tex2.rgb, tex.rgb, vColor1.a);
        finalOpacity = 1.0;
        
    } else if (uPixelShader == 13) { //MapObjTwoLayerDiffuseEmissive
        vec3 t1diffuse = (tex2.rgb * (1.0 - tex2.a));

        matDiffuse = mix(t1diffuse, tex.rgb, vColor2.a);

        emissive = (tex2.rgb * tex2.a) * (1.0 - vColor2.a);

        finalOpacity = tex.a;
    } else if (uPixelShader == 14) { //MapObjAdditiveMaskedEnvMetal
        matDiffuse = mix(
            (tex.rgb * tex2.rgb * 2.0) + (tex3.rgb * clamp(tex3.a * vColor2.a, 0.0, 1.0)),
            tex.rgb,
            vec3(tex.a)
        );

        finalOpacity = 1.0;
    } else if (uPixelShader == 15) { //MapObjTwoLayerDiffuseMod2x
        vec3 layer1 = tex.rgb;
        vec3 layer2 = mix(layer1, tex2.rgb, vec3(tex2.a));
        vec3 layer3 = mix(layer2, layer1, vec3(vColor2.a));

        matDiffuse = layer3 * tex3.rgb * 2.0;
        finalOpacity = tex.a;
    } else if (uPixelShader == 16) { //MapObjTwoLayerDiffuseMod2xNA
        vec3 layer1 = ((tex.rgb * tex2.rgb) * 2.0);

        matDiffuse = mix(tex.rgb, layer1, vec3(vColor2.a)) ;
        finalOpacity = tex.a;
    } else if (uPixelShader == 17) { //MapObjTwoLayerDiffuseAlpha
        vec3 layer1 = tex.rgb;
        vec3 layer2 = mix(layer1, tex2.rgb, vec3(tex2.a));
        vec3 layer3 = mix(layer2, layer1, vec3(tex3.a));

        matDiffuse = ((layer3 * tex3.rgb) * 2.0);
        finalOpacity = tex.a;
    } else if (uPixelShader == 18) { //MapObjLod
        matDiffuse = tex.rgb;
        finalOpacity = tex.a;
        
    } else if (uPixelShader == 19) { //MapObjParallax
        /*
        vec4 tex_6 = texture(uTexture6, vTexCoord2).rgba;

        mat3 TBN = contangent_frame(vNormal.xyz, -vPosition.xyz, vTexCoord2);

        float cosAlpha = dot(normalize(vPosition.xyz), vNormal.xyz);
        vec2 dotResult = (TBN * (normalize(-vPosition.xyz) / cosAlpha)).xy;

        vec4 tex_4 = texture(uTexture4, vTexCoord2 - (dotResult * tex_6.r * 0.25)).rgba;
        vec4 tex_5 = texture(uTexture5, vTexCoord3 - (dotResult * tex_6.r * 0.25)).rgba;
        vec4 tex_3 = texture(uTexture3, vTexCoord2).rgba;

        vec3 mix1 = tex_5.rgb + tex_4.rgb * tex_4.a;
        vec3 mix2 = (tex_3.rgb - mix1) * tex_6.g + mix1;
        vec3 mix3 = tex_3.rgb * tex_6.b + (tex_5.rgb * tex_5.a * (1.0 - tex3.b));

        vec4 tex_2 = texture(uTexture3, vColorSecond.bg).rgba;
        vec3 tex_2_mult = tex_2.rgb * tex_2.a;

        vec3 emissive_component;
        if (vColor2.a> 0.0)
        {
            vec4 tex = texture(uTexture, vTexCoord).rgba;
            matDiffuse = (tex.rgb - mix2 ) * vColor2.a + mix2;
            emissive_component = ((tex.rgb * tex.a) - tex_2_mult.rgb) * vColor2.a + tex_2_mult.rgb;
        } else {
            emissive_component = tex_2_mult;
            matDiffuse = mix2;
        }

        emissive = (mix3 - (mix3 * vColor2.a)) + (emissive_component * tex_2.rgb);
        .*/

    } else if (uPixelShader == 20) { //MapObjUnkShader
        //vec4 tex_1 = texture(uTexture, posToTexCoord(vPosition.xyz, vNormal.xyz));
        vec4 tex_1 = vec4(0.0);
        vec4 tex_2 = texture(texture2, TexCoord);
        vec4 tex_3 = texture(texture3, TexCoord2);
        vec4 tex_4 = texture(texture4, TexCoord3);
        vec4 tex_5 = texture(texture5, TexCoord4);

        vec4 tex_6 = texture(texture6, TexCoord);
        vec4 tex_7 = texture(texture7, TexCoord2);
        vec4 tex_8 = texture(texture8, TexCoord3);
        vec4 tex_9 = texture(texture9, TexCoord4);

        float secondColorSum = dot(vColor3.bgr, vec3(1.0));
        vec4 alphaVec = max(vec4(tex_6.a, tex_7.a, tex_8.a, tex_9.a), 0.004) * vec4(vColor3.bgr, 1.0 - clamp(secondColorSum, 0.0, 1.0));
        float maxAlpha = max(alphaVec.r, max(alphaVec.g, max(alphaVec.r, alphaVec.a)));
        vec4 alphaVec2 = (1.0 - clamp(vec4(maxAlpha) - alphaVec, 0.0, 1.0));
        alphaVec2 = alphaVec2 * alphaVec;

        vec4 alphaVec2Normalized = alphaVec2 * (1.0 / dot(alphaVec2, vec4(1.0)));

        vec4 texMixed = tex_2 * alphaVec2Normalized.r +
                        tex_3 * alphaVec2Normalized.g +
                        tex_4 * alphaVec2Normalized.b +
                        tex_5 * alphaVec2Normalized.a;

        emissive = (texMixed.w * tex_1.rgb) * texMixed.rgb;
        vec3 diffuseColor = vec3(0,0,0); //<= it's unknown where this color comes from. But it's not MOMT chunk
        matDiffuse = (diffuseColor - texMixed.rgb) * vColor3.a + texMixed.rgb;
        finalOpacity = texMixed.a;
        
    }

    float diffuse = max(dot(normalize(Normal), normalize(lightDirection)), 0.0);
    float ambientStrength = 0.3;
    vec3 ambient = ambientStrength * vec3(1.0);
    vec3 lighting = ambient + diffuse;

    outputColor = vec4(matDiffuse * lighting + emissive, finalOpacity);
}
