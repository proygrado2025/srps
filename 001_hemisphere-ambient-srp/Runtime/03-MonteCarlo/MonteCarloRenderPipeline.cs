using Unity.Collections;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using RenderingUtils;
using System.IO;
using System.Text;


namespace UnityEngine.Rendering.MonteCarloRenderPipeline {
    public sealed class MonteCarloRenderPipeline : RenderPipeline {
        private RayTracingAccelerationStructure raytracingAccelerationStructure = null;
        private RayTracingShader RTShader;
        private ComputeShader preprocessCS;
        private ComputeShader regressionCS;
        private ComputeShader postprocessCS;

        // private bool UseDenoiserPreprocess;
        // private bool UseDenoiserRegression;
        // private bool UseDenoiserPostprocess;

        private int cameraWidth = 0;
        private int cameraHeight = 0;

        //TODO: asegurarse que funcione con CommandBuffer en la memoria de GPU

        private RenderTexture rtWorldPositionCur = null; // W -> RT
        private RenderTexture rtWorldPositionPrev = null; // frame anterior

        private RenderTexture rtWorldNormalCur = null; // W -> RT
        private RenderTexture rtWorldNormalPrev = null; // frame anterior

        private RenderTexture rtNoisyColorCur = null;   // W ->RT y RW -> preprocess.ps.hlsl
        private RenderTexture rtNoisyColorPrev = null;  // frame anterior
        private RenderTexture rtAlbedoCur = null;       // W ->RT y R -> regression

        private RenderTexture rtAcceptPixelBools = null;  // W -> preprocess.ps.hlsl
        private RenderTexture rtInvalidatePrevFramePixels = null;  // W -> preprocess.ps.hlsl
        private RenderTexture rtPixelPosPrev = null;      // W -> preprocess.ps.hlsl
        private RenderTexture csTmpData = null;             // W ->regression
        private RenderTexture csOutData = null;             // W ->regression
        private RenderTexture csAccumulatedFrameCur = null;  // W ->postprocess
        private RenderTexture csAccumulatedFramePrev = null;  // frame anterior

        private Matrix4x4 viewProjMatrixPrev = Matrix4x4.identity;      // last projection

        private Texture CubeMapTexture = null;

        private static ShaderTagId defaultShaderTagId = new ShaderTagId("SceneViewLightMode");

        // private Material SimpleRTLightingMat;
        public MonteCarloRenderPipeline(MonteCarloRenderPipelineAsset asset) {
            // SimpleRTLightingMat = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/SimpleRTLighting"));

            MonteCarloShaderBindings._debugInt = asset.debugInt;
            MonteCarloShaderBindings._debugFloat = asset.debugFloat;
            MonteCarloShaderBindings._debugFloat2 = asset.debugFloat2;
            MonteCarloShaderBindings._debugFloat3 = asset.debugFloat3;

            MonteCarloShaderBindings.samplesPerPixel = asset.spp;

            MonteCarloShaderBindings.maxRecursionDepth = asset.maxRecursionDepth;

            RTShader = asset.RTShader;
            preprocessCS = asset.preprocessCS;
            regressionCS = asset.regressionCS;
            postprocessCS = asset.postprocessCS;

            MonteCarloShaderBindings.UseDenoiserPreprocess = asset.UseDenoiserPreprocess;
            MonteCarloShaderBindings.UseDenoiserRegression = asset.UseDenoiserRegression;
            MonteCarloShaderBindings.UseDenoiserPostprocess = asset.UseDenoiserPostprocess;


            if(asset.CubeMapTexture != null) {
                CubeMapTexture = asset.CubeMapTexture;
            }
            else {
                CubeMapTexture = new Cubemap(1, TextureFormat.RGBA32, false);
            }

            cameraWidth = Camera.main.pixelWidth;
            cameraHeight = Camera.main.pixelHeight;

            CheckOrCreateTextures();
        }

        void CheckOrCreateTextures(){
            CheckOrCreate(ref texture2save, TextureFormat.RGB24);
            CheckOrCreate(ref texFloat, TextureFormat.RGBAFloat);
            CheckOrCreate(ref rtWorldPositionCur, RenderTextureFormat.ARGBFloat);
            CheckOrCreate(ref rtWorldPositionPrev, RenderTextureFormat.ARGBFloat);
            CheckOrCreate(ref rtWorldNormalCur, RenderTextureFormat.ARGBFloat);
            CheckOrCreate(ref rtWorldNormalPrev, RenderTextureFormat.ARGBFloat);
            CheckOrCreate(ref rtNoisyColorCur, RenderTextureFormat.ARGBFloat);
            CheckOrCreate(ref rtNoisyColorPrev, RenderTextureFormat.ARGBFloat);
            CheckOrCreate(ref rtAlbedoCur, RenderTextureFormat.ARGBFloat);
            CheckOrCreate(ref rtAcceptPixelBools, RenderTextureFormat.RInt); // otras opciones: R8 R16
            CheckOrCreate(ref rtInvalidatePrevFramePixels, RenderTextureFormat.RInt); // otras opciones: R8 R16
            CheckOrCreate(ref rtPixelPosPrev, RenderTextureFormat.RGFloat);
            CheckOrCreate(ref csAccumulatedFrameCur, RenderTextureFormat.ARGBFloat);
            CheckOrCreate(ref csAccumulatedFramePrev, RenderTextureFormat.ARGBFloat);

            int w = Mathf.CeilToInt(cameraWidth / 32.0f)+1; // 32 is block edge length, +1 is because BLOCK_OFFSETS
            int h = Mathf.CeilToInt(cameraHeight / 32.0f)+1;
            // w /= 2;

            // 13 es la cantidad de features
            CheckOrCreate(ref csTmpData, RenderTextureFormat.RFloat , 1024, w * h * 13);
            CheckOrCreate(ref csOutData, RenderTextureFormat.RFloat , 1024, w * h * 13);
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras) {
            //https://docs.unity3d.com/ScriptReference/Rendering.RenderPipeline.BeginFrameRendering.html
            BeginFrameRendering(context, cameras);

            MonteCarloShaderBindings.SetPerFrameShaderVariables(context);

            foreach(Camera camera in cameras) {

                context.SetupCameraProperties(camera);
                MonteCarloShaderBindings.SetPerCameraShaderVariables(context, camera);
                BeginCameraRendering(context, camera);
#if UNITY_EDITOR
                if (camera.cameraType == CameraType.SceneView)
                {
                    ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
                }
#endif
                if( camera == Camera.main ){
                    DrawCamera(context, camera);
                }
                else{
                    SRPUtils.DefaultDrawCamera(context, camera, defaultShaderTagId);
                }

                EndCameraRendering(context, camera);
            }
            EndFrameRendering(context, cameras);
        }

        void DrawCamera(ScriptableRenderContext context, Camera camera) {
            CreateResources(context, camera);

            RenderRT(context, camera);

            // // Submit commands to GPU. Up to this point all commands have been enqueued in the context.
            // // Several submits can be done in a frame to better controls CPU/GPU workload.
            // context.Submit();
        }


        // private void CheckOrCreate(ref ComputeBuffer buffer, int size){
        //     if( buffer != null){
        //         buffer.Release();
        //     }

        //     buffer = new ComputeBuffer(size, sizeof(float));
        // }
        private void CheckOrCreate(ref RenderTexture texture, RenderTextureFormat format) {
            CheckOrCreate(ref texture, format, cameraWidth,cameraHeight);
        }


        private void CheckOrCreate(ref RenderTexture texture, RenderTextureFormat format, int width, int height) {
            if( (texture != null) && texture.IsCreated()) {
                texture.Release();
            }

            texture = new RenderTexture(width, height, 0, format);
            texture.useMipMap = false;
            texture.enableRandomWrite = true;
            texture.Create();
        }

        private void CreateResources( ScriptableRenderContext context, Camera camera)
        {
            BuildRaytracingAccelerationStructure(context);

            // Configuración inicial para crear los tamaños de los buffers
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            RenderTargetIdentifier target = BuiltinRenderTextureType.CameraTarget;
            int aa = 1;

#if UNITY_EDITOR
            // When in UNITY EDITOR, the camera may have a render target texture attached to it, in this case use the size in pixel of that texture
            if (camera.targetTexture)
            {
                width = camera.targetTexture.width;
                height = camera.targetTexture.height;
                target = camera.targetTexture;
                aa = camera.targetTexture.antiAliasing;
            }
#endif

            if (cameraWidth != width || cameraHeight != height)
            {
                // update cameraWidth and cameraHeight first!
                cameraWidth = width;
                cameraHeight = height;

                CheckOrCreateTextures();
            }
        }
        void RenderRT(ScriptableRenderContext context, Camera camera) {
            if(!SystemInfo.supportsRayTracing || !RTShader) {
                Debug.Log("The RayTracing API is not supported by this GPU or by the current graphics API.");
                // Graphics.Blit(src, dest);
                return;
            }

            // global shader texture
            // TODO: pensar en moverlo para otro que tenga sentido (se tendría que ejecutar sólo una vez por más que hayan muchos shaders y cámaras)
            Shader.SetGlobalTexture(Shader.PropertyToID("g_CubeMap"), CubeMapTexture);

            Vector3 rotatedVector = RenderSettings.sun.transform.rotation * Vector3.forward;
            Shader.SetGlobalVector(Shader.PropertyToID("g_sun_direction"), rotatedVector);

            Shader.SetGlobalFloat(Shader.PropertyToID("g_sun_intensity"), RenderSettings.sun.intensity);

            RenderTexture srcTexture2BlitScreen = null;

#region RayTracing
            CommandBuffer cmd = CommandBufferPool.Get("Render Simple RT Render Pipeline");
            cmd.SetRayTracingAccelerationStructure(RTShader, Shader.PropertyToID("g_SceneAccelStruct"), raytracingAccelerationStructure);
            cmd.SetGlobalMatrix(Shader.PropertyToID("g_InvViewMatrix"), camera.cameraToWorldMatrix);
            cmd.SetGlobalFloat(Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * camera.fieldOfView * 0.5f));
            cmd.SetRayTracingShaderPass(RTShader, "Test");

            cmd.SetRayTracingTextureParam(RTShader, Shader.PropertyToID("g_OutputWorldPos"), rtWorldPositionCur);
            cmd.SetRayTracingTextureParam(RTShader, Shader.PropertyToID("g_OutputAlbedo"), rtAlbedoCur);
            cmd.SetRayTracingTextureParam(RTShader, Shader.PropertyToID("g_OutputNormal"), rtWorldNormalCur);
            cmd.SetRayTracingTextureParam(RTShader, Shader.PropertyToID("g_OutputColor"), rtNoisyColorCur);
            cmd.SetRayTracingTextureParam(RTShader, Shader.PropertyToID("g_OutputInvalidatePrevFrame"), rtInvalidatePrevFramePixels);

            cmd.DispatchRays(RTShader, "MainRayGenShader",(uint)cameraWidth, (uint)cameraHeight, 1U,camera);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            srcTexture2BlitScreen = rtNoisyColorCur;
#endregion


#region DenoiserPreprocess
            if(MonteCarloShaderBindings.UseDenoiserPreprocess){
                cmd = CommandBufferPool.Get("Denoiser Preprocess");
                int kernelFirstPass = preprocessCS.FindKernel("preprocess");

                cmd.SetGlobalInt(Shader.PropertyToID("IMAGE_WIDTH"), cameraWidth);
                cmd.SetGlobalInt(Shader.PropertyToID("IMAGE_HEIGHT"), cameraHeight);

                cmd.SetComputeMatrixParam(preprocessCS, Shader.PropertyToID("_prevViewProjMat"), viewProjMatrixPrev);
                SetupPreprocessComputeShaderTextures(cmd, kernelFirstPass);
                Vector2Int threadGroups = new Vector2Int(Mathf.CeilToInt(cameraWidth / 8.0f), Mathf.CeilToInt(cameraHeight / 8.0f));

                cmd.DispatchCompute(preprocessCS, kernelFirstPass, threadGroups.x, threadGroups.y, 1);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);

                srcTexture2BlitScreen = rtNoisyColorCur;
            }
#endregion

#region DenoiserRegression
            if( MonteCarloShaderBindings.UseDenoiserRegression){
                cmd = CommandBufferPool.Get("Denoiser Regression");
                int kernelSecondPass = regressionCS.FindKernel("regression");

                SetupRegressionComputeShaderTextures(cmd, kernelSecondPass);

                // threadGroups = new Vector2Int(Mathf.CeilToInt(cameraWidth / 32.0f), Mathf.CeilToInt(cameraHeight / 32.0f));
                //calcula la cantidad bloques que hay en la pantalla
                int w = Mathf.CeilToInt(cameraWidth / 32.0f)+1;  // 32 is block edge length, +1 is because BLOCK_OFFSETS
                int h = Mathf.CeilToInt(cameraHeight / 32.0f)+1; // 32 is block edge length, +1 is because BLOCK_OFFSETS

                cmd.DispatchCompute(regressionCS, kernelSecondPass, w*h,1,1);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);

                srcTexture2BlitScreen = rtNoisyColorCur;
            }
#endregion

#region DenoiserPostprocess
            if( MonteCarloShaderBindings.UseDenoiserPostprocess){
                cmd = CommandBufferPool.Get("Denoiser Psotprocess");
                int kernelThirdPass = postprocessCS.FindKernel("postprocess");

                SetupPostProcessComputeShaderTextures(cmd, kernelThirdPass);

                Vector2Int threadGroups = new Vector2Int(Mathf.CeilToInt(cameraWidth / 8.0f), Mathf.CeilToInt(cameraHeight / 8.0f));
                cmd.DispatchCompute(postprocessCS, kernelThirdPass, threadGroups.x, threadGroups.y, 1);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);

                srcTexture2BlitScreen = csAccumulatedFrameCur;
            }
#endregion

#region BlitToCamera
            cmd = CommandBufferPool.Get("Blit to camera");
            cmd.Blit(srcTexture2BlitScreen, null as RenderTexture); // null means draw to screen
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            saveAsPNG(srcTexture2BlitScreen, camera, "color");
            // saveAsPNG(rtAlbedoCur, camera, "albedo");
            // saveAsPNG(rtWorldNormalCur, camera, "normal");
            // saveAsPNG(rtWorldPositionCur, camera, "worldpos");
            // SaveRenderTextureToPFM(srcTexture2BlitScreen, camera, "color");
            // SaveRenderTextureToPFM(rtAlbedoCur, camera, "albedo");
            // SaveRenderTextureToPFM(rtWorldNormalCur, camera, "normal");
            // SaveRenderTextureToPFM(rtWorldPositionCur, camera, "worldpos");
            updateFileIndex();
#endregion

#region UpdatePrevFrameData
            SwapAllCurAndPrev();
            Matrix4x4 viewMat = camera.worldToCameraMatrix;
            viewProjMatrixPrev = camera.projectionMatrix * viewMat;
#endregion
            context.Submit();
        }

        void SwapCurAndPrev(ref RenderTexture curRT, ref RenderTexture prevRT){
            RenderTexture tmpRT = null;

            tmpRT = curRT;
            curRT = prevRT;
            prevRT = tmpRT;
        }

        void SwapAllCurAndPrev(){
            SwapCurAndPrev(ref rtNoisyColorCur, ref rtNoisyColorPrev);
            SwapCurAndPrev(ref rtWorldPositionCur, ref rtWorldPositionPrev);
            SwapCurAndPrev(ref rtWorldNormalCur, ref rtWorldNormalPrev);
            SwapCurAndPrev(ref csAccumulatedFrameCur, ref csAccumulatedFramePrev);
        }
        void SetupPreprocessComputeShaderTextures(CommandBuffer cmd, int kernelId){
            cmd.SetComputeTextureParam(
                preprocessCS,
                kernelId,
                Shader.PropertyToID("gCurPos"), rtWorldPositionCur);
            cmd.SetComputeTextureParam(
                preprocessCS,
                kernelId,
                Shader.PropertyToID("gPrevPos"), rtWorldPositionPrev);
            cmd.SetComputeTextureParam(
                preprocessCS,
                kernelId,
                Shader.PropertyToID("gCurNorm"), rtWorldNormalCur);
            cmd.SetComputeTextureParam(
                preprocessCS,
                kernelId,
                Shader.PropertyToID("gPrevNorm"), rtWorldNormalPrev);
            cmd.SetComputeTextureParam(
                preprocessCS,
                kernelId,
                Shader.PropertyToID("gCurNoisy"), rtNoisyColorCur);
            cmd.SetComputeTextureParam(
                preprocessCS,
                kernelId,
                Shader.PropertyToID("gPrevNoisy"), rtNoisyColorPrev);
            cmd.SetComputeTextureParam(
                preprocessCS,
                kernelId,
                Shader.PropertyToID("accept_bools"), rtAcceptPixelBools);
            cmd.SetComputeTextureParam(
                preprocessCS,
                kernelId,
                Shader.PropertyToID("out_prev_frame_pixel"), rtPixelPosPrev);
            cmd.SetComputeTextureParam(
                preprocessCS,
                kernelId,
                Shader.PropertyToID("gInvalidatePrevFrame"), rtInvalidatePrevFramePixels);
        }
        void SetupRegressionComputeShaderTextures(CommandBuffer cmd, int kernelId){
            cmd.SetComputeTextureParam(
                regressionCS,
                kernelId,
                Shader.PropertyToID("gCurPos"), rtWorldPositionCur);
            cmd.SetComputeTextureParam(
                regressionCS,
                kernelId,
                Shader.PropertyToID("gCurNorm"), rtWorldNormalCur);
            cmd.SetComputeTextureParam(
                regressionCS,
                kernelId,
                Shader.PropertyToID("albedo"), rtAlbedoCur);
            cmd.SetComputeTextureParam(
                regressionCS,
                kernelId,
                Shader.PropertyToID("gCurNoisy"), rtNoisyColorCur);
            cmd.SetComputeTextureParam(
                regressionCS,
                kernelId,
                Shader.PropertyToID("tmp_data"), csTmpData);
            cmd.SetComputeTextureParam(
                regressionCS,
                kernelId,
                Shader.PropertyToID("out_data"), csOutData);
            cmd.SetComputeTextureParam(
                regressionCS,
                kernelId,
                Shader.PropertyToID("gInvalidatePrevFrame"), rtInvalidatePrevFramePixels);
        }

        void SetupPostProcessComputeShaderTextures(CommandBuffer cmd, int kernelId){
            cmd.SetComputeTextureParam(
                postprocessCS,
                kernelId,
                Shader.PropertyToID("filtered_frame"), rtNoisyColorCur);
            cmd.SetComputeTextureParam(
                postprocessCS,
                kernelId,
                Shader.PropertyToID("accumulated_prev_frame"), csAccumulatedFramePrev);
            cmd.SetComputeTextureParam(
                postprocessCS,
                kernelId,
                Shader.PropertyToID("albedo"), rtAlbedoCur);
            cmd.SetComputeTextureParam(
                postprocessCS,
                kernelId,
                Shader.PropertyToID("accept_bools"), rtAcceptPixelBools);
            cmd.SetComputeTextureParam(
                postprocessCS,
                kernelId,
                Shader.PropertyToID("in_prev_frame_pixel"), rtPixelPosPrev);
            cmd.SetComputeTextureParam(
                postprocessCS,
                kernelId,
                Shader.PropertyToID("accumulated_frame"), csAccumulatedFrameCur);
        }

        private void BuildRaytracingAccelerationStructure(ScriptableRenderContext context) {
        	// TODO: revisar por qué no se actualiza la estructura en modo ManagementMode.Automatic
            if(raytracingAccelerationStructure != null ){
                raytracingAccelerationStructure.Release();
                raytracingAccelerationStructure = null;
            }

            if(raytracingAccelerationStructure == null) {
                RayTracingAccelerationStructure.RASSettings settings = new RayTracingAccelerationStructure.RASSettings();
                settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;
                settings.managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic;
                settings.layerMask = 255;

                raytracingAccelerationStructure = new RayTracingAccelerationStructure(settings);

                CommandBuffer cmd = CommandBufferPool.Get("Build Ray Tracing Acceleration Structure");

                cmd.BuildRayTracingAccelerationStructure(raytracingAccelerationStructure);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);

            }
        }

        private void ReleaseResources() {
            if(raytracingAccelerationStructure != null) {
                raytracingAccelerationStructure.Release();
                raytracingAccelerationStructure = null;
            }
        }

        void OnDestroy()
        {
            if( csTmpData != null ){
                csTmpData.Release();
            }

            if( csOutData != null ){
                csOutData.Release();
            }
        }

///////////////////////////////////////////////////////////////////
        private void CheckOrCreate(ref Texture2D texture, TextureFormat format){
            CheckOrCreate(ref texture, format, cameraWidth,cameraHeight);
        }
        private void CheckOrCreate(ref Texture2D texture, TextureFormat format, int width, int height){
            texture = new Texture2D(width, height, format, -1, false);
        }

        Texture2D texture2save;
        void saveAsPNG(RenderTexture rt, Camera camera, string suffix){
            if( !Application.isPlaying || !MonteCarloShaderBindings.isRecording ){
                return;
            }


            Transform camT = camera.transform;
            string dirName = Application.dataPath + $"/../output/d"+
                $"{MonteCarloShaderBindings.maxRecursionDepth}__"+
                $"p{camT.position.x.ToString("F2")}_"+
                $"{camT.position.y.ToString("F2")}_"+
                $"{camT.position.z.ToString("F2")}__"+
                $"r{camT.eulerAngles.x.ToString("F2")}_"+
                $"{camT.eulerAngles.y.ToString("F2")}_"+
                $"{camT.eulerAngles.z.ToString("F2")}__"+
                $"d{(MonteCarloShaderBindings.UseDenoiserPreprocess?1:0)}"+
                $"{(MonteCarloShaderBindings.UseDenoiserRegression?1:0)}"+
                $"{(MonteCarloShaderBindings.UseDenoiserPostprocess?1:0)}";


            if( !System.IO.Directory.Exists(dirName)){
                System.IO.Directory.CreateDirectory(dirName);
            }

            // Texture2D texture2save = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            texture2save.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture2save.Apply();
            RenderTexture.active = null;
            byte[] bytes = texture2save.EncodeToPNG();

            string filePath = $"{dirName}/{MonteCarloShaderBindings.fileIndex.ToString("00000")}{suffix}.png";
            System.IO.File.WriteAllBytes(filePath, bytes);

            // if( ++MonteCarloShaderBindings.fileIndex >= 100000){
            //     // MonteCarloShaderBindings.fileIndex = 0;

            //     // if( ++MonteCarloShaderBindings.maxRecursionDepth > 5){
            //         Application.Quit();
            //     // }
            // }

        }

        void updateFileIndex(){
            if( !Application.isPlaying || !MonteCarloShaderBindings.isRecording ){
                return;
            }

            if( ++MonteCarloShaderBindings.fileIndex >= 1600){
                // MonteCarloShaderBindings.fileIndex = 0;

                // if( ++MonteCarloShaderBindings.maxRecursionDepth > 5){
                    Application.Quit();
                // }
            }
        }


        Texture2D texFloat = null;
        private void SaveRenderTextureToPFM(RenderTexture rt, Camera camera, string suffix)
        {
            if( !Application.isPlaying || !MonteCarloShaderBindings.isRecording ){
                return;
            }


            Transform camT = camera.transform;
            string dirName = Application.dataPath + $"/../output/d"+
                $"{MonteCarloShaderBindings.maxRecursionDepth}__"+
                $"p{camT.position.x.ToString("F2")}_"+
                $"{camT.position.y.ToString("F2")}_"+
                $"{camT.position.z.ToString("F2")}__"+
                $"r{camT.eulerAngles.x.ToString("F2")}_"+
                $"{camT.eulerAngles.y.ToString("F2")}_"+
                $"{camT.eulerAngles.z.ToString("F2")}__"+
                $"d{(MonteCarloShaderBindings.UseDenoiserPreprocess?1:0)}"+
                $"{(MonteCarloShaderBindings.UseDenoiserRegression?1:0)}"+
                $"{(MonteCarloShaderBindings.UseDenoiserPostprocess?1:0)}";

            string filePath = $"{dirName}/{MonteCarloShaderBindings.fileIndex.ToString("00000")}{suffix}.pfm";

            if( !System.IO.Directory.Exists(dirName)){
                System.IO.Directory.CreateDirectory(dirName);
            }

            int width = rt.width;
            int height = rt.height;

            // if( (texFloat == null) || (texFloat.width != width) || (texFloat.height != height) ){
            //     texFloat = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
            // }
            RenderTexture.active = rt;
            texFloat.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texFloat.Apply();
            RenderTexture.active = null;


            // Create a binary writer to save the PFM file
            using (BinaryWriter writer = new BinaryWriter(File.Open(filePath, FileMode.Create)))
            {
                // // Write the PFM header
                // writer.Write(Encoding.ASCII.GetBytes("PF\n"));
                // writer.Write(width);
                // writer.Write(' ');
                // writer.Write(height);
                // writer.Write('\n');
                // writer.Write(Encoding.ASCII.GetBytes("-1.000000\n")); // Scale factor for little-endian

                // // Write the pixel data
                // Color[] pixels = tex.GetPixels();
                // foreach (Color pixel in pixels)
                // {
                //     writer.Write(pixel.r);
                //     writer.Write(pixel.g);
                //     writer.Write(pixel.b);
                // }
                string str = $"PF\n{(int)width} {(int)height}\n-1.0\n";

                byte[] bytes = Encoding.ASCII.GetBytes(str);

                writer.Write(bytes);

                Color[] pixels = texFloat.GetPixels();
                foreach (Color pixel in pixels)
                {
                    writer.Write((float)pixel.r);
                    writer.Write((float)pixel.g);
                    writer.Write((float)pixel.b);
                }
                // for (int i = 0; i < texture.Length; i++)
                // {
                //     writer.Write((float)texture[i].R);
                //     writer.Write((float)texture[i].G);
                //     writer.Write((float)texture[i].B);
                // }
            }
        }
    }

}
