using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util; // содержит AddBlitPass

public class PixelateRenderPass : ScriptableRenderPass
{
    private Material _material;
    private PixelateRenderFeature.Settings _settings;
    private static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");

    public PixelateRenderPass(Material material, PixelateRenderFeature.Settings settings)
    {
        _material = material;
        _settings = settings;
        profilingSampler = new ProfilingSampler(nameof(PixelateRenderPass));
    }

    public void Dispose() { }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_material == null)
            return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

        if (resourceData == null || cameraData == null)
            return;

        // Нельзя читать напрямую из backbuffer
        if (resourceData.isActiveTargetBackBuffer)
            return;

        TextureHandle srcColor = resourceData.activeColorTexture;

        // Создаём временную текстуру для результата
        TextureDesc desc = renderGraph.GetTextureDesc(srcColor);
        TextureHandle dstColor = renderGraph.CreateTexture(desc);

        // Устанавливаем размер пикселя в материале
        _material.SetFloat(PixelSizeId, _settings.pixelSize);

        // Используем готовый BlitPass – он автоматически свяжет srcColor с _BlitTexture
        RenderGraphUtils.BlitMaterialParameters blitParams =
            new(srcColor, dstColor, _material, 0);
        renderGraph.AddBlitPass(blitParams, "Pixelate");

        // Подменяем цвет камеры на наш результат, чтобы избежать лишнего копирования обратно
        resourceData.cameraColor = dstColor;
    }
}