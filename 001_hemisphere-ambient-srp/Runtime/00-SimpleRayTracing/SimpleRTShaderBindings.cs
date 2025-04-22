namespace UnityEngine.Rendering.SimpleRTRenderPipeline
{
    public static class SimpleRTShaderPassTag
    {
        public static ShaderTagId gbuffer = new ShaderTagId("gbuffer");
        public static ShaderTagId SimpleRTLit = new ShaderTagId("SimpleRTLit");
        public static ShaderTagId ShadowCaster = new ShaderTagId("ShadowCaster");
    }

    public static class SimpleRTShaderBindings
    {
        const string kPerFrameShaderVariablesTag = "SetPerFrameShaderVariables";
        const string kPerFrameCameraVariablesTag = "SetPerFrameCameraVariables";

        public static readonly int debugFloatId = Shader.PropertyToID("_debugFloat");
        public static readonly int debugFloat2Id = Shader.PropertyToID("_debugFloat2");
        public static readonly int debugFloat3Id = Shader.PropertyToID("_debugFloat3");
        public static float _debugFloat;
        public static Vector2 _debugFloat2;
        public static Vector3 _debugFloat3;


        public static void SetPerFrameShaderVariables(ScriptableRenderContext context)
        {
            CommandBuffer cmd = CommandBufferPool.Get(kPerFrameShaderVariablesTag);

            cmd.SetGlobalFloat(debugFloatId, _debugFloat);
            cmd.SetGlobalVector(debugFloat2Id, _debugFloat2);
            cmd.SetGlobalVector(debugFloat3Id, _debugFloat3);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public static void SetPerCameraShaderVariables(ScriptableRenderContext context, Camera camera)
        {
            CommandBuffer cmd = CommandBufferPool.Get(kPerFrameCameraVariablesTag);
            // cmd.SetGlobalMatrix(invVP, (camera.projectionMatrix * camera.worldToCameraMatrix).inverse);
            // cmd.SetGlobalMatrix(viewToWorld, camera.cameraToWorldMatrix);
            // Matrix4x4 pMatrix = GL.GetGPUProjectionMatrix();
            // cmd.SetGlobalVector(dirLightColorId, light.color.linear);
            // cmd.SetGlobalVector(dirLightDirectionId, -light.transform.forward);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}

///