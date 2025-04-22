#ifndef CUSTOM_GROUND_TRUTH_AMBIENT_OCCLUSION_PASS_INCLUDED
#define CUSTOM_GROUND_TRUTH_AMBIENT_OCCLUSION_PASS_INCLUDED

#include "UnityCG.cginc"

CBUFFER_START(UnityPerMaterial)
sampler2D _BaseMap;
float4 _BaseMap_ST;
float4 _BaseColor;
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

struct GBufferValues{
    float4 color        : SV_TARGET0;
    float4 normal       : SV_TARGET1;
};

Varyings DeferredLightingGBufferPassVertex (Attributes input) {
	Varyings output = (Varyings)0;

	// Transform position from object to projection space (Clip Space)
    output.positionCS = UnityObjectToClipPos(input.positionOS.xyz);

    // tilling and offset texture transformations
    output.uv = TRANSFORM_TEX(input.texcoords, _BaseMap);

	// Transform normal from object to world space
	output.normalWS = UnityObjectToWorldNormal(input.normalOS.xyz);

	return output;
}

GBufferValues DeferredLightingGBufferPassFragment (Varyings input) { 
    GBufferValues output = (GBufferValues)0;

    // Texture sampling
    output.color = tex2D(_BaseMap, input.uv) * _BaseColor;

	// Normalize the input normal
	output.normal = float4(normalize(input.normalWS)*float(0.5) + float(0.5),0);

	return output;
}

#endif