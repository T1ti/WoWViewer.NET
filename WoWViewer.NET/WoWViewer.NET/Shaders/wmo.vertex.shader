#version 450

uniform mat4 projection_matrix;
uniform mat4 view_matrix;
uniform mat4 model_matrix;

uniform float vertexShader;

in vec3 position;
in vec3 normal;
in vec2 texCoord;
in vec2 texCoord2;
in vec2 texCoord3;
in vec2 texCoord4;

out vec3 Normal;
out vec2 TexCoord;
out vec2 TexCoord2;
out vec2 TexCoord3;
out vec2 TexCoord4;

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
	gl_Position = projection_matrix * view_matrix * model_matrix * vec4(position, 1);

    mat4 viewModelMatForNormal = transpose(inverse(view_matrix));
    Normal = normalize(viewModelMatForNormal * vec4(normal, 0.0)).xyz;

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
        TexCoord3 = texCoord2; //not used
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
        TexCoord3 = texCoord3;
    }
}
