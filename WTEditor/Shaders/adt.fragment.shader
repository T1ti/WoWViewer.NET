#version 430

in vec2 TexCoord;
in vec4 VColor;
in vec3 Normal;
out vec4 out_col0;

uniform int layerCount;

uniform float heightScales[8];
uniform float heightOffsets[8];
uniform float layerScales[8];

uniform sampler2D diffuseLayers[8];
uniform sampler2D heightLayers[8];
uniform sampler2D alphaLayers[2];

layout(location = 5) uniform vec3 lightDirection;

// Based on https://github.com/Kruithne/wow.export/blob/main/src/shaders/adt.fragment.shader but without the sampler2darrays because I'm stoopid
void main()
{
	vec4 in_vertexColor = VColor;

	float alphas[8];
	vec4 alpha0 = texture(alphaLayers[0], mod(TexCoord, 1.0));
	vec4 alpha1 = texture(alphaLayers[1], mod(TexCoord, 1.0));

	alphas[0] = 1.0;
	alphas[1] = alpha0.g;
	alphas[2] = alpha0.b;
	alphas[3] = alpha0.a;
	alphas[4] = alpha1.r;
	alphas[5] = alpha1.g;
	alphas[6] = alpha1.b;
	alphas[7] = alpha1.a;

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

	float diffuse = max(dot(normalize(Normal), normalize(lightDirection)), 0.0);
	float ambientStrength = 0.3;
	vec3 ambient = ambientStrength * vec3(1.0);
	vec3 lighting = ambient + diffuse;
	vec4 result_color = vec4(final_color * in_vertexColor.rgb * 2.0 * lighting, 1.0);
	out_col0 =  result_color;
}
