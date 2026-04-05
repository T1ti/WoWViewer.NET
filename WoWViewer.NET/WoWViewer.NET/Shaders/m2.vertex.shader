#version 330

uniform mat4 projection_matrix;
uniform mat4 view_matrix;
uniform mat4 model_matrix;

in vec3 position;
in vec3 normal;
in vec2 texCoord;

out vec2 TexCoord;
out vec3 Normal;

void main()
{
	gl_Position = projection_matrix * view_matrix * model_matrix * vec4(position, 1);
	TexCoord = texCoord;

	mat4 modelViewMatrix = view_matrix * model_matrix;
	mat3 normalMatrix = transpose(inverse(mat3(modelViewMatrix)));
	Normal = normalize(normalMatrix * normal);
}
