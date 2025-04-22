//https://github.com/Unity-Technologies/Graphics/blob/master/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl#L205
//https://github.com/fakestuff/SRPSubpassTestProject/blob/master/Packages/src/SRPRenderPass/ShaderLibrary/FrameBufferFetchUtl.hlsl

#ifndef FRAME_BUFFER_FETCH_UTILS
    #define FRAME_BUFFER_FETCH_UTILS
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #if defined(SHADER_API_VULKAN) || (defined(SHADER_API_METAL) && defined(UNITY_FRAMEBUFFER_FETCH_AVAILABLE))
        #define UNITY_DECLARE_FRAMEBUFFER_INPUT_FLOAT(idx) cbuffer hlslcc_SubpassInput_f_##idx { float4 hlslcc_fbinput_##idx; }
        #define UNITY_DECLARE_FRAMEBUFFER_INPUT_HALF(idx) cbuffer hlslcc_SubpassInput_h_##idx { half4 hlslcc_fbinput_##idx; }
        #define UNITY_READ_FRAMEBUFFER_INPUT(idx, v2fname) hlslcc_fbinput_##idx
    #else
        #define UNITY_DECLARE_FRAMEBUFFER_INPUT_FLOAT(idx) TEXTURE2D_FLOAT(_UnityFBInput##idx); float4 _UnityFBInput##idx##_TexelSize
        #define UNITY_DECLARE_FRAMEBUFFER_INPUT_HALF(idx) TEXTURE2D_HALF(_UnityFBInput##idx); float4 _UnityFBInput##idx##_TexelSize
        #define UNITY_READ_FRAMEBUFFER_INPUT(idx, v2fvertexname) _UnityFBInput##idx.Load(uint3(v2fvertexname.xy, 0))
    #endif
#endif