namespace UnityEngine.Rendering.MonteCarloRenderPipeline
{
    public static class MonteCarloShaderPassTag
    {
        public static ShaderTagId gbuffer = new ShaderTagId("gbuffer");
        public static ShaderTagId SimpleRTLit = new ShaderTagId("SimpleRTLit");
        public static ShaderTagId ShadowCaster = new ShaderTagId("ShadowCaster");
    }

    public static class MonteCarloShaderBindings
    {
        public static int fileIndex = 0;
        public static bool isRecording = false;
        const string kPerFrameShaderVariablesTag = "SetPerFrameShaderVariables";
        const string kPerFrameCameraVariablesTag = "SetPerFrameCameraVariables";

        public static readonly int debugIntId = Shader.PropertyToID("_debugInt");
        public static readonly int debugFloatId = Shader.PropertyToID("_debugFloat");
        public static readonly int debugFloat2Id = Shader.PropertyToID("_debugFloat2");
        public static readonly int debugFloat3Id = Shader.PropertyToID("_debugFloat3");
        public static int _debugInt;
        public static float _debugFloat;
        public static Vector2 _debugFloat2;
        public static Vector3 _debugFloat3;

        public static readonly int frameNumberId = Shader.PropertyToID("frameNumber");
        // se inicializa en cero para no intente acumular el primer frame con el (inexistente) frame anterior
        public static uint frameNumber = 0;
        public static readonly int maxRecursionDepthId = Shader.PropertyToID("g_max_recursion_depth");
        public static uint maxRecursionDepth = 1;

        public static readonly int samplesPerPixelId = Shader.PropertyToID("g_samplesPerPixel");
        public static uint samplesPerPixel = 1;
        // public static readonly int preBlendAlphaId = Shader.PropertyToID("g_preBlendAlpha");
        // public static float preBlendAlpha = 0.2f;

        // this are not exactly "shader" bindings ¯\_(ツ)_/¯
        public static bool UseDenoiserPreprocess = true;
        public static bool UseDenoiserRegression = true;
        public static bool UseDenoiserPostprocess = true;
        public static void SetPerFrameShaderVariables(ScriptableRenderContext context)
        {
            // this "fix" the biased on random number generation when running for too long
            // el +1 es para que no vuelva a ser cero (ver comentario de inicialización
            frameNumber = (frameNumber% 1000) + 1;

            CommandBuffer cmd = CommandBufferPool.Get(kPerFrameShaderVariablesTag);

            cmd.SetGlobalInt(debugIntId, _debugInt);
            cmd.SetGlobalFloat(debugFloatId, _debugFloat);
            cmd.SetGlobalVector(debugFloat2Id, _debugFloat2);
            cmd.SetGlobalVector(debugFloat3Id, _debugFloat3);

            cmd.SetGlobalInt(frameNumberId, (int)frameNumber);

            cmd.SetGlobalInt(maxRecursionDepthId, (int)maxRecursionDepth);
            cmd.SetGlobalInt(samplesPerPixelId, (int)samplesPerPixel);
            // cmd.SetGlobalFloat(preBlendAlphaId, preBlendAlpha);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public static void SetPerCameraShaderVariables(ScriptableRenderContext context, Camera camera)
        {
            CommandBuffer cmd = CommandBufferPool.Get(kPerFrameCameraVariablesTag);
            cmd.SetGlobalMatrix(Shader.PropertyToID("_InvCameraViewProj"), (GL.GetGPUProjectionMatrix(camera.projectionMatrix,true) * camera.worldToCameraMatrix).inverse);
            // cmd.SetGlobalMatrix(viewToWorld, camera.cameraToWorldMatrix);
            // Matrix4x4 pMatrix = GL.GetGPUProjectionMatrix();
            // cmd.SetGlobalVector(dirLightColorId, light.color.linear);
            // cmd.SetGlobalVector(dirLightDirectionId, -light.transform.forward);

            cmd.SetGlobalInt(Shader.PropertyToID("screen_width"), camera.pixelWidth);
            cmd.SetGlobalInt(Shader.PropertyToID("screen_height"), camera.pixelHeight);
            cmd.SetGlobalInt(Shader.PropertyToID("horizontal_blocks_count"), Mathf.CeilToInt(camera.pixelWidth / 32.0f)+1); // el +1 es por el BLOCK_OFFSETS

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}

///