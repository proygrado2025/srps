namespace UnityEngine.Rendering.DeferredRenderPipeline
{
    public static class DeferredShaderPassTag
    {
        public static ShaderTagId gbuffer = new ShaderTagId("gbuffer");
        public static ShaderTagId deferredLit = new ShaderTagId("deferredLit");
        public static ShaderTagId ShadowCaster = new ShaderTagId("ShadowCaster");
    }

    public static class DeferredShaderBindings
    {
        const string kPerFrameShaderVariablesTag = "SetPerFrameShaderVariables";
        const string kPerFrameCameraVariablesTag = "SetPerFrameCameraVariables";
        public static readonly int dirLightColorId = Shader.PropertyToID("_DirectionalLightColor");
        public static readonly int dirLightDirectionId = Shader.PropertyToID("_DirectionalLightDirection");
        public static readonly int invVP = Shader.PropertyToID("_invVP");
        public static readonly int viewToWorld = Shader.PropertyToID("_ViewToWorld");
        public static readonly int aoIntensityId = Shader.PropertyToID("_aoIntensity");
        public static readonly int aoRadiusId = Shader.PropertyToID("_aoRadius");
        public static readonly int aoFallOffId = Shader.PropertyToID("_aoFallOff");
        public static readonly int aoBlurCenterWeightId = Shader.PropertyToID("_aoBlurCenterWeight");

        public static readonly int debugFloatId = Shader.PropertyToID("_debugFloat");
        public static readonly int debugFloat2Id = Shader.PropertyToID("_debugFloat2");
        public static readonly int debugFloat3Id = Shader.PropertyToID("_debugFloat3");
        public static float _aoIntensity;
        public static float _aoRadius;
        public static float _aoFallOff;
        public static float _aoBlurCenterWeight;
        public static float _debugFloat;
        public static Vector2 _debugFloat2;
        public static Vector3 _debugFloat3;


        public static void SetPerFrameShaderVariables(ScriptableRenderContext context)
        {
            Light light = RenderSettings.sun;

            CommandBuffer cmd = CommandBufferPool.Get(kPerFrameShaderVariablesTag);
            cmd.SetGlobalVector(dirLightColorId, light.color.linear);
            cmd.SetGlobalVector(dirLightDirectionId, -light.transform.forward);
            cmd.SetGlobalFloat(aoIntensityId, _aoIntensity);
            cmd.SetGlobalFloat(aoRadiusId, _aoRadius);
            cmd.SetGlobalFloat(aoFallOffId, _aoFallOff);
            cmd.SetGlobalFloat(aoBlurCenterWeightId, _aoBlurCenterWeight);


            cmd.SetGlobalFloat(debugFloatId, _debugFloat);
            cmd.SetGlobalVector(debugFloat2Id, _debugFloat2);
            cmd.SetGlobalVector(debugFloat3Id, _debugFloat3);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public static void SetPerCameraShaderVariables(ScriptableRenderContext context, Camera camera)
        {
            CommandBuffer cmd = CommandBufferPool.Get(kPerFrameCameraVariablesTag);
            cmd.SetGlobalMatrix(invVP, (camera.projectionMatrix * camera.worldToCameraMatrix).inverse);
            cmd.SetGlobalMatrix(viewToWorld, camera.cameraToWorldMatrix);
            // Matrix4x4 pMatrix = GL.GetGPUProjectionMatrix();
            // cmd.SetGlobalVector(dirLightColorId, light.color.linear);
            // cmd.SetGlobalVector(dirLightDirectionId, -light.transform.forward);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}

///