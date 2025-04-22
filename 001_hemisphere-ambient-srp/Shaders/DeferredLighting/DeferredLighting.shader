Shader "Hidden/DeferredLighting"
{
    SubShader
    {

        Tags { "RenderType" = "Opaque" "RenderPipeline" = ""}

        Pass
        {
            Name "DeferredLighting"

            ZTest Always
            ZWrite Off
            Cull Off


            HLSLPROGRAM
            // #pragma exclude_renderers gles d3d11_9x
            // #pragma enable_d3d11_debug_symbols
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "UnityCG.cginc"
            #include "Lighting.cginc"


            //#include "Packages/com.render-pipelines.custom/ShaderLibrary/FrameBufferFetchUtl.hlsl"
            UNITY_DECLARE_FRAMEBUFFER_INPUT_FLOAT(0); //depth
            UNITY_DECLARE_FRAMEBUFFER_INPUT_FLOAT(1); //normal


            static const uint NumSamples = 16;
            static const float NumSamplesRcp = 1.f /((float)NumSamples);

            CBUFFER_START(_CustomLight)
                // float3 _DirectionalLightColor;
                float3 _DirectionalLightDirection;

                float _aoIntensity;
                float _aoRadius;

                float _debugFloat;
                float2 _debugFloat2;
                float3 _debugFloat3;
            CBUFFER_END



            struct inVertex {
                float4 positionCS           : POSITION;  // Position World Space
                float3 uv                   : TEXCOORD0;    // Texture Coords
            };

            struct vertex2fragment {
                float4 positionCS           : SV_POSITION;  // Position World Space
                float2 uv                   : TEXCOORD0;
            };

            struct fragmentOut {
                float lighting                : SV_TARGET0;
                float ao                      : SV_TARGET1;
            };


            float nrand(float2 uv, float dx, float dy)
            {
                uv += float2(dx, dy + _Time.x);
                return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
            }

            float3 spherical_kernel(float2 uv, float index)
            {
                // Uniformaly distributed points
                // http://mathworld.wolfram.com/SpherePointPicking.html
                float u = nrand(uv, 0, index) * 2 - 1;
                float theta = nrand(uv, 1, index) * UNITY_PI * 2;
                float u2 = sqrt(1 - u * u);
                float3 v = float3(u2 * cos(theta), u2 * sin(theta), u);
                // Adjustment for distance distribution.
                float l = index / NumSamples;
                return v * lerp(0.1, 1.0, l * l);
            }

            vertex2fragment Vertex(inVertex inData)
            {
                vertex2fragment output = (vertex2fragment)0;

                output.positionCS = inData.positionCS;
                output.uv = inData.uv; // coordenadas en el rango [0,1]

                return output;
            }

            fragmentOut Fragment(vertex2fragment v2fInput)
            {
                fragmentOut output = (fragmentOut)0;

                float2 pos = v2fInput.positionCS;

                float3 normalWS = normalize((UNITY_READ_FRAMEBUFFER_INPUT(1, pos).rgb * 2.0h - 1.0h));
                float linearDepth = LinearEyeDepth(UNITY_READ_FRAMEBUFFER_INPUT(0, pos).r);



                // reevaluar el uso de unity_WorldToCamera para la pasada de sombras
                float3 normalVS = normalize(mul((float3x3)unity_WorldToCamera,normalWS));

                // Reconstruct the view-space position.
                float2 p11_22 = float2(unity_CameraProjection._11, unity_CameraProjection._22);
                float2 p13_31 = float2(unity_CameraProjection._13, unity_CameraProjection._23);
                float3 posVS = float3((v2fInput.uv * 2 - 1 - p13_31) / p11_22, 1) * linearDepth;


                float3x3 proj = (float3x3)unity_CameraProjection;

                float occ = 0.0;
                [unroll]
                for (uint s = 1; s < NumSamples; s++)
                {
                    float3 deltaVS = spherical_kernel(v2fInput.uv, s);

                    // Wants a sample in normal oriented hemisphere.
                    deltaVS *= (dot(normalVS, deltaVS) >= 0) * 2 - 1;

                    // Sampling point.
                    float3 posSampleVS = posVS + deltaVS * _aoRadius;

                    // Re-project the sampling point.
                    float3 pos_sc = mul(proj, posSampleVS);
                    float2 uv_s = (pos_sc.xy / posSampleVS.z + 1) * 0.5;


                    float2 pos_sCS = float2(uv_s.x*_ScreenParams.x,(1-uv_s.y)*_ScreenParams.y);

                    // Sample a linear depth at the sampling point.
                    float depth_s = LinearEyeDepth(UNITY_READ_FRAMEBUFFER_INPUT(0, pos_sCS));

                    // Occlusion test.
                    float dist = posSampleVS.z - depth_s;
                    occ += (dist > 0.01 * _aoRadius);
                }

                occ = saturate(occ * _aoIntensity * NumSamplesRcp);

                // incidencia de la dirección de la luz
                float NdotL = saturate(dot(normalWS, _DirectionalLightDirection));
                output.lighting = NdotL;

                output.ao = occ;

                // output.ao = posVS/_debugFloat;

                return output;
            }

            ENDHLSL
        }

        Pass
        {
            Name "ApplyAO"

            ZTest Always
            ZWrite Off
            Cull Off


            HLSLPROGRAM
            // #pragma exclude_renderers gles d3d11_9x
            // #pragma enable_d3d11_debug_symbols
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            //#include "Packages/com.render-pipelines.custom/ShaderLibrary/FrameBufferFetchUtl.hlsl"
            UNITY_DECLARE_FRAMEBUFFER_INPUT_FLOAT(0); //albedo
            UNITY_DECLARE_FRAMEBUFFER_INPUT_FLOAT(1); //ao
            UNITY_DECLARE_FRAMEBUFFER_INPUT_FLOAT(2); //lighting


            static const int sampleSize = 2;
            static const int fullSampleSize = (sampleSize*2+1)^2;
            static const float invOuterCount = 1.f/max(fullSampleSize-1,1);
            // static const float invFullCount = 1.f/fullSampleSize;

            CBUFFER_START(_CustomLight)
                float3 _DirectionalLightColor;
                float _aoBlurCenterWeight;

                float _debugFloat;
                float2 _debugFloat2;
                float3 _debugFloat3;
            CBUFFER_END



            struct inVertex {
                float4 positionCS           : POSITION;  // Position World Space
                // float3 uv                   : TEXCOORD1;    // Texture Coords
            };

            struct vertex2fragment {
                float4 positionCS           : SV_POSITION;  // Position World Space
                // float2 uv                   : TEXCOORD0;
            };

            struct ColorValue{
                float4 color        : SV_TARGET0;
            };

            vertex2fragment Vertex(inVertex inData)
            {
                vertex2fragment output = (vertex2fragment)0;

                output.positionCS = inData.positionCS;
                // output.uv = inData.uv; // coordenadas en el rango [0,1]

                return output;
            }

            ColorValue Fragment(vertex2fragment v2fInput)
            {
                ColorValue output = (ColorValue)0;

                float2 pos = v2fInput.positionCS;

                float4 albedo = UNITY_READ_FRAMEBUFFER_INPUT(0, pos);
                float lighting = UNITY_READ_FRAMEBUFFER_INPUT(2, pos).r;

                // output.color = UNITY_READ_FRAMEBUFFER_INPUT(1, pos)*_debugFloat2.x;
                // return output;

                float aoOtherWeight = (1.f-_aoBlurCenterWeight)*invOuterCount;

                float blurAO = 0.0;
                UNITY_UNROLL
                for (int deltaU = -sampleSize; deltaU <= sampleSize; deltaU++)
                {
                    UNITY_UNROLL
                    for(int deltaV = -sampleSize; deltaV <= sampleSize; deltaV++)
                    {
                        float isCenter = (deltaU==0 & deltaV==0);

                        float sampleAO = UNITY_READ_FRAMEBUFFER_INPUT(1, pos+float2(deltaU,deltaV));
                        blurAO += ((isCenter*_aoBlurCenterWeight)+((1-isCenter) * aoOtherWeight))*sampleAO;
                    }
                }

                // return 1-blurAO.xxxx;

                //return blurAO.xyzz;
                // return albedo+float4(lerp(_DirectionalLightColor*_debugFloat, (float4)0.0, saturate(blurAO)), 0);
                output.color = lerp(albedo*(lighting), (float4)0.0, saturate(blurAO));
                // return half4(lerp(float3(1,1,1), (half3)0.0, blurAO), albedo.a);

                // output.color = albedo*lighting.x;
                return output;
            }

            ENDHLSL
        }
    }
}