namespace UnityEngine.Rendering.ForwardRenderPipeline
{
    [CreateAssetMenu(menuName = "Rendering/Forward Render Pipeline")]
    public class ForwardRenderPipelineAsset : RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline()
        {
            return new ForwardRenderPipeline(this);
        }
    }
}


