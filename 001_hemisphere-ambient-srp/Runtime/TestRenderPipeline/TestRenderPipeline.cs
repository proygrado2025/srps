using Unity.Collections;
using System.Collections.Generic;

namespace UnityEngine.Rendering.TestRenderPipeline
{
    public sealed class TestRenderPipeline : RenderPipeline
    {
        static Mesh s_FullscreenMesh = null;
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
        static Mesh s_FullscreenCameraMesh = null;
        static List<Vector2> s_TextCoords = new List<Vector2>
                {
                    new Vector2(0.0f, 0),
                    new Vector2(0.0f, 1),
                    new Vector2(1.0f, 0),
                    new Vector2(1.0f, 1)
                };

        private Material testMat;
        public TestRenderPipeline(TestRenderPipelineAsset asset)
        {
            testMat = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/testSRP"));
        }

        private static Mesh GetFullScreenCameraQuad(Camera c)
        {
            if (!s_FullscreenCameraMesh)
            {
                s_FullscreenCameraMesh = new Mesh { name = "Fullscreen Clipping Space Quad" };
            }

            Vector3[] frustumCorners = new Vector3[4];
            c.CalculateFrustumCorners(new Rect(0, 0, 1, 1), c.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, frustumCorners);

            for (int i = 0; i < frustumCorners.Length; ++i)
            {
                var worldSpaceCorner = c.transform.TransformVector(frustumCorners[i]);
                frustumCorners[i] = worldSpaceCorner;

                Debug.DrawRay(c.transform.position, worldSpaceCorner, Color.red);
            }

            Debug.Log(
                frustumCorners[0] + "\n" +
                frustumCorners[1] + "\n" +
                frustumCorners[2] + "\n" +
                frustumCorners[3] + "\n"
            );

            s_FullscreenCameraMesh.SetVertices(frustumCorners);
            s_FullscreenCameraMesh.SetUVs(0, s_TextCoords);
            s_FullscreenCameraMesh.SetIndices(new[] { 0, 1, 3, 1, 3, 2 }, MeshTopology.Triangles, 0, false);

            s_FullscreenCameraMesh.UploadMeshData(true);
            return s_FullscreenCameraMesh;
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach (Camera camera in cameras)
            {
                context.SetupCameraProperties(camera);
                TestShaderBindings.SetPerCameraShaderVariables(context, camera);
#if UNITY_EDITOR
                if (camera.cameraType == CameraType.SceneView)
                    ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
#endif

                CullingResults cullingResults = Cull(context, camera);
                TestRender(context, cullingResults, camera);
            }
        }

        CullingResults Cull(ScriptableRenderContext context, Camera camera)
        {
            // Culling. Adjust culling parameters for your needs. One could enable/disable
            // per-object lighting or control shadow caster distance.
            camera.TryGetCullingParameters(out var cullingParameters);
            return context.Cull(ref cullingParameters);
        }

        void TestRender(ScriptableRenderContext context, CullingResults cullingResults, Camera camera)
        {
            CommandBuffer cmd = CommandBufferPool.Get("test cmd");

            // material que implementa AMBIENT OCCLUSION
            cmd.DrawMesh(TestRenderPipeline.fullscreenMesh, Matrix4x4.identity, testMat, 0, 0);
            //cmd.DrawMesh(GetFullScreenCameraQuad(camera), Matrix4x4.identity, testMat, 0, 0);
            context.DrawSkybox(camera);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        void DrawCamera(ScriptableRenderContext context, CullingResults cullingResults, Camera camera)
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
            DrawingSettings opaqueDrawingSettings = new DrawingSettings(TestShaderPassTag.testLightMode, opaqueSortingSettings);
            opaqueDrawingSettings.enableDynamicBatching = enableDynamicBatching;
            // opaqueDrawingSettings.enableInstancing = enableInstancing;
            opaqueDrawingSettings.perObjectData = perObjectData;

            // ShaderTagId must match the "LightMode" tag inside the shader pass.
            // If not "LightMode" tag is found the object won't render.
            DrawingSettings transparentDrawingSettings = new DrawingSettings(TestShaderPassTag.testLightMode, transparentSortingSettings);
            transparentDrawingSettings.enableDynamicBatching = enableDynamicBatching;
            // transparentDrawingSettings.enableInstancing = enableInstancing;
            transparentDrawingSettings.perObjectData = perObjectData;

            // Sets active render target and clear based on camera background color.
            var cmd = CommandBufferPool.Get();
            cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            cmd.ClearRenderTarget(true, true, camera.backgroundColor);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            // Render Opaque objects given the filtering and settings computed above.
            // This functions will sort and batch objects.
            context.DrawRenderers(cullingResults, ref opaqueDrawingSettings, ref opaqueFilteringSettings);

            cmd = CommandBufferPool.Get("full screen quad");
            cmd.DrawMesh(GetFullScreenCameraQuad(camera), Matrix4x4.identity, testMat, 0, 0);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);


            // Renders skybox if required
            if (camera.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null)
                context.DrawSkybox(camera);


            // Render Transparent objects given the filtering and settings computed above.
            // This functions will sort and batch objects.
            context.DrawRenderers(cullingResults, ref transparentDrawingSettings, ref transparentFilteringSettings);

            // Submit commands to GPU. Up to this point all commands have been enqueued in the context.
            // Several submits can be done in a frame to better controls CPU/GPU workload.
            context.Submit();
        }

    }

}
