#version 430

in vec2 TexCoord1;
in vec2 TexCoord2;
in vec2 TexCoord3;
in vec3 Normal;
in float EdgeFade;

uniform float alphaRef;
uniform sampler2D texture1;
uniform sampler2D texture2;
uniform sampler2D texture3;
uniform sampler2D texture4;

layout(location = 4) uniform float pixelShader;
layout(location = 5) uniform vec3 lightDirection;
layout(location = 6) uniform float blendMode;

out vec4 outColor;

// based on wow.export's m2 shader which in turn is based on deamon's shader
vec3 calc_lighting(vec3 color, vec3 normal) {
	// TEMP
	int u_apply_lighting = 1;
	vec3 u_ambient_color = vec3(1.0);
	vec3 u_diffuse_color = vec3(1.0);
	// END TEMP

	if (u_apply_lighting == 0)
		return color;

	vec3 n = normalize(normal);
	float n_dot_l = max(dot(n, normalize(-lightDirection)), 0.0);

	vec3 ambient = u_ambient_color * color;
	vec3 diffuse = u_diffuse_color * color * n_dot_l;

	return ambient + diffuse;
}

void main()
{
	// TODO: hook these up
	vec4 MeshColor = vec4(1.0);
	vec4 TexSampleAlpha = vec4(1.0);

	int iBlendMode = int(blendMode);
	int iPixelShader = int(pixelShader);

	vec2 uv1 = TexCoord1;
	vec2 uv2 = TexCoord2;
	vec2 uv3 = TexCoord3;

	if(iPixelShader == 26 || iPixelShader == 27 || iPixelShader == 28) {
		uv2 = TexCoord1;
		uv3 = TexCoord1;
	}

	vec4 tex1 = texture(texture1, uv1);
	vec4 tex2 = texture(texture2, uv2);
	vec4 tex3 = texture(texture3, uv3);
	vec4 tex4 = texture(texture4, TexCoord2);

	vec3 mesh_color = MeshColor.rgb;
	float mesh_opacity = MeshColor.a * EdgeFade;

	vec3 mat_diffuse = vec3(0.0);
	vec3 specular = vec3(0.0);
	float discard_alpha = 1.0;
	bool can_discard = false;

	// Blizzard M2 combiner shaders, reference: https://wowdev.wiki/M2#Shaders
	switch (iPixelShader) {
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
			mat_diffuse = mesh_color * mix(tex1.rgb * tex2.rgb * 2.0, tex1.rgb, vec3(tex1.a));
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
			mat_diffuse = mesh_color * mix(tex1.rgb * tex2.rgb * 2.0, tex1.rgb, vec3(tex1.a));
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
			mat_diffuse = mesh_color * mix(mix(tex1.rgb, tex2.rgb, vec3(tex2.a)), tex1.rgb, vec3(tex1.a));
			break;

		case 19: // Combiners_Opaque_Mod2xNA_Alpha_3s
			mat_diffuse = mesh_color * mix(tex1.rgb * tex2.rgb * 2.0, tex3.rgb, vec3(tex3.a));
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
			mat_diffuse = mesh_color * mix(tex1.rgb * tex2.rgb, tex1.rgb, vec3(tex1.a));
			break;

		case 23: // Combiners_Mod_AddAlpha_Wgt
			mat_diffuse = mesh_color * tex1.rgb;
			discard_alpha = tex1.a;
			can_discard = true;
			specular = tex2.rgb * tex2.a * TexSampleAlpha.g;
			break;

		case 24: // Combiners_Opaque_Mod_Add_Wgt
			mat_diffuse = mesh_color * mix(tex1.rgb, tex2.rgb, vec3(tex2.a));
			specular = tex1.rgb * tex1.a * TexSampleAlpha.r;
			break;

		case 25: // Combiners_Opaque_Mod2xNA_Alpha_UnshAlpha
			{
				float glow_opacity = clamp(tex3.a * TexSampleAlpha.b, 0.0, 1.0);
				mat_diffuse = mesh_color * mix(tex1.rgb * tex2.rgb * 2.0, tex1.rgb, vec3(tex1.a)) * (1.0 - glow_opacity);
				specular = tex3.rgb * glow_opacity;
			}
			break;

		case 26: // Combiners_Mod_Dual_Crossfade
			{
				vec4 mixed = mix(mix(tex1, tex2, vec4(clamp(TexSampleAlpha.g, 0.0, 1.0))), tex3, vec4(clamp(TexSampleAlpha.b, 0.0, 1.0)));
				mat_diffuse = mesh_color * mixed.rgb;
				discard_alpha = mixed.a;
				can_discard = true;
			}
			break;

		case 27: // Combiners_Opaque_Mod2xNA_Alpha_Alpha
			mat_diffuse = mesh_color * mix(mix(tex1.rgb * tex2.rgb * 2.0, tex3.rgb, vec3(tex3.a)), tex1.rgb, vec3(tex1.a));
			break;

		case 28: // Combiners_Mod_Masked_Dual_Crossfade
			{
				vec4 mixed = mix(mix(tex1, tex2, vec4(clamp(TexSampleAlpha.g, 0.0, 1.0))), tex3, vec4(clamp(TexSampleAlpha.b, 0.0, 1.0)));
				mat_diffuse = mesh_color * mixed.rgb;
				discard_alpha = mixed.a * tex4.a;
				can_discard = true;
			}
			break;

		case 29: // Combiners_Opaque_Alpha
			mat_diffuse = mesh_color * mix(tex1.rgb, tex2.rgb, vec3(tex2.a));
			break;

		case 30: // Guild
			{
				vec3 generic0 = vec3(1.0);
				vec3 generic1 = vec3(1.0);
				vec3 generic2 = vec3(1.0);
				mat_diffuse = mesh_color * mix(tex1.rgb * mix(generic0, tex2.rgb * generic1, vec3(tex2.a)), tex3.rgb * generic2, vec3(tex3.a));
				discard_alpha = tex1.a;
				can_discard = true;
			}
			break;

		case 31: // Guild_NoBorder
			{
				vec3 generic0 = vec3(1.0);
				vec3 generic1 = vec3(1.0);
				mat_diffuse = mesh_color * tex1.rgb * mix(generic0, tex2.rgb * generic1, vec3(tex2.a));
				discard_alpha = tex1.a;
				can_discard = true;
			}
			break;

		case 32: // Guild_Opaque
			{
				vec3 generic0 = vec3(1.0);
				vec3 generic1 = vec3(1.0);
				vec3 generic2 = vec3(1.0);
				mat_diffuse = mesh_color * mix(tex1.rgb * mix(generic0, tex2.rgb * generic1, vec3(tex2.a)), tex3.rgb * generic2, vec3(tex3.a));
			}
			break;

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
				vec4 generic0 = vec4(1.0);
				vec4 combined = tex1 * tex2 * tex3 * generic0;
				mat_diffuse = mesh_color * combined.rgb;
				discard_alpha = combined.a;
				can_discard = true;
			}
			break;

		case 36: // Combiners_Mod_Mod_Depth
			mat_diffuse = mesh_color * tex1.rgb * tex2.rgb;
			discard_alpha = tex1.a * tex2.a;
			can_discard = true;
			break;

		default:
			mat_diffuse = mesh_color * tex1.rgb;
			break;
	}

	// calculate final opacity based on blend mode
	float final_opacity;
	bool do_discard = false;

	if (iBlendMode == 13) {
		// constant alpha blend
		final_opacity = discard_alpha * mesh_opacity;
	} else if (iBlendMode == 1) {
		// alpha key - hard cutoff
		final_opacity = mesh_opacity;
		if (can_discard && discard_alpha < alphaRef)
			do_discard = true;
	} else if (iBlendMode == 0) {
		// opaque
		final_opacity = mesh_opacity;
	} else if (iBlendMode == 4 || iBlendMode == 5) {
		// MOD and MOD2X blend modes: discard low alpha pixels to prevent holdout
		// these modes multiply destination by source, so low alpha = black = destroys destination
		final_opacity = discard_alpha * mesh_opacity;
		if (can_discard && discard_alpha < alphaRef)
			do_discard = true;
	} else {
		// other blend modes
		final_opacity = discard_alpha * mesh_opacity;
	}

	if (do_discard)
		discard;

	// apply lighting
	vec3 lit_color = calc_lighting(mat_diffuse, Normal);

	// add specular (disabled for debugging)
	// lit_color += specular;

	outColor = vec4(lit_color, final_opacity);


	// old
	/*
	vec4 colTexture = texture(texture1, TexCoord1);

	if (colTexture.a < alphaRef) { discard; }

	float diffuse = max(dot(normalize(Normal), normalize(lightDirection)), 0.0);
	float ambientStrength = 0.3;
	vec3 ambient = ambientStrength * vec3(1.0);
	vec3 lighting = ambient + diffuse;

	outColor = vec4(colTexture.rgb * lighting, colTexture.a);
	*/
}
