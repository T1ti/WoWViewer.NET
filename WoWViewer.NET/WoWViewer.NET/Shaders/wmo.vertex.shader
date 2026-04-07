#version 450

layout(location = 0) uniform mat4 projection_matrix;
layout(location = 1) uniform mat4 view_matrix;
layout(location = 2) uniform mat4 model_matrix;

layout(location = 3) uniform float vertexShader;
layout(location = 4) uniform float pixelShader;

in vec3 position;
in vec3 normal;
in vec2 texCoord;
in vec2 texCoord2;
in vec2 texCoord3;
in vec2 texCoord4;
in vec4 color1;
in vec4 color2;
in vec4 color3;

layout(location = 10) in vec4 instanceMatrix0;
layout(location = 11) in vec4 instanceMatrix1;
layout(location = 12) in vec4 instanceMatrix2;
layout(location = 13) in vec4 instanceMatrix3;

out vec3 Normal;
out vec2 TexCoord;
out vec2 TexCoord2;
out vec2 TexCoord3;
out vec2 TexCoord4;
out vec4 vColor1;
out vec4 vColor2;
out vec4 vColor3;

// Based on deamon's webwowviewercpp

vec2 posToTexCoord(const vec3 vertexPosInView, const vec3 normal){
    //Blizz seems to have vertex in view space as vector from "vertex to eye", while in this implementation, it's
    //vector from "eye to vertex". So the minus here is not needed
    //vec3 viewVecNormalized = -normalize(cameraPoint.xyz);
    vec3 viewVecNormalized = normalize(vertexPosInView.xyz);
    vec3 reflection = reflect(viewVecNormalized, normalize(normal));
    vec3 temp_657 = vec3(reflection.x, reflection.y, (reflection.z + 1.0));

    return ((normalize(temp_657).xy * 0.5) + vec2(0.5));
}

void main()
{
	mat4 instanceMatrix = mat4(instanceMatrix0, instanceMatrix1, instanceMatrix2, instanceMatrix3);

	gl_Position = projection_matrix * view_matrix * instanceMatrix * vec4(position, 1);

	mat4 modelViewMatrix = view_matrix * instanceMatrix;
	mat3 normalMatrix = transpose(inverse(mat3(modelViewMatrix)));
	Normal = normalize(normalMatrix * normal);

	vColor1 = color1;
	vColor2 = color2;
	vColor3 = color3;

	int VertexShader = int(vertexShader);

	if(VertexShader == -1){
		TexCoord = texCoord;
		TexCoord2 = texCoord2;
		TexCoord3 = texCoord3;
	} else if(VertexShader == 0){
		TexCoord = texCoord;
		TexCoord2 = texCoord2;
		TexCoord3 = texCoord3;
	} else if(VertexShader == 1){
		TexCoord = texCoord;
		TexCoord2 = reflect(normalize(vec3(1.0)), normal).xy; // TODO: 1.0 => cameraPoint
		TexCoord3 = texCoord3;
	} else if(VertexShader == 2){
		TexCoord = texCoord;
		TexCoord2 = texCoord2;
		TexCoord3 = texCoord3;
    } else if (VertexShader == 2) { //MapObjDiffuse_T1_Env_T2
        TexCoord = texCoord;
        TexCoord2 = posToTexCoord(position.xyz, normal);;
        TexCoord3 = texCoord3;
    } else if (VertexShader == 3) { //MapObjSpecular_T1
        TexCoord = texCoord;
        TexCoord2 = texCoord2; //not used
        TexCoord3 = texCoord3; //not used
    } else if (VertexShader == 4) { //MapObjDiffuse_Comp
        TexCoord = texCoord;
        TexCoord2 = texCoord2;
        TexCoord3 = texCoord3; //not used
    } else if (VertexShader == 5) { //MapObjDiffuse_Comp_Refl
        TexCoord = texCoord;
        TexCoord2 = texCoord2;
        TexCoord3 = reflect(normalize(vec3(1.0)), normal).xy; // TODO: 1.0 => cameraPoint
    } else if (VertexShader == 6) { //MapObjDiffuse_Comp_Terrain
        TexCoord = texCoord;
        TexCoord2 = position.xy * -0.239999995;
        TexCoord3 = texCoord3; //not used
    } else if (VertexShader == 7) { //MapObjDiffuse_CompAlpha
        TexCoord = texCoord;
        TexCoord2 = position.xy * -0.239999995;
        TexCoord3 = texCoord3; //not used
    } else if (VertexShader == 8) { //MapObjParallax
        TexCoord = texCoord;
        TexCoord2 = texCoord2;
    } else {
        TexCoord = vec2(0.0, 1.0);
        TexCoord2 = vec2(0.0, 1.0);
        TexCoord3 = vec2(0.0, 1.0);
    }

    TexCoord4 = texCoord4;
}
