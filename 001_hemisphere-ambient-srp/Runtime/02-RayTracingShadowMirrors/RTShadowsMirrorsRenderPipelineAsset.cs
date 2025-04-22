using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.RTShadowsMirrorsRenderPipeline
{
    [CreateAssetMenu(menuName = "Rendering/02 - RTShadowsMirrors Render Pipeline")]
    public class RTShadowsMirrorsRenderPipelineAsset : RenderPipelineAsset
    {
        public float debugFloat;
        public Vector2 debugFloat2;
        public Vector3 debugFloat3;

        public RayTracingShader RTShader;

        public Texture CubeMapTexture;
        protected override RenderPipeline CreatePipeline()
        {
            return new RTShadowsMirrorsRenderPipeline(this);
        }
    }
}


