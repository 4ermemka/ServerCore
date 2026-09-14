using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PixelateNormalEdgeFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Shaders")]
        [SerializeField] public Shader normalEdgeShader;

        [Header("Pixelation")]
        [Range(1, 64)] public int pixelSize = 8;

        [Header("Edge Detection")]
        [Range(0.01f, 0.5f)] public float normalBias = 0.3f;
        [Tooltip("Maximum allowed Linear01Depth delta between normal samples.")]
        [Range(0.0001f, 0.01f)] public float depthThreshold = 0.001f;
        public bool useDepthFilter = true;

        [Header("Edge Rendering")]
        [Range(0f, 1f)] public float brightenAmount = 0.3f;
        [Tooltip("Line width in screen pixels before pixelation.")]
        [Range(1, 10)] public int edgeWidth = 1;

        [Header("Luminance Modulation (based on final pixel brightness)")]
        public bool modulateByLuminance = true;
        [Range(0f, 1f)] public float luminanceContribution = 1f;
        [Tooltip("Minimum pixel luminance where the line starts appearing.")]
        [Range(0f, 1f)] public float minLuminance = 0.2f;
        [Tooltip("Maximum pixel luminance where the line reaches full brightness.")]
        [Range(0f, 1f)] public float maxLuminance = 0.8f;

        [Header("Stencil Filtering (experimental)")]
        public bool useStencilFilter = false;
        [Range(0, 255)]
        public int stencilReference = 0;
        public CompareFunction stencilCompare = CompareFunction.Equal;

        [Header("Debug")]
        public bool normalPreview = false;
        public bool showInSceneView = false;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

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
