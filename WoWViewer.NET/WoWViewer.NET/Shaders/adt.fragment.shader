#version 330

in vec2 TexCoord;
in vec4 VColor;
out vec4 out_col0;

uniform int layerCount;

uniform float heightScales[8];
uniform float heightOffsets[8];
uniform float layerScales[8];

uniform sampler2D diffuseLayers[8];
uniform sampler2D heightLayers[8];
uniform sampler2D alphaLayers[8];

// Based on https://github.com/Kruithne/wow.export/blob/main/src/shaders/adt.fragment.shader but without the sampler2darrays because I'm stoopid
void main()
{
	vec4 in_vertexColor = VColor;

	float alphas[8];
	alphas[0] = 1.0;
	alphas[1] = texture(alphaLayers[1], mod(TexCoord, 1.0)).r;
	alphas[2] = texture(alphaLayers[2], mod(TexCoord, 1.0)).r;
	alphas[3] = texture(alphaLayers[3], mod(TexCoord, 1.0)).r;
	alphas[4] = texture(alphaLayers[4], mod(TexCoord, 1.0)).r;
	alphas[5] = texture(alphaLayers[5], mod(TexCoord, 1.0)).r;
	alphas[6] = texture(alphaLayers[6], mod(TexCoord, 1.0)).r;
	alphas[7] = texture(alphaLayers[7], mod(TexCoord, 1.0)).r;

	vec3 alpha_sum = vec3(
		alphas[1] + alphas[2] + alphas[3] + alphas[4] + alphas[5] + alphas[6] + alphas[7]
	);

	float layer_weights[8];
	layer_weights[0] = 1.0 - clamp(alpha_sum.x, 0.0, 1.0);

	for (int i = 1; i < 8; i++)
		layer_weights[i] = alphas[i];

	float layer_pcts[8];
	for (int i = 0; i < 8; i++) {
		vec2 tc = TexCoord * (8.0 / layerScales[i]);
		float height_val = texture(heightLayers[i], tc).a;
		layer_pcts[i] = layer_weights[i] * (height_val * heightScales[i] + heightOffsets[i]);
	}

	float max_pct = 0.0;
	for (int i = 0; i < 8; i++)
		max_pct = max(max_pct, layer_pcts[i]);

	for (int i = 0; i < 8; i++)
		layer_pcts[i] = layer_pcts[i] * (1.0 - clamp(max_pct - layer_pcts[i], 0.0, 1.0));

	float pct_sum = 0.0;
	for (int i = 0; i < 8; i++)
		pct_sum += layer_pcts[i];

	for (int i = 0; i < 8; i++)
		layer_pcts[i] = layer_pcts[i] / pct_sum;

	vec3 final_color = vec3(0.0);
	for (int i = 0; i < 8; i++) {
		vec2 tc = TexCoord * (8.0 / layerScales[i]);
		vec4 layer_sample = texture(diffuseLayers[i], tc);
		final_color += layer_sample.rgb * layer_pcts[i];
	}

	out_col0 = vec4(final_color * in_vertexColor.rgb * 2.0, 1.0);
}
