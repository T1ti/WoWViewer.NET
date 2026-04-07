#version 430

layout(location = 0) uniform mat4 projection_matrix;
layout(location = 1) uniform mat4 view_matrix;
layout(location = 2) uniform mat4 model_matrix;

in vec3 position;
in vec3 normal;
in vec2 texCoord;

// Instance matrix attributes (location 10-13)
layout(location = 10) in vec4 instanceMatrix0;
layout(location = 11) in vec4 instanceMatrix1;
layout(location = 12) in vec4 instanceMatrix2;
layout(location = 13) in vec4 instanceMatrix3;

out vec2 TexCoord;
out vec3 Normal;

void main()
{
	mat4 instanceMatrix = mat4(instanceMatrix0, instanceMatrix1, instanceMatrix2, instanceMatrix3);

	gl_Position = projection_matrix * view_matrix * instanceMatrix * vec4(position, 1);
	TexCoord = texCoord;

	mat4 modelViewMatrix = view_matrix * instanceMatrix;
	mat3 normalMatrix = transpose(inverse(mat3(modelViewMatrix)));
	Normal = normalize(normalMatrix * normal);
}
