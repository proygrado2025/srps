#ifndef CUSTOM_HEMISPHERIC_AMBIENT_PASS_INCLUDED
#define CUSTOM_HEMISPHERIC_AMBIENT_PASS_INCLUDED

#include "../../ShaderLibrary/Common.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
float4 _BaseColor;
float4 _AmbientDownColor;
float4 _AmbientRangeColor;
CBUFFER_END 

struct Attributes {
	float4 positionOS   : POSITION;     // Position Object Space
	float3 normalOS     : NORMAL;       // Normal Object Space
    float2 texcoords    : TEXCOORD0;    // Texture Coordinates
};

struct Varyings {
	float4 positionCS   : SV_POSITION;  // Position World Space
    float2 uv           : TEXCOORD0;    // UV Coordinates
	float3 normalWS     : TEXCOORD1;    // Normal World Space
};

Varyings HemisphericAmbientPassVertex (Attributes input) {
	Varyings output = (Varyings)0;

	// Transform position from object to projection space (Clip Space)
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

    // tilling and offset texture transformations
    output.uv = TRANSFORM_TEX(input.texcoords, _BaseMap);

	// Transform normal from object to world space
	output.normalWS = UnityObjectToWorldNormal(input.normalOS.xyz);

	return output;
}

float3 CalcAmbient(float3 normal, float3 color) {
	// Convert from [-1, 1] to [0, 1]
	float up = normal.y * 0.5 + 0.5;

	// Calculate the ambient value
	float3 ambient = (1-up)*_AmbientDownColor.rgb + up * _AmbientRangeColor.rgb;

	// Apply the ambient value to the color
	return ambient * color;
}

float3 HemisphericAmbientPassFragment (Varyings input) : COLOR { 
	// Normalize the input normal
	float3 normal = normalize(input.normalWS);

    // Texture sampling
    float4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

	// Call the helper function and return the value
    // Because the pass is opaque, the alpha value is discarded
	return CalcAmbient(normal, color.rgb);
}

#endif