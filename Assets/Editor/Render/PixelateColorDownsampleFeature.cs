using UnityEditor;
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
        // legacy colors – используются только если colorPaletteSO == null
        public Color[] colorPalette = new Color[] { Color.black, Color.white };
        public bool useLABDistance = true;

        [Header("Visibility")]
        public bool showInSceneView = false;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public Settings settings = new();

    [Tooltip("Общие настройки пикселизации (если назначены, переопределяют локальный pixelSize)")]
    public PixelationSettings sharedPixelationSettings;

    [Tooltip("Палитра цветов (если назначена, переопределяет локальный массив цветов)")]
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
            //if (shader == null) shader = CreatePixelateShader();
            _pixelateMaterial = new Material(settings.pixelShader);
        }
        if (_quantizeMaterial == null)
        {
            if (settings.quantizeShader == null)
            {
                settings.quantizeShader = Shader.Find("Hidden/Quantize");
            }
            //if (shader == null) shader = CreateQuantizeShader();
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
        if (shouldRender) renderer.EnqueuePass(_renderPass);
    }
}