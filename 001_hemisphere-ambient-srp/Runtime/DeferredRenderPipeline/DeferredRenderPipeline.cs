using Unity.Collections;
using System.Collections.Generic;

namespace UnityEngine.Rendering.DeferredRenderPipeline
{
    public sealed class DeferredRenderPipeline : RenderPipeline
    {
        static Mesh s_FullscreenMesh = null;
        static Mesh s_FullscreenCameraMesh = null;
        static Vector3[] s_FullscreenVertices = null;

        public static Mesh fullscreenMesh
        {
            get
            {
                if (s_FullscreenMesh != null)
                    return s_FullscreenMesh;

                float topV = 1.0f;
                float bottomV = 0.0f;

                Mesh mesh = new Mesh { name = "Fullscreen Clipping Space Quad" };
                mesh.SetVertices(new List<Vector3>
                {
                    new Vector3(-1.0f, -1.0f, 0.0f),
                    new Vector3(-1.0f,  1.0f, 0.0f),
                    new Vector3(1.0f, -1.0f, 0.0f),
                    new Vector3(1.0f,  1.0f, 0.0f)
                });

                mesh.SetUVs(0, new List<Vector2>
                {
                    new Vector2(0.0f, bottomV),
                    new Vector2(0.0f, topV),
                    new Vector2(1.0f, bottomV),
                    new Vector2(1.0f, topV)
                });

                mesh.SetIndices(new[] { 0, 1, 2, 2, 1, 3 }, MeshTopology.Triangles, 0, false);
                mesh.UploadMeshData(true);
                return mesh;
            }
        }

        private Material deferredLightingMat;
        public DeferredRenderPipeline(DeferredRenderPipelineAsset asset)
        {
            deferredLightingMat = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/DeferredLighting"));
            DeferredShaderBindings._aoIntensity = asset.AmbientOcclusionIntensity;
            DeferredShaderBindings._aoRadius = asset.AmbientOcclusionRadius;
            DeferredShaderBindings._aoFallOff = asset.AmbientOcclusionFallOff;
            DeferredShaderBindings._aoBlurCenterWeight = asset.AmbientOcclusionBlurCenterWeight;

            DeferredShaderBindings._debugFloat = asset.debugFloat;
            DeferredShaderBindings._debugFloat2 = asset.debugFloat2;
            DeferredShaderBindings._debugFloat3 = asset.debugFloat3;
        }

        private static Mesh GetFullScreenCameraQuad(Camera c)
        {
            if (!s_FullscreenCameraMesh)
            {
                s_FullscreenVertices = new Vector3[4];
                s_FullscreenCameraMesh = new Mesh { name = "Fullscreen Clipping Space Quad" };

            }

            c.CalculateFrustumCorners(new Rect(0, 0, 1, 1), c.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, s_FullscreenVertices);

            s_FullscreenCameraMesh.SetVertices(s_FullscreenVertices);
            s_FullscreenCameraMesh.SetIndices(new[] { 0, 1, 3, 1, 3, 2 }, MeshTopology.Triangles, 0, false);

            s_FullscreenCameraMesh.UploadMeshData(true);
            return s_FullscreenCameraMesh;
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            BeginFrameRendering(context, cameras);

            DeferredShaderBindings.SetPerFrameShaderVariables(context);

            foreach (Camera camera in cameras)
            {
                context.SetupCameraProperties(camera);
                DeferredShaderBindings.SetPerCameraShaderVariables(context, camera);
                BeginCameraRendering(context, camera);
#if UNITY_EDITOR
                if (camera.cameraType == CameraType.SceneView)
                {
                    ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
                }
#endif

                CullingResults cullingResults = new CullingResults();

                if (Cull(context, camera, ref cullingResults))
                {
                    DrawCamera(context, cullingResults, camera);
                }
                EndCameraRendering(context, camera);
            }
            EndFrameRendering(context, cameras);
        }

        bool Cull(ScriptableRenderContext context, Camera camera, ref CullingResults result)
        {
            // Culling. Adjust culling parameters for your needs. One could enable/disable
            // per-object lighting or control shadow caster distance.
            if (camera.TryGetCullingParameters(out var cullingParameters))
            {
                result = context.Cull(ref cullingParameters);
                return true;
            }
            else
            {
                return false;
            }
        }

        void DrawCamera(ScriptableRenderContext context, CullingResults cullingResults, Camera camera)
        {
            // Create the attachment descriptors. If these attachments are not specifically bound to any RenderTexture using the ConfigureTarget calls,
            // these are treated as temporary surfaces that are discarded at the end of the renderpass
            var tmpAlbedo = new AttachmentDescriptor(RenderTextureFormat.ARGB32);
            var albedo = new AttachmentDescriptor(RenderTextureFormat.ARGB32);
            var normal = new AttachmentDescriptor(RenderTextureFormat.ARGB2101010);
            var depth = new AttachmentDescriptor(RenderTextureFormat.Depth);
            var ambientOcclusion = new AttachmentDescriptor(RenderTextureFormat.R8);
            var lighting = new AttachmentDescriptor(RenderTextureFormat.R8);

            // At the beginning of the render pass, clear the emission buffer to all black, and the depth buffer to 1.0f
            // Tested with only one parameter and still works
            //emission.ConfigureClear(new Color(0.0f, 0.0f, 0.0f, 0.0f), 1.0f, 0);
            depth.ConfigureClear(new Color(), 1.0f, 0);

            // También se podría limpiar el albedo buffer, pero como estamos usando skybox nos aseguramos que todos los pixels se van a actualizar
            // albedo.ConfigureClear(new Color(.5f, .5f, .5f, .5f), 1.0f, 0);

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

            // Bind the albedo surface to the current camera target, so the final pass will render the Scene to the screen backbuffer
            // The second argument specifies whether the existing contents of the surface need to be loaded as the initial values;
            // in our case we do not need that because we'll be clearing the attachment anyway. This saves a lot of memory
            // bandwidth on tiled GPUs.
            // The third argument specifies whether the rendering results need to be written out to memory at the end of
            // the renderpass. We need this as we'll be generating the final image there.
            // We could do this in the constructor already, but the camera target may change on the fly, esp. in the editor
            albedo.ConfigureTarget(target, false, true);

            // All other attachments are transient surfaces that are not stored anywhere. If the renderer allows,
            // those surfaces do not even have a memory allocated for the pixel values, saving RAM usage.

            // Start the renderpass using the given scriptable rendercontext, resolution, samplecount, array of attachments that will be used within the renderpass and the depth surface
            var attachments = new NativeArray<AttachmentDescriptor>(6, Allocator.Temp);
            const int depthIndex = 0, tmpAlbedoIndex = 1, albedoIndex = 2, normalIndex = 3, ambientOcclusionIndex = 4, lightingIndex = 5;
            attachments[depthIndex] = depth;
            attachments[tmpAlbedoIndex] = tmpAlbedo;
            attachments[albedoIndex] = albedo;
            attachments[normalIndex] = normal;
            attachments[ambientOcclusionIndex] = ambientOcclusion;
            attachments[lightingIndex] = lighting;

            // al iniciar un Scoped Render Pass no hay que invocar el End Render Pass
            using (context.BeginScopedRenderPass(width, height, aa, attachments, depthIndex))
            {
                attachments.Dispose();

                // Start the first subpass, GBuffer creation: render to albedo and normal, no need to read any input attachments
                var gbufferColors = new NativeArray<int>(2, Allocator.Temp);
                gbufferColors[0] = tmpAlbedoIndex;
                gbufferColors[1] = normalIndex;

                using (context.BeginScopedSubPass(gbufferColors))
                {
                    gbufferColors.Dispose();

                    // Render the deferred G-Buffer
                    RenderGBuffer(context, cullingResults, camera);
                }

                // segunda sub-pasada, lighting + cálculo de  SSAO
                var aoInputs = new NativeArray<int>(2, Allocator.Temp);
                aoInputs[0] = depthIndex;
                aoInputs[1] = normalIndex;
                var aoColors = new NativeArray<int>(2, Allocator.Temp);
                aoColors[0] = lightingIndex;
                aoColors[1] = ambientOcclusionIndex;
                using (context.BeginScopedSubPass(aoColors, aoInputs, true))
                {
                    aoInputs.Dispose();
                    aoColors.Dispose();

                    RenderDeferredLighting(context, cullingResults, camera);
                }

                var applyAOInputs = new NativeArray<int>(3, Allocator.Temp);
                applyAOInputs[0] = tmpAlbedoIndex;
                applyAOInputs[1] = ambientOcclusionIndex;
                applyAOInputs[2] = lightingIndex;
                var applyAOColors = new NativeArray<int>(1, Allocator.Temp);
                applyAOColors[0] = albedoIndex;
                using (context.BeginScopedSubPass(applyAOColors, applyAOInputs, true))
                {
                    applyAOColors.Dispose();
                    applyAOInputs.Dispose();

                    RenderAO(context, cullingResults, camera);
                }

                // El siguiente código está comentado para ser tomado como base

                // // Second subpass, lighting: Render to the emission buffer, read from albedo, specRough, normal and depth.
                // // The last parameter indicates whether the depth buffer can be bound as read-only.
                // // Note that some renderers (notably iOS Metal) won't allow reading from the depth buffer while it's bound as Z-buffer,
                // // so those renderers should write the Z into an additional FP32 render target manually in the pixel shader and read from it instead
                // var lightingColors = new NativeArray<int>(1, Allocator.Temp);
                // lightingColors[0] = emissionIndex;
                // var lightingInputs = new NativeArray<int>(4, Allocator.Temp);
                // lightingInputs[0] = albedoIndex;
                // lightingInputs[1] = specRoughIndex;
                // lightingInputs[2] = normalIndex;
                // lightingInputs[3] = depthIndex;
                // context.BeginSubPass(lightingColors, lightingInputs, true);
                // lightingColors.Dispose();
                // lightingInputs.Dispose();

                // // PushGlobalShadowParams(context);
                // // RenderLighting(camera, cullResults, context);

                // context.EndSubPass();

                // // Third subpass, tonemapping: Render to albedo (which is bound to the camera target), read from emission.
                // var tonemappingColors = new NativeArray<int>(1, Allocator.Temp);
                // tonemappingColors[0] = albedoIndex;
                // var tonemappingInputs = new NativeArray<int>(1, Allocator.Temp);
                // tonemappingInputs[0] = emissionIndex;
                // context.BeginSubPass(tonemappingColors, tonemappingInputs, true);
                // tonemappingColors.Dispose();
                // tonemappingInputs.Dispose();

                // // present frame buffer.
                // // FinalPass(context);

                // context.EndSubPass();

            }

            // Submit commands to GPU. Up to this point all commands have been enqueued in the context.
            // Several submits can be done in a frame to better controls CPU/GPU workload.
            context.Submit();
        }

        void RenderGBuffer(ScriptableRenderContext context, CullingResults cullingResults, Camera camera)
        {
            bool enableDynamicBatching = false;
            // bool enableInstancing = false;
            PerObjectData perObjectData = PerObjectData.None;

            FilteringSettings opaqueFilteringSettings = new FilteringSettings(RenderQueueRange.opaque);
            FilteringSettings transparentFilteringSettings = new FilteringSettings(RenderQueueRange.transparent);

            SortingSettings opaqueSortingSettings = new SortingSettings(camera);
            opaqueSortingSettings.criteria = SortingCriteria.CommonOpaque;

            SortingSettings transparentSortingSettings = new SortingSettings(camera);
            transparentSortingSettings.criteria = SortingCriteria.CommonTransparent;

            // ShaderTagId must match the "LightMode" tag inside the shader pass.
            // If not "LightMode" tag is found the object won't render.
            DrawingSettings opaqueDrawingSettings = new DrawingSettings(DeferredShaderPassTag.gbuffer, opaqueSortingSettings);
            opaqueDrawingSettings.enableDynamicBatching = enableDynamicBatching;
            // opaqueDrawingSettings.enableInstancing = enableInstancing;
            opaqueDrawingSettings.perObjectData = perObjectData;

            // ShaderTagId must match the "LightMode" tag inside the shader pass.
            // If not "LightMode" tag is found the object won't render.
            DrawingSettings transparentDrawingSettings = new DrawingSettings(DeferredShaderPassTag.gbuffer, transparentSortingSettings);
            transparentDrawingSettings.enableDynamicBatching = enableDynamicBatching;
            // transparentDrawingSettings.enableInstancing = enableInstancing;
            transparentDrawingSettings.perObjectData = perObjectData;

            //Get the setting from camera component
            bool drawSkyBox = camera.clearFlags == CameraClearFlags.Skybox ? true : false;
            bool clearDepth = camera.clearFlags == CameraClearFlags.Nothing ? false : true;
            bool clearColor = camera.clearFlags == CameraClearFlags.Color ? true : false;

            // Render Opaque objects given the filtering and settings computed above.
            // This functions will sort and batch objects.
            context.DrawRenderers(cullingResults, ref opaqueDrawingSettings, ref opaqueFilteringSettings);

            // // Renders skybox if required
            // if (drawSkyBox && RenderSettings.skybox != null)
            // {
            //     context.DrawSkybox(camera);
            // }

            // // Render Transparent objects given the filtering and settings computed above.
            // // This functions will sort and batch objects.
            // context.DrawRenderers(cullingResults, ref transparentDrawingSettings, ref transparentFilteringSettings);
        }

        void RenderDeferredLighting(ScriptableRenderContext context, CullingResults cullingResults, Camera camera)
        {
            CommandBuffer cmd = CommandBufferPool.Get("Render Deferred Lighting");

            // material que implementa AMBIENT OCCLUSION, la pasada 0 (parámetro 5) sólo calcula AO
            Matrix4x4 identity = new Matrix4x4();
            cmd.DrawMesh(DeferredRenderPipeline.fullscreenMesh, identity, deferredLightingMat, 0, 0);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

        }

        void RenderAO(ScriptableRenderContext context, CullingResults cullingResults, Camera camera)
        {
            CommandBuffer cmd = CommandBufferPool.Get("Apply Ambient Occlusion");

            // material que implementa AMBIENT OCCLUSION, la pasada 1 (parámetro 5) sólo aplica AO
            cmd.DrawMesh(DeferredRenderPipeline.fullscreenMesh, Matrix4x4.identity, deferredLightingMat, 0, 1);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            bool drawSkyBox = camera.clearFlags == CameraClearFlags.Skybox ? true : false;
            if (drawSkyBox && RenderSettings.skybox != null)
            {
                context.DrawSkybox(camera);
            }
        }
    }

}
