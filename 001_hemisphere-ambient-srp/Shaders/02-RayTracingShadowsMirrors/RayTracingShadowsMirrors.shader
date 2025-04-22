Shader "Custom SRP/Ray Tracing Shadows Mirrors Material"
{
    Properties
    {
    	// TODO: revisar si es necesario el ShaderPropertyFlags.MainTexture
    	// https://docs.unity3d.com/ScriptReference/Rendering.ShaderPropertyFlags.MainTexture.html
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1.0, 0.0, 1.0, 1.0)
        [KeywordEnum(DIFFUSE, MIRROR, TRANSPARENT)] _GI_STATE("Surface Type", float) = 0
        [Header(Reflective stuff)]
        [Slider] _ReflectiveStrength ("Reflective/Refractive Strength", Range (0, 1)) = 0.5
        [Header(Refractive stuff)]
        _RefractiveIndex("Refractive Index", Range(1.0, 2.0)) = 1.55
    }

    SubShader
    {
        Tags {  "LightMode" = "SceneViewLightMode" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #include "../SRPUtils.hlsl"
            ENDCG
        }
    }


    SubShader
    {
        Pass
        {
        // SetShaderPass must use this name in order to execute the ray tracing shaders from this Pass.
            Name "Test"

            Tags{ "LightMode" = "RayTracing" }

            HLSLPROGRAM

            #pragma shader_feature _GI_STATE_DIFFUSE _GI_STATE_MIRROR _GI_STATE_TRANSPARENT

            #pragma raytracing test

            #include "UnityRaytracingMeshUtils.cginc"
            #include "RayPayload.hlsl"

            Texture2D _BaseMap;
            float4 _BaseMap_ST;
            float4 _BaseColor;
            SamplerState sampler_linear_repeat;
            RaytracingAccelerationStructure g_SceneAccelStruct;
            float _ReflectiveStrength;
            float _RefractiveIndex;
            float3 g_sun_direction;

            struct AttributeData
            {
                float2 barycentrics;
            };


            struct Vertex
            {
                float3 position;
                float3 normal;
                float2 uv;
            };


            Vertex FetchVertex(uint vertexIndex)
            {
                Vertex v;
                v.position  = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
                v.normal    = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
                v.uv        = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord0);
                return v;
            }

            Vertex InterpolateVertices(Vertex v0, Vertex v1, Vertex v2, float3 barycentrics)
            {
                Vertex v;
                #define INTERPOLATE_ATTRIBUTE(attr) v.attr = v0.attr * barycentrics.x + v1.attr * barycentrics.y + v2.attr * barycentrics.z
                INTERPOLATE_ATTRIBUTE(position);
                INTERPOLATE_ATTRIBUTE(normal);
                INTERPOLATE_ATTRIBUTE(uv);
                return v;
            }

            void fresnel(in float3 I, in float3 N, in float ior, out float kr)
            {
                float cosi = clamp(-1, 1, dot(I, N));
                float etai = 1, etat = ior;
                if (cosi > 0)
                { 
                    float temp = etai;
                    etai = etat;
                    etat = temp;
                }
                // Compute sini using Snell's law
                float sint = etai / etat * sqrt(max(0.f, 1 - cosi * cosi));
                // Total internal reflection
                if (sint >= 1)
                {
                    kr = 1;
                }
                else 
                {
                    float cost = sqrt(max(0, 1 - sint * sint));
                    cosi = abs(cosi);
                    float Rs = ((etat * cosi) - (etai * cost)) / ((etat * cosi) + (etai * cost));
                    float Rp = ((etai * cosi) - (etat * cost)) / ((etai * cosi) + (etat * cost));
                    kr = (Rs * Rs + Rp * Rp) / 2;
                }
                // As a consequence of the conservation of energy, transmittance is given by:
                // kt = 1 - kr;
            }

            [shader("closesthit")]
            void ClosestHitMain(inout RayPayload payload : SV_RayPayload, in AttributeData attribs : SV_IntersectionAttributes)
            {
                uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

                Vertex v0, v1, v2;
                v0 = FetchVertex(triangleIndices.x);
                v1 = FetchVertex(triangleIndices.y);
                v2 = FetchVertex(triangleIndices.z);

                float3 barycentricCoords = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
                Vertex v = InterpolateVertices(v0, v1, v2, barycentricCoords);

                // Shadow ray from the SUN
                float3 vecToLightNorm = normalize(-g_sun_direction);
                float3 worldPosition = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();

                float3 faceNormal = normalize(mul(v.normal, (float3x3)WorldToObject()));
                bool isFrontFace = (HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE);
                faceNormal = isFrontFace ? faceNormal : -faceNormal;

                payload.color = _BaseMap.SampleLevel(sampler_linear_repeat, v.uv * _BaseMap_ST.xy + _BaseMap_ST.zw, 0) * _BaseColor;

                float cosNL = max(dot(vecToLightNorm, faceNormal),0.1);
                payload.color *= cosNL;

				// solo se lanza un rayo de sombra cuando es necesario
                if( cosNL > 0.15){

                    RayDesc shadowRay;
                    shadowRay.Origin = worldPosition + vecToLightNorm * 0.001f;
                    shadowRay.Direction = vecToLightNorm;
                    shadowRay.TMin = 0;
                    shadowRay.TMax = 1e20f;

                    RayPayloadShadow payloadShadow;
                    payloadShadow.shadowValue = .5;

                    const uint missShaderForShadowRay = 0;
                    TraceRay(g_SceneAccelStruct, RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER /*| RAY_FLAG_CULL_BACK_FACING_TRIANGLES*/, 0xFF, 0, 0, missShaderForShadowRay, shadowRay, payloadShadow);

                    if( payloadShadow.shadowValue != 1){
                        payload.color *= payloadShadow.shadowValue;
                    }
                }

                if( payload.bounceIndex < 10)
                {
                    RayDesc ray;

                    const uint missShaderIndex = 1;

                    #if _GI_STATE_MIRROR
                    float3 reflectedRay = reflect(WorldRayDirection(), faceNormal);

                    ray.Origin = worldPosition;
                    ray.Direction = reflectedRay;
                    ray.TMin = 0.001f;
                    ray.TMax = 1e20f;

                    RayPayload reflRayPayload;
                    reflRayPayload.color = float4(0, 0, 0, 0);
                    reflRayPayload.bounceIndex = payload.bounceIndex + 1;

                    TraceRay(g_SceneAccelStruct, 0, 0xFF, 0, 0, missShaderIndex, ray, reflRayPayload);

                    payload.color = lerp(payload.color*_BaseColor, reflRayPayload.color, _ReflectiveStrength);
                    #endif

                    #if _GI_STATE_TRANSPARENT
                    float refractiveIndex = isFrontFace ? (1.0f / _RefractiveIndex) : (_RefractiveIndex / 1.0f);

                    float kr;
                    fresnel(WorldRayDirection(), faceNormal, _RefractiveIndex, kr);

                    float3 refractedRay = refract(WorldRayDirection(), faceNormal, refractiveIndex);

                    ray.Origin = worldPosition ;
                    ray.Direction = refractedRay;
                    ray.TMin = 0.001f;
                    ray.TMax = 1e20f;

                    RayPayload refrRayPayload;
                    refrRayPayload.color = float4(0, 0, 0, 0);
                    refrRayPayload.bounceIndex = payload.bounceIndex + 1;

                    TraceRay(g_SceneAccelStruct, 0, 0xFF, 0, 0, missShaderIndex, ray, refrRayPayload);

                    payload.color = lerp(payload.color*_BaseColor, refrRayPayload.color, _ReflectiveStrength);

                    #endif
                }
            }

            ENDHLSL
        }
    }
}