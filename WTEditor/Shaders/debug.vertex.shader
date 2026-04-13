#version 430 core

layout (location = 0) in vec3 position;
layout (location = 1) in vec4 color;

uniform mat4 projection_matrix;
uniform mat4 view_matrix;

out vec4 fragColor;

void main()
{
    gl_Position = projection_matrix * view_matrix * vec4(position, 1.0);
    fragColor = color;
}
