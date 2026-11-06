using UnityEngine;
using UnityEngine.Rendering.Universal;

public partial class PixelateRenderFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Shaders")]
        [SerializeField]
        public Shader pixelShader;
        [SerializeField]
        public Shader quantizeShader;
        [Header("Pixelation (local override)")]
        public bool enablePixelate = true;
        [Range(1, 64)]
        public int pixelSize = 8;
        [Header("Quantization")]
        public bool enableQuantization = false;
        // Legacy fallback used only when colorPaletteSO is not assigned.
        public Color[] colorPalette = new Color[]
        {
            Color.black,
            Color.white
        };
        public bool useLABDistance = true;
        [Header("Visibility")]
        public bool showInSceneView = false;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }
}
