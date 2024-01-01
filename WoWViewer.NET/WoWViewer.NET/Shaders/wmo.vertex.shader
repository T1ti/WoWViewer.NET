#version 330

uniform mat4 projection_matrix;
uniform mat4 view_matrix;
uniform mat4 model_matrix;

//in int vertexShader;
//in int pixelShader;

in vec3 position;
in vec2 texCoord;
in vec2 texCoord2;
in vec2 texCoord3;
in vec2 texCoord4;

// out int PixelShader;
out vec2 TexCoord;
// out vec2 TexCoord2;
// out vec2 TexCoord3;
// out vec2 TexCoord4;

void main()
{
	gl_Position = projection_matrix * view_matrix * model_matrix * vec4(position, 1);

	TexCoord = texCoord;
	// TexCoord2 = texCoord2;
	// TexCoord3 = texCoord3;
	// TexCoord4 = texCoord4;

	//PixelShader = pixelShader;
}
