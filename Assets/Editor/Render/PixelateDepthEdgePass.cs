using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class PixelateDepthEdgePass : ScriptableRenderPass
{
    private Material _material;
    private PixelateDepthEdgeFeature.Settings _settings;
    private PixelationSettings _sharedPixelationSettings;

    private static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");
    private static readonly int DepthBiasId = Shader.PropertyToID("_DepthBias");
    private static readonly int DarkenAmountId = Shader.PropertyToID("_DarkenAmount");
    private static readonly int EdgeSideId = Shader.PropertyToID("_EdgeSide");
    private static readonly int DepthContrastId = Shader.PropertyToID("_DepthContrast");
    private static readonly int DepthBrightnessId = Shader.PropertyToID("_DepthBrightness");
    private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
    private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
    private static readonly int ModulateByLightingId = Shader.PropertyToID("_ModulateByLighting");
    private static readonly int LightingStrengthId = Shader.PropertyToID("_LightingStrength");

    public PixelateDepthEdgePass(Material material,
                                 PixelateDepthEdgeFeature.Settings settings,
                                 PixelationSettings sharedPixelationSettings)
    {
        _material = material;
        _settings = settings;
        _sharedPixelationSettings = sharedPixelationSettings;
        profilingSampler = new ProfilingSampler(nameof(PixelateDepthEdgePass));
    }

    public void Dispose() { }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_material == null) return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        if (resourceData == null || resourceData.isActiveTargetBackBuffer) return;

        TextureHandle srcColor = resourceData.activeColorTexture;
        TextureHandle depthTexture = resourceData.cameraDepthTexture;
        if (!depthTexture.IsValid()) return;

        TextureDesc desc = renderGraph.GetTextureDesc(srcColor);
        desc.filterMode = FilterMode.Point;
        TextureHandle dstColor = renderGraph.CreateTexture(desc);

        int pixelSize = (_sharedPixelationSettings != null) ? _sharedPixelationSettings.pixelSize : _settings.pixelSize;

        _material.SetFloat(PixelSizeId, pixelSize);
        _material.SetFloat(DepthBiasId, _settings.depthBias);
        _material.SetFloat(DarkenAmountId, _settings.darkenAmount);
        _material.SetFloat(EdgeSideId, (float)_settings.edgeSide);
        _material.SetFloat(DepthContrastId, _settings.depthContrast);
        _material.SetFloat(DepthBrightnessId, _settings.depthBrightness);
        _material.SetFloat(ModulateByLightingId, _settings.modulateByLighting ? 1f : 0f);
        _material.SetFloat(LightingStrengthId, _settings.lightingModulationStrength);

        if (_settings.depthPreview)
            _material.EnableKeyword("DEPTH_PREVIEW");
        else
            _material.DisableKeyword("DEPTH_PREVIEW");

        using (var builder = renderGraph.AddUnsafePass<PassData>("DepthEdge", out var passData))
        {
            passData.source = srcColor;
            passData.destination = dstColor;
            passData.depthTexture = depthTexture;
            passData.material = _material;

            builder.UseTexture(srcColor, AccessFlags.Read);
            builder.UseTexture(depthTexture, AccessFlags.Read);
            builder.UseTexture(dstColor, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
        }

        resourceData.cameraColor = dstColor;
    }

    private static void ExecutePass(PassData data, UnsafeGraphContext context)
    {
        var material = data.material;
        var cmd = context.cmd;

        material.SetTexture(BlitTextureId, data.source);
        material.SetTexture(DepthTextureId, data.depthTexture);

        cmd.SetRenderTarget(data.destination);
        cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1);
    }

    private class PassData
    {
        public TextureHandle source;
        public TextureHandle destination;
        public TextureHandle depthTexture;
        public Material material;
    }
}