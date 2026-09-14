using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PixelateRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Shaders")]
        [SerializeField] public Shader pixelShader;
        [SerializeField] public Shader quantizeShader;

        [Header("Pixelation (local override)")]
        public bool enablePixelate = true;
        [Range(1, 64)] public int pixelSize = 8;

        [Header("Quantization")]
        public bool enableQuantization = false;
        // Legacy fallback used only when colorPaletteSO is not assigned.
        public Color[] colorPalette = new Color[] { Color.black, Color.white };
        public bool useLABDistance = true;

        [Header("Visibility")]
        public bool showInSceneView = false;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public Settings settings = new();

    [Tooltip("Shared pixelation settings. When assigned, this overrides the local pixel size.")]
    public PixelationSettings sharedPixelationSettings;

    [Tooltip("Shared color palette. When assigned, this overrides the local color array.")]
    public ColorPaletteSO colorPaletteSO;

    private PixelateRenderPass _renderPass;
    private Material _pixelateMaterial;
    private Material _quantizeMaterial;

    public override void Create()
    {
        if (_pixelateMaterial == null)
        {
            if (settings.pixelShader == null)
            {
                settings.pixelShader = Shader.Find("Hidden/Pixelate");
            }
            // Keep the shader as an asset/Shader.Find result; editor-only generated fallbacks break player builds.
            if (settings.pixelShader == null)
            {
                Debug.LogError("Pixelate shader 'Hidden/Pixelate' was not found.");
                return;
            }
            _pixelateMaterial = new Material(settings.pixelShader);
        }

        if (_quantizeMaterial == null)
        {
            if (settings.quantizeShader == null)
            {
                settings.quantizeShader = Shader.Find("Hidden/Quantize");
            }
            // Keep the shader as an asset/Shader.Find result; editor-only generated fallbacks break player builds.
            if (settings.quantizeShader == null)
            {
                Debug.LogError("Quantize shader 'Hidden/Quantize' was not found.");
                return;
            }
            _quantizeMaterial = new Material(settings.quantizeShader);
        }

        _renderPass = new PixelateRenderPass(_pixelateMaterial, _quantizeMaterial, settings, sharedPixelationSettings, colorPaletteSO)
        {
            renderPassEvent = settings.renderPassEvent
        };
    }

    protected override void Dispose(bool disposing)
    {
        _renderPass?.Dispose();
        if (_pixelateMaterial != null) CoreUtils.Destroy(_pixelateMaterial);
        if (_quantizeMaterial != null) CoreUtils.Destroy(_quantizeMaterial);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cameraType = renderingData.cameraData.cameraType;
        bool shouldRender = cameraType == CameraType.Game ||
                            (cameraType == CameraType.SceneView && settings.showInSceneView);
        if (shouldRender && _renderPass != null) renderer.EnqueuePass(_renderPass);
    }
}
