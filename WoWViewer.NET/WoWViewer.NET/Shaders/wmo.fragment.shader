#version 330

in vec2 TexCoord;
//in vec2 TexCoord2;
//in vec2 TexCoord3;
//in vec2 TexCoord4;
// in int PixelShader;
out vec4 outColor;

uniform float alphaRef;

uniform sampler2D colorTexture;


void main()
{

	vec4 colTexture = texture(colorTexture, TexCoord);

	if (colTexture.a < alphaRef) { discard; }

	outColor = colTexture;
}
