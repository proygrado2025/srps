Shader "Custom SRP/Hemispheric Ambient"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _AmbientDownColor("AmbientDownColor", Color) = (1.0, 1.0, 1.0, 1.0)
        _AmbientRangeColor("AmbientRangeColor", Color) = (1.0, 1.0, 1.0, 1.0)
    }
    SubShader
    {
        Pass
        {
            // This tag must match ShaderTagId passed in ScriptableRenderContext.DrawRenderers
            Tags { "LightMode" = "ForwardLit"}
            HLSLPROGRAM
            #pragma vertex HemisphericAmbientPassVertex
			#pragma fragment HemisphericAmbientPassFragment
            #include "HemisphericAmbientPass.hlsl"
            ENDHLSL
        }
    }
}