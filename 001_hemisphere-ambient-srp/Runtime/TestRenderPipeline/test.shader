Shader "Hidden/testSRP"
{
    SubShader
    {
    
        Tags { "RenderType" = "Opaque" "RenderPipeline" = ""}

        Pass
        {
            Tags { "LightMode" = "testLightMode"}
            Name "testSRP"

            // Blend One Zero
            // ZTest Always
            // ZWrite Off
            // Cull Off


            HLSLPROGRAM
            // #pragma exclude_renderers gles d3d11_9x
            // #pragma enable_d3d11_debug_symbols
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            float4x4 _InvProjectionMatrix;

            struct inVertex {
                float4 positionWS           : POSITION;  // Position World Space
                float3 textureCoords        : TEXCOORD1;    // Texture Coords
            };

            struct vertex2fragment {
                float4 positionCS           : SV_POSITION;  // Position World Space
                float3 textureCoords        : TEXCOORD1;    // Normal World Space
                float3 worldDirection       : TEXCOORD2;    // Normal World Space
            };


            vertex2fragment Vertex(inVertex inData)
            {
                // vertex2fragment output = (vertex2fragment)0;

                // output.positionCS = UnityObjectToClipPos(inData.positionWS.xyz);
                // output.worldDirection = inData.positionWS.xyz - _WorldSpaceCameraPos;

                // output.textureCoords = inData.textureCoords;

                // return output;


                vertex2fragment o = (vertex2fragment)0;
                o.positionCS = inData.positionWS;
                o.textureCoords = inData.textureCoords;
                float2 uv = o.textureCoords * 2.0f - 1.0f;
                o.worldDirection = mul (
                    _InvProjectionMatrix,
                    float4 (
                            uv.x, uv.y,
                            1.0f,
                            1.0f
                        )
                    );
                return o;
            }

            float4 Fragment(vertex2fragment v2fInput) : SV_Target
            {

                // return float4(v2fInput.textureCoords, 0);
                return float4(0,1,1, 0);
            }
            ENDHLSL
        }
    }
}