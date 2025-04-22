namespace UnityEngine.Rendering.DeferredRenderPipeline
{
    [CreateAssetMenu(menuName = "Rendering/Deferred Render Pipeline")]
    public class DeferredRenderPipelineAsset : RenderPipelineAsset
    {
        public float AmbientOcclusionIntensity = 1;
        public float AmbientOcclusionRadius = 0.1f;
        public float AmbientOcclusionFallOff = 1;

        [Range(0, 1)]
        public float AmbientOcclusionBlurCenterWeight;

        public float debugFloat;
        public Vector2 debugFloat2;
        public Vector3 debugFloat3;
        protected override RenderPipeline CreatePipeline()
        {
            return new DeferredRenderPipeline(this);
        }
    }
}


