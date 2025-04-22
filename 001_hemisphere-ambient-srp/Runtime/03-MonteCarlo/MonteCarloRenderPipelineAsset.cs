using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.MonteCarloRenderPipeline
{
    [CreateAssetMenu(menuName = "Rendering/03 - Monte Carlo Render Pipeline")]
    public class MonteCarloRenderPipelineAsset : RenderPipelineAsset
    {
        [Range(0,8)]
        public uint maxRecursionDepth; //g_max_recursion_depth
        public uint spp;
        public int debugInt;
        public float debugFloat;
        public Vector2 debugFloat2;
        public Vector3 debugFloat3;

        public RayTracingShader RTShader;
        public ComputeShader preprocessCS;
        public ComputeShader regressionCS;
        public ComputeShader postprocessCS;

        [Header("Denoiser")]
        public bool UseDenoiserPreprocess = true;
        public bool UseDenoiserRegression = true;
        public bool UseDenoiserPostprocess = true;


        public Texture CubeMapTexture;
        protected override RenderPipeline CreatePipeline()
        {
            return new MonteCarloRenderPipeline(this);
        }
    }
}


