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

    private static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");
    private static readonly int PaletteSizeId = Shader.PropertyToID("_PaletteSize");
    private static readonly int PaletteColorsId = Shader.PropertyToID("_PaletteColors");

    private static Vector4[] _colorCache = new Vector4[16];

    public PixelateRenderPass(Material pixelateMat, Material quantizeMat, PixelateRenderFeature.Settings settings)
    {
        _pixelateMaterial = pixelateMat;
        _quantizeMaterial = quantizeMat;
        _settings = settings;
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
        TextureHandle afterPixelate = renderGraph.CreateTexture(desc);

        // --- Проход пикселизации ---
        _pixelateMaterial.SetFloat(PixelSizeId, _settings.pixelSize);
        var blitParams1 = new RenderGraphUtils.BlitMaterialParameters(srcColor, afterPixelate, _pixelateMaterial, 0);
        renderGraph.AddBlitPass(blitParams1, "Pixelate");

        TextureHandle finalOutput = afterPixelate;

        // --- Проход квантования (если включен) ---
        if (_settings.enableQuantization && _settings.colorPalette != null && _settings.colorPalette.Length > 0)
        {
            TextureHandle afterQuantize = renderGraph.CreateTexture(desc);

            int count = Mathf.Min(_settings.colorPalette.Length, 16);
            // Используем SetFloat вместо SetInteger
            _quantizeMaterial.SetFloat(PaletteSizeId, count);

            for (int i = 0; i < count; i++)
                _colorCache[i] = (Vector4)_settings.colorPalette[i];
            _quantizeMaterial.SetVectorArray(PaletteColorsId, _colorCache);

            if (_settings.useLABDistance)
                _quantizeMaterial.EnableKeyword("DISTANCE_LAB_DELTAE");
            else
                _quantizeMaterial.DisableKeyword("DISTANCE_LAB_DELTAE");

            var blitParams2 = new RenderGraphUtils.BlitMaterialParameters(afterPixelate, afterQuantize, _quantizeMaterial, 0);
            renderGraph.AddBlitPass(blitParams2, "Quantize");

            finalOutput = afterQuantize;
        }

        resourceData.cameraColor = finalOutput;
    }
}