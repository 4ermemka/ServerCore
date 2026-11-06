using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public partial class PixelateDepthEdgeFeature : ScriptableRendererFeature
{

    public Settings settings = new();
    public PixelationSettings sharedPixelationSettings;

    private PixelateDepthEdgePass _renderPass;
    private Material _material;

    public override void Create()
    {
        if (_material == null)
        {
            Shader shader = settings.depthEdgeShader != null
                ? settings.depthEdgeShader
                : Shader.Find("Hidden/PixelateDepthEdge");
            // Keep this as a real shader asset; editor-only generated fallbacks are unavailable in player builds.
            if (shader == null)
            {
                Debug.LogError("Depth edge shader 'Hidden/PixelateDepthEdge' was not found.");
                return;
            }

            _material = new Material(shader);
        }

        _renderPass = new PixelateDepthEdgePass(_material, settings, sharedPixelationSettings)
        {
            renderPassEvent = settings.renderPassEvent
        };
    }

    protected override void Dispose(bool disposing)
    {
        _renderPass?.Dispose();
        if (_material != null) CoreUtils.Destroy(_material);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cameraType = renderingData.cameraData.cameraType;
        bool shouldRender = cameraType == CameraType.Game ||
                            (cameraType == CameraType.SceneView && settings.showInSceneView);
        if (shouldRender && _renderPass != null) renderer.EnqueuePass(_renderPass);
    }
}
