namespace UnityEngine.Rendering.TestRenderPipeline
{
    public static class TestShaderPassTag
    {
        public static ShaderTagId gbuffer = new ShaderTagId("gbuffer");
        public static ShaderTagId testLightMode = new ShaderTagId("testLightMode");
        public static ShaderTagId ShadowCaster = new ShaderTagId("ShadowCaster");
    }

    public static class TestShaderBindings
    {
        const string kPerFrameShaderVariablesTag = "SetPerFrameShaderVariables";
        const string kPerFrameCameraVariablesTag = "SetPerFrameCameraVariables";
        public static int dirLightColorId = Shader.PropertyToID("_DirectionalLightColor");
        public static int dirLightDirectionId = Shader.PropertyToID("_DirectionalLightDirection");
        public static int invProjectionMatrixId = Shader.PropertyToID("_InvProjectionMatrix");


        public static void SetPerFrameShaderVariables(ScriptableRenderContext context)
        {
            Light light = RenderSettings.sun;

            CommandBuffer cmd = CommandBufferPool.Get(kPerFrameShaderVariablesTag);
            cmd.SetGlobalVector(dirLightColorId, light.color.linear);
            cmd.SetGlobalVector(dirLightDirectionId, -light.transform.forward);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public static void SetPerCameraShaderVariables(ScriptableRenderContext context, Camera camera)
        {
            CommandBuffer cmd = CommandBufferPool.Get(kPerFrameCameraVariablesTag);
            cmd.SetGlobalMatrix(invProjectionMatrixId, camera.projectionMatrix.inverse);
            // Matrix4x4 pMatrix = GL.GetGPUProjectionMatrix();
            // cmd.SetGlobalVector(dirLightColorId, light.color.linear);
            // cmd.SetGlobalVector(dirLightDirectionId, -light.transform.forward);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}

///