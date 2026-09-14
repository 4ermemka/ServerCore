using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PixelateDepthEdgeFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Shaders")]
        [SerializeField] public Shader depthEdgeShader;

        [Header("Pixelation")]
        [Tooltip("Pixel size used when shared PixelationSettings is not assigned.")]
        [Range(1, 64)]
        public int pixelSize = 8;

        [Header("Depth Difference")]
        [Tooltip("Minimum edge depth delta after shader-side Linear01Depth conversion.")]
        [Range(0.0f, 1.0f)]
        public float minDepthDifference = 0.002f;
        [Tooltip("Maximum edge depth delta after shader-side Linear01Depth conversion.")]
        [Range(0.0f, 1.0f)]
        public float maxDepthDifference = 0.01f;

        [Header("Darkening")]
        [Tooltip("Minimum darkening amount.")]
        [Range(0f, 1f)]
        public float minDarkening = 0f;
        [Tooltip("Maximum darkening amount.")]
        [Range(0f, 1f)]
        public float maxDarkening = 0.5f;

        [Header("Edge Side")]
        public EdgeSide edgeSide = EdgeSide.Foreground;

        [Header("Luminance Modulation (optional)")]
        public bool modulateByLuminance = false;
        [Range(0f, 1f)]
        public float luminanceModulationStrength = 1f;
        [Range(0f, 1f)]
        public float minLuminance = 0.2f;
        [Range(0f, 1f)]
        public float maxLuminance = 0.8f;

        [Header("Stencil Filtering (experimental)")]
        public bool useStencilFilter = false;
        [Range(0, 255)]
        public int stencilReference = 0;
        public CompareFunction stencilCompare = CompareFunction.Equal;

        [Header("Debug")]
        public bool depthPreview = false;
        public bool showInSceneView = false;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public enum EdgeSide { Foreground, Background }

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
