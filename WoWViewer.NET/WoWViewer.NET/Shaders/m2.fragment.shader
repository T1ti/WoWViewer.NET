#version 430

in vec2 TexCoord;
in vec3 Normal;
out vec4 outColor;

uniform float alphaRef;
uniform sampler2D colorTexture;
layout(location = 5) uniform vec3 lightDirection;

void main()
{
	vec4 colTexture = texture(colorTexture, TexCoord);

	if (colTexture.a < alphaRef) { discard; }

	float diffuse = max(dot(normalize(Normal), normalize(lightDirection)), 0.0);
	float ambientStrength = 0.3;
	vec3 ambient = ambientStrength * vec3(1.0);
	vec3 lighting = ambient + diffuse;

	outColor = vec4(colTexture.rgb * lighting, colTexture.a);
}
