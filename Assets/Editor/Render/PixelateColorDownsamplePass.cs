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

    private static Vector4[] _colorCache = new Vector4[256]; // увеличен до 256

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

    public void Dispose() { } // добавлен пустой Dispose для совместимости с Feature

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

        int pixelSize = (_sharedPixelationSettings != null) ? _sharedPixelationSettings.pixelSize : _settings.pixelSize;

        if (_settings.enablePixelate)
        {
            TextureHandle afterPixelate = renderGraph.CreateTexture(desc);
            _pixelateMaterial.SetFloat(PixelSizeId, pixelSize);
            var blitParams = new RenderGraphUtils.BlitMaterialParameters(current, afterPixelate, _pixelateMaterial, 0);
            renderGraph.AddBlitPass(blitParams, "Pixelate");
            current = afterPixelate;
        }

        if (_settings.enableQuantization)
        {
            Color[] paletteColors;
            bool useLAB;
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

            if (paletteColors != null && paletteColors.Length > 0)
            {
                TextureHandle afterQuantize = renderGraph.CreateTexture(desc);
                int count = Mathf.Min(paletteColors.Length, 256); // теперь до 256
                _quantizeMaterial.SetFloat(PaletteSizeId, count);
                for (int i = 0; i < count; i++)
                    _colorCache[i] = (Vector4)paletteColors[i];
                _quantizeMaterial.SetVectorArray(PaletteColorsId, _colorCache);

                if (useLAB)
                    _quantizeMaterial.EnableKeyword("DISTANCE_LAB_DELTAE");
                else
                    _quantizeMaterial.DisableKeyword("DISTANCE_LAB_DELTAE");

                var blitParams = new RenderGraphUtils.BlitMaterialParameters(current, afterQuantize, _quantizeMaterial, 0);
                renderGraph.AddBlitPass(blitParams, "Quantize");
                current = afterQuantize;
            }
        }

        resourceData.cameraColor = current;
    }
}