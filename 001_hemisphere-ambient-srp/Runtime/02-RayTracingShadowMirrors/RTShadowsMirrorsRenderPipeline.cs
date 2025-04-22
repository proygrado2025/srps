using Unity.Collections;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using RenderingUtils;

namespace UnityEngine.Rendering.RTShadowsMirrorsRenderPipeline {
    public sealed class RTShadowsMirrorsRenderPipeline : RenderPipeline {
        private RayTracingAccelerationStructure raytracingAccelerationStructure = null;
        private RayTracingShader RTShader;

        private int cameraWidth = 0;
        private int cameraHeight = 0;

        //TODO: asegurarse que funcione con CommandBuffer en la memoria de GPU
        private RenderTexture rayTracingOutput = null;
        private Texture CubeMapTexture = null;

        private static ShaderTagId defaultShaderTagId = new ShaderTagId("SceneViewLightMode");

        // private Material SimpleRTLightingMat;
        public RTShadowsMirrorsRenderPipeline(RTShadowsMirrorsRenderPipelineAsset asset) {
            // SimpleRTLightingMat = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/SimpleRTLighting"));

            RTShadowsMirrorsShaderBindings._debugFloat = asset.debugFloat;
            RTShadowsMirrorsShaderBindings._debugFloat2 = asset.debugFloat2;
            RTShadowsMirrorsShaderBindings._debugFloat3 = asset.debugFloat3;

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

            RTShadowsMirrorsShaderBindings.SetPerFrameShaderVariables(context);

            foreach(Camera camera in cameras) {

                context.SetupCameraProperties(camera);
                RTShadowsMirrorsShaderBindings.SetPerCameraShaderVariables(context, camera);
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

            if (cameraWidth != width || cameraHeight != height || !rayTracingOutput)
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

            // global shader texture 
            // TODO: pensar en moverlo para otro que tenga sentido (se tendría que ejecutar sólo una vez por más que hayan muchos shaders y cámaras)
            Shader.SetGlobalTexture(Shader.PropertyToID("g_CubeMap"), CubeMapTexture);

            Vector3 rotatedVector = RenderSettings.sun.transform.rotation * Vector3.forward;
            Shader.SetGlobalVector(Shader.PropertyToID("g_sun_direction"), rotatedVector);

            CommandBuffer cmd = CommandBufferPool.Get("Render Simple RT Render Pipeline");

            cmd.SetGlobalVector(Shader.PropertyToID("_debugFloat2"), RTShadowsMirrorsShaderBindings._debugFloat2);
            cmd.SetGlobalVector(Shader.PropertyToID("_debugFloat3"), RTShadowsMirrorsShaderBindings._debugFloat3);

            cmd.SetRayTracingAccelerationStructure(RTShader, Shader.PropertyToID("g_SceneAccelStruct"), raytracingAccelerationStructure);
            cmd.SetGlobalMatrix(Shader.PropertyToID("g_InvViewMatrix"), camera.cameraToWorldMatrix);
            cmd.SetGlobalFloat(Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * camera.fieldOfView * 0.5f));
            cmd.SetRayTracingShaderPass(RTShader, "Test");
            
            cmd.SetRayTracingTextureParam(RTShader, Shader.PropertyToID("g_Output"), rayTracingOutput);

            cmd.DispatchRays(RTShader, "MainRayGenShader",(uint)cameraWidth, (uint)cameraHeight, 1U,camera);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            // cmd.Blit(rayTracingOutput, null as RenderTexture, Vector2.one, Vector2.zero);

            context.Submit();

            Graphics.Blit(rayTracingOutput, null as RenderTexture); // null means draw to screen
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

    }

}
