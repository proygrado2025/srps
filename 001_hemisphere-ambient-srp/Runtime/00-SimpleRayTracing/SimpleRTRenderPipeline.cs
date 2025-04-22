using Unity.Collections;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using RenderingUtils;

namespace UnityEngine.Rendering.SimpleRTRenderPipeline {
    public sealed class SimpleRTRenderPipeline : RenderPipeline {
        private RayTracingAccelerationStructure raytracingAccelerationStructure = null;
        private RayTracingShader RTShader;

        private int cameraWidth = 0;
        private int cameraHeight = 0;

        //TODO: asegurarse que funcione con CommandBuffer en la memoria de GPU
        private RenderTexture rayTracingOutput = null;
        private Texture CubeMapTexture = null;

        private static ShaderTagId defaultShaderTagId = new ShaderTagId("SceneViewLightMode");

        // private Material SimpleRTLightingMat;
        public SimpleRTRenderPipeline(SimpleRTRenderPipelineAsset asset) {
            // SimpleRTLightingMat = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/SimpleRTLighting"));

            SimpleRTShaderBindings._debugFloat = asset.debugFloat;
            SimpleRTShaderBindings._debugFloat2 = asset.debugFloat2;
            SimpleRTShaderBindings._debugFloat3 = asset.debugFloat3;

            RTShader = asset.RTShader;
            if(asset.CubeMapTexture != null) {
                CubeMapTexture = asset.CubeMapTexture;
            }
            else {
                CubeMapTexture = new Cubemap(1, TextureFormat.RGBA32, false);
            }
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras) {
            BeginFrameRendering(context, cameras);

            SimpleRTShaderBindings.SetPerFrameShaderVariables(context);

            foreach(Camera camera in cameras) {

                context.SetupCameraProperties(camera);
                SimpleRTShaderBindings.SetPerCameraShaderVariables(context, camera);
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
            CreateResources(camera);

            RenderRT(context, camera);

            // // Submit commands to GPU. Up to this point all commands have been enqueued in the context.
            // // Several submits can be done in a frame to better controls CPU/GPU workload.
            // context.Submit();
        }

        private void CreateResources( Camera camera)
        {
            BuildRaytracingAccelerationStructure();

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
                if (rayTracingOutput){
                    rayTracingOutput.Release();
                }

                rayTracingOutput = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
                rayTracingOutput.enableRandomWrite = true;
                rayTracingOutput.Create();
                
                cameraWidth = width;
                cameraHeight = height;
            }
        }
        void RenderRT(ScriptableRenderContext context, Camera camera) {
            if(!SystemInfo.supportsRayTracing || !RTShader) {
                Debug.Log("The RayTracing API is not supported by this GPU or by the current graphics API.");
                // Graphics.Blit(src, dest);
                return;
            }

            //TODO: usar después como una mejora
            // CommandBuffer cmd = CommandBufferPool.Get("Render Simple RT Render Pipeline");

            // cmd.SetRayTracingAccelerationStructure(RTShader, Shader.PropertyToID("g_SceneAccelStruct"), raytracingAccelerationStructure);
            // cmd.DispatchRays(RTShader, "MyRaygenShader", (uint)width, (uint)height, 1U,camera);
            // context.ExecuteCommandBuffer(cmd);
            // CommandBufferPool.Release(cmd);

            // global shader texture 
            // TODO: pensar en moverlo para otro que tenga sentido (se tendría que ejecutar sólo una vez por más que hayan muchos shaders y cámaras)
            Shader.SetGlobalTexture(Shader.PropertyToID("g_CubeMap"), CubeMapTexture);

            // Input
            RTShader.SetAccelerationStructure(Shader.PropertyToID("g_SceneAccelStruct"), raytracingAccelerationStructure);
            RTShader.SetMatrix(Shader.PropertyToID("g_InvViewMatrix"), camera.cameraToWorldMatrix);
            RTShader.SetFloat(Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * camera.fieldOfView * 0.5f));
            RTShader.SetShaderPass("Test");

            // Output
            // TODO: fijarse si podemos usar el render texture de la cámara (si aplica)
            RTShader.SetTexture("g_Output", rayTracingOutput);

            RTShader.Dispatch("MainRayGenShader", cameraWidth, cameraHeight, 1, camera);


            Graphics.Blit(rayTracingOutput, null as RenderTexture); // null means draw to screen
        }

        private void BuildRaytracingAccelerationStructure() {
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

                raytracingAccelerationStructure.Build();
            }
        }

        private void ReleaseResources() {
            if(raytracingAccelerationStructure != null) {
                raytracingAccelerationStructure.Release();
                raytracingAccelerationStructure = null;
            }
        }
    }

}
