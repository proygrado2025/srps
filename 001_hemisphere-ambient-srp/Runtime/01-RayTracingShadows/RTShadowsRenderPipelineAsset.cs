using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.RTShadowsRenderPipeline
{
    [CreateAssetMenu(menuName = "Rendering/01 - RTShadows Render Pipeline")]
    public class RTShadowsRenderPipelineAsset : RenderPipelineAsset
    {
        public float debugFloat;
        public Vector2 debugFloat2;
        public Vector3 debugFloat3;

        public RayTracingShader RTShader;

        public Texture CubeMapTexture;
        protected override RenderPipeline CreatePipeline()
        {
            return new RTShadowsRenderPipeline(this);
        }
    }
}


