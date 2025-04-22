using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.SimpleRTRenderPipeline
{
    [CreateAssetMenu(menuName = "Rendering/00 - SimpleRT Render Pipeline")]
    public class SimpleRTRenderPipelineAsset : RenderPipelineAsset
    {
        public float debugFloat;
        public Vector2 debugFloat2;
        public Vector3 debugFloat3;

        public RayTracingShader RTShader;

        public Texture CubeMapTexture;
        protected override RenderPipeline CreatePipeline()
        {
            return new SimpleRTRenderPipeline(this);
        }
    }
}


