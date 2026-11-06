using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public partial class PixelateNormalEdgeFeature : ScriptableRendererFeature
{

    public Settings settings = new();
    public PixelationSettings sharedPixelationSettings;

    private PixelateNormalEdgePass _renderPass;
    private Material _material;

    public override void Create()
    {
        if (_material == null)
        {
            Shader shader = settings.normalEdgeShader != null
                ? settings.normalEdgeShader
                : Shader.Find("Hidden/PixelateNormalEdge");
            // Keep this as a real shader asset; editor-only generated fallbacks are unavailable in player builds.
            if (shader == null)
            {
                Debug.LogError("Normal edge shader 'Hidden/PixelateNormalEdge' was not found.");
                return;
            }

            _material = new Material(shader);
        }

        _renderPass = new PixelateNormalEdgePass(_material, settings, sharedPixelationSettings)
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
