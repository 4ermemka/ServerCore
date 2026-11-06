using UnityEngine.Rendering.Universal;

public partial class ExclusionMaskFeature : ScriptableRendererFeature
{

    public Settings settings = new();
    private ExclusionMaskPass _renderPass;

    public override void Create()
    {
        _renderPass = new ExclusionMaskPass(settings) { renderPassEvent = settings.renderPassEvent };
    }

    protected override void Dispose(bool disposing)
    {
        _renderPass?.Dispose();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.excludedLayers != 0)
            renderer.EnqueuePass(_renderPass);
    }
}
