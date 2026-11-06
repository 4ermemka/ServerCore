using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

public class PixelateRenderPass : ScriptableRenderPass
{
    private Material _pixelateMaterial;
    private Material _quantizeMaterial;
    private PixelateRenderFeature.Settings _settings;
    private PixelationSettings _sharedPixelationSettings;
    private ColorPaletteSO _colorPaletteSO;

    private static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");
    private static readonly int PaletteSizeId = Shader.PropertyToID("_PaletteSize");
    private static readonly int PaletteColorsId = Shader.PropertyToID("_PaletteColors");
    private static readonly int PaletteLabColorsId = Shader.PropertyToID("_PaletteLabColors");
    private static readonly int ApplyPixelationId = Shader.PropertyToID("_ApplyPixelation");

    private static Vector4[] _colorCache = new Vector4[256];
    private static Vector4[] _labColorCache = new Vector4[256];

    public PixelateRenderPass(Material pixelateMat, Material quantizeMat,
                              PixelateRenderFeature.Settings settings,
                              PixelationSettings sharedPixelationSettings,
                              ColorPaletteSO colorPaletteSO)
    {
        _pixelateMaterial = pixelateMat;
        _quantizeMaterial = quantizeMat;
        _settings = settings;
        _sharedPixelationSettings = sharedPixelationSettings;
        _colorPaletteSO = colorPaletteSO;
        profilingSampler = new ProfilingSampler(nameof(PixelateRenderPass));
    }

    public void Dispose() { }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_pixelateMaterial == null || _quantizeMaterial == null) return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        if (resourceData == null || cameraData == null || resourceData.isActiveTargetBackBuffer) return;

        TextureHandle srcColor = resourceData.activeColorTexture;
        TextureDesc desc = renderGraph.GetTextureDesc(srcColor);
        desc.filterMode = FilterMode.Point;
        TextureHandle current = srcColor;

        int pixelSize = Mathf.Max(1, (_sharedPixelationSettings != null) ? _sharedPixelationSettings.pixelSize : _settings.pixelSize);
        Color[] paletteColors = null;
        bool useLAB = false;
        bool canQuantize = false;

        if (_settings.enableQuantization)
        {
            if (_colorPaletteSO != null)
            {
                paletteColors = _colorPaletteSO.colors;
                useLAB = _colorPaletteSO.useLABDistance;
            }
            else
            {
                paletteColors = _settings.colorPalette;
                useLAB = _settings.useLABDistance;
            }

            canQuantize = paletteColors != null && paletteColors.Length > 0;
        }

        // If quantization runs, it can sample the pixel block center itself and save one full-screen blit.
        bool applyPixelationInQuantize = _settings.enablePixelate && canQuantize;

        if (_settings.enablePixelate && !applyPixelationInQuantize)
        {
            TextureHandle afterPixelate = renderGraph.CreateTexture(desc);
            _pixelateMaterial.SetFloat(PixelSizeId, pixelSize);
            var blitParams = new RenderGraphUtils.BlitMaterialParameters(current, afterPixelate, _pixelateMaterial, 0);
            renderGraph.AddBlitPass(blitParams, "Pixelate");
            current = afterPixelate;
        }

        if (canQuantize)
        {
            TextureHandle afterQuantize = renderGraph.CreateTexture(desc);
            int count = Mathf.Min(paletteColors.Length, 256);
            _quantizeMaterial.SetInt(PaletteSizeId, count);
            _quantizeMaterial.SetFloat(PixelSizeId, pixelSize);
            _quantizeMaterial.SetFloat(ApplyPixelationId, applyPixelationInQuantize ? 1f : 0f);

            // Precompute palette LAB values once on CPU; the shader still computes LAB only for the sampled source color.
            for (int i = 0; i < count; i++)
            {
                _colorCache[i] = (Vector4)paletteColors[i];
                _labColorCache[i] = RgbToLab(paletteColors[i]);
            }
            _quantizeMaterial.SetVectorArray(PaletteColorsId, _colorCache);
            _quantizeMaterial.SetVectorArray(PaletteLabColorsId, _labColorCache);

            if (useLAB)
                _quantizeMaterial.EnableKeyword("DISTANCE_LAB_DELTAE");
            else
                _quantizeMaterial.DisableKeyword("DISTANCE_LAB_DELTAE");

            var blitParams = new RenderGraphUtils.BlitMaterialParameters(current, afterQuantize, _quantizeMaterial, 0);
            renderGraph.AddBlitPass(blitParams, applyPixelationInQuantize ? "Pixelate + Quantize" : "Quantize");
            current = afterQuantize;
        }

        resourceData.cameraColor = current;
    }

    private static Vector4 RgbToLab(Color color)
    {
        Vector3 rgb = new Vector3(
            Mathf.Pow(Mathf.Clamp01(color.r), 2.2f),
            Mathf.Pow(Mathf.Clamp01(color.g), 2.2f),
            Mathf.Pow(Mathf.Clamp01(color.b), 2.2f));

        Vector3 xyz = new Vector3(
            0.4124564f * rgb.x + 0.3575761f * rgb.y + 0.1804375f * rgb.z,
            0.2126729f * rgb.x + 0.7151522f * rgb.y + 0.0721750f * rgb.z,
            0.0193339f * rgb.x + 0.1191920f * rgb.y + 0.9503041f * rgb.z);

        xyz.x /= 0.95047f;
        xyz.z /= 1.08883f;

        Vector3 f = new Vector3(LabPivot(xyz.x), LabPivot(xyz.y), LabPivot(xyz.z));
        return new Vector4(116f * f.y - 16f, 500f * (f.x - f.y), 200f * (f.y - f.z), color.a);
    }

    private static float LabPivot(float value)
    {
        return value > 0.008856f ? Mathf.Pow(value, 1f / 3f) : 7.787f * value + 16f / 116f;
    }
}
