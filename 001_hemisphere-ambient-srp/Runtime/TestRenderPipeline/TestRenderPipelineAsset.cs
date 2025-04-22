namespace UnityEngine.Rendering.TestRenderPipeline
{
    [CreateAssetMenu(menuName = "Rendering/Test Render Pipeline")]
    public class TestRenderPipelineAsset : RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline()
        {
            return new TestRenderPipeline(this);
        }
    }
}


