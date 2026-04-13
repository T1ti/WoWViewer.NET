#version 430

layout(location = 0) uniform mat4 model_matrix;
layout(location = 1) uniform mat4 projection_matrix;
layout(location = 2) uniform mat4 rotation_matrix;

uniform vec3 firstPos;

in vec3 position;
in vec3 normal;
in vec2 texCoord;
in vec4 color;

out vec2 TexCoord;
out vec4 VColor;
out vec3 Normal;

void main()
{
	gl_Position = projection_matrix * model_matrix * rotation_matrix * vec4(vec3(position.x - firstPos.x, position.y - firstPos.y, position.z), 1);
	TexCoord = texCoord;
	Normal = normal;
	VColor = color;
}
