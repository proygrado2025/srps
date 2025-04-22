Shader "Custom SRP/Deferred Lighting + AO"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1.0, 1.0, 1.0, 1.0)
    }
    SubShader
    {
        Pass
        {
            // This tag must match ShaderTagId passed in ScriptableRenderContext.DrawRenderers
            Tags { "LightMode" = "gbuffer"}
            HLSLPROGRAM
            #pragma vertex DeferredLightingGBufferPassVertex
			#pragma fragment DeferredLightingGBufferPassFragment
            #include "DeferredLightingGBufferPass.hlsl"
            ENDHLSL
        }
        
        // Pass
        // {
        //     // This tag must match ShaderTagId passed in ScriptableRenderContext.DrawRenderers
        //     Tags { "LightMode" = "deferredLit"}
        //     HLSLPROGRAM
        //     #pragma vertex DeferredLightingGBufferPassVertex
		// 	#pragma fragment DeferredLightingGBufferPassFragment
        //     #include "DeferredLightingGBufferPass.hlsl"
        //     ENDHLSL
        // }


        // WIP: SHADOWS
        // Pass
        // {
        //     Tags {"LightMode"="ShadowCaster"}

        //     CGPROGRAM
        //     #pragma vertex vert
        //     #pragma fragment frag
        //     #pragma multi_compile_shadowcaster
        //     #include "UnityCG.cginc"

        //     struct v2f { 
        //         V2F_SHADOW_CASTER;
        //     };

        //     v2f vert(appdata_base v)
        //     {
        //         v2f o;
        //         TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
        //         return o;
        //     }

        //     float4 frag(v2f i) : SV_Target
        //     {
        //         SHADOW_CASTER_FRAGMENT(i)
        //     }
        //     ENDCG
        // }
    }
}