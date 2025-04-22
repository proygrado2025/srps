Shader "Custom SRP/Ray Tracing Material"
{
    Properties
    {
    	// TODO: revisar si es necesario el ShaderPropertyFlags.MainTexture
    	// https://docs.unity3d.com/ScriptReference/Rendering.ShaderPropertyFlags.MainTexture.html
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1.0, 0.0, 1.0, 1.0)
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

            #pragma multi_compile_local RAY_TRACING_PROCEDURAL_GEOMETRY

            #pragma raytracing test

            #include "UnityRaytracingMeshUtils.cginc"

            Texture2D _BaseMap;
            float4 _BaseMap_ST;
            float4 _BaseColor;
            SamplerState sampler_linear_repeat;
            // RaytracingAccelerationStructure g_SceneAccelStruct;


            struct AttributeData
            {
                float2 barycentrics;
            };

            struct RayPayload
            {
                float4 color;
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
            
            [shader("closesthit")]
            void ClosestHitMain(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
            {
                uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

                Vertex v0, v1, v2;
                v0 = FetchVertex(triangleIndices.x);
                v1 = FetchVertex(triangleIndices.y);
                v2 = FetchVertex(triangleIndices.z);

                float3 barycentricCoords = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
                Vertex v = InterpolateVertices(v0, v1, v2, barycentricCoords);


                float4 color = _BaseMap.SampleLevel(sampler_linear_repeat, v.uv * _BaseMap_ST.xy + _BaseMap_ST.zw, 0) * _BaseColor;

                if( color.a == 1)
                {
                    payload.color = color;
                }
                else
                {
                    payload.color = float4(1,0,1,0);

                    // float3 worldRayOrigin = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();

                    // // esta es la forma compacta
                    // // RayDesc reflectedRay = { worldRayOrigin, SceneConstants.Epsilon,
                    // //         ReflectRay(WorldRayDirection(), worldNormal),
                    // //         SceneConstants.TMax };
                    // RayDesc ray;
                    // ray.Origin    = worldRayOrigin;
                    // ray.Direction = WorldRayDirection();
                    // ray.TMin      = 0.0001f;
                    // ray.TMax      = 1000.0f;

                    // RayPayload transparentPayload;
                    // // TraceRay(g_SceneAccelStruct, 0, 0xFF, 0, 1, 0, ray, transparentPayload);

                    // // payload.color = payload.color.rgb*payload.color.a + transparentPayload.color.rgb*(1-payload.color.a);
                }

            }

            ENDHLSL
        }
    }
}