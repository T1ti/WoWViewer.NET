#version 430

layout(location = 0) uniform mat4 projection_matrix;
layout(location = 1) uniform mat4 view_matrix;

layout(location = 3) uniform float vertexShader;
// 4 pixel shader
// 5 light dir
// 6 blend mode

in vec3 position;
in vec3 normal;
in vec2 texCoord1;
in vec2 texCoord2;

// Instance matrix attributes (location 10-13)
layout(location = 10) in vec4 instanceMatrix0;
layout(location = 11) in vec4 instanceMatrix1;
layout(location = 12) in vec4 instanceMatrix2;
layout(location = 13) in vec4 instanceMatrix3;

// Texture transform matrices
uniform mat4 texMatrix1;
uniform mat4 texMatrix2;
//uniform int hasTexMatrix1;
//uniform int hasTexMatrix2;

out vec2 TexCoord1;
out vec2 TexCoord2;
out vec2 TexCoord3;
out float EdgeFade;
out vec3 Normal;

void main()
{
	mat4 instanceMatrix = mat4(instanceMatrix0, instanceMatrix1, instanceMatrix2, instanceMatrix3);

	gl_Position = projection_matrix * view_matrix * instanceMatrix * vec4(position, 1);

	mat4 modelViewMatrix = view_matrix * instanceMatrix;
	mat3 normalMatrix = transpose(inverse(mat3(modelViewMatrix)));
	Normal = normalize(normalMatrix * normal);

	// TODO: hook up
	float vEdgeFade = 1.0;
	vec2 envCoord = vec2(0.0);
	int hasTexMatrix1 = 0;
	int hasTexMatrix2 = 0;

	mat4 textureMatrix1 = hasTexMatrix1 != 0 ? texMatrix1 : mat4(1.0);
	mat4 textureMatrix2 = hasTexMatrix2 != 0 ? texMatrix2 : mat4(1.0);

	TexCoord1 = texCoord1;
	TexCoord2 = vec2(0.0);
	TexCoord3 = vec2(0.0);

	EdgeFade = 1.0;

	int iVertexShader = int(vertexShader);
	switch (iVertexShader) {
		case 0: // Diffuse_T1
			TexCoord1 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			break;

		case 1: // Diffuse_Env
			TexCoord1 = envCoord;
			break;

		case 2: // Diffuse_T1_T2
			TexCoord1 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			TexCoord2 = (textureMatrix2 * vec4(texCoord2, 0.0, 1.0)).xy;
			break;

		case 3: // Diffuse_T1_Env
			TexCoord1 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			TexCoord2 = envCoord;
			break;

		case 4: // Diffuse_Env_T1
			TexCoord1 = envCoord;
			TexCoord2 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			break;

		case 5: // Diffuse_Env_Env
			TexCoord1 = envCoord;
			TexCoord2 = envCoord;
			break;

		case 6: // Diffuse_T1_Env_T1
			TexCoord1 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			TexCoord2 = envCoord;
			TexCoord3 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			break;

		case 7: // Diffuse_T1_T1
			TexCoord1 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			TexCoord2 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			break;

		case 8: // Diffuse_T1_T1_T1
			TexCoord1 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			TexCoord2 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			TexCoord3 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			break;

		case 9: // Diffuse_EdgeFade_T1
			//v_edge_fade = edge_scan;
			TexCoord1 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			break;

		case 10: // Diffuse_T2
			TexCoord1 = (textureMatrix2 * vec4(texCoord2, 0.0, 1.0)).xy;
			break;

		case 11: // Diffuse_T1_Env_T2
			TexCoord1 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			TexCoord2 = envCoord;
			TexCoord3 = (textureMatrix2 * vec4(texCoord2, 0.0, 1.0)).xy;
			break;

		case 12: // Diffuse_EdgeFade_T1_T2
			//v_edge_fade = edge_scan;
			TexCoord1 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			TexCoord2 = (textureMatrix2 * vec4(texCoord2, 0.0, 1.0)).xy;
			break;

		case 13: // Diffuse_EdgeFade_Env
			//v_edge_fade = edge_scan;
			TexCoord1 = envCoord;
			break;

		case 14: // Diffuse_T1_T2_T1
			TexCoord1 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			TexCoord2 = (textureMatrix2 * vec4(texCoord2, 0.0, 1.0)).xy;
			TexCoord3 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			break;

		case 15: // Diffuse_T1_T2_T3
			TexCoord1 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			TexCoord2 = (textureMatrix2 * vec4(texCoord2, 0.0, 1.0)).xy;
			TexCoord3 = TexCoord2;
			break;

		case 16: // Color_T1_T2_T3
			TexCoord1 = (textureMatrix2 * vec4(texCoord2, 0.0, 1.0)).xy;
			TexCoord2 = vec2(0.0);
			TexCoord3 = TexCoord2;
			break;

		case 17: // BW_Diffuse_T1
		case 18: // BW_Diffuse_T1_T2
			TexCoord1 = (textureMatrix1 * vec4(texCoord1, 0.0, 1.0)).xy;
			break;

		default:
			TexCoord1 = texCoord1;
			break;
	}
}
