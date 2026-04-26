using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class PixelateNormalEdgePass : ScriptableRenderPass
{
    private Material _material;
    private PixelateNormalEdgeFeature.Settings _settings;
    private PixelationSettings _sharedPixelationSettings;

    private static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");
    private static readonly int NormalBiasId = Shader.PropertyToID("_NormalBias");
    private static readonly int DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
    private static readonly int BrightenAmountId = Shader.PropertyToID("_BrightenAmount");
    private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
    private static readonly int NormalsTextureId = Shader.PropertyToID("_NormalsTexture");
    private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
    private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
    private static readonly int ModulateByLuminanceId = Shader.PropertyToID("_ModulateByLuminance");
    private static readonly int LuminanceContributionId = Shader.PropertyToID("_LuminanceContribution");
    private static readonly int MinLuminanceId = Shader.PropertyToID("_MinLuminance");
    private static readonly int MaxLuminanceId = Shader.PropertyToID("_MaxLuminance");
    private static readonly int UseDepthFilterId = Shader.PropertyToID("_UseDepthFilter");
    private static readonly int UseStencilFilterId = Shader.PropertyToID("_UseStencilFilter");
    private static readonly int StencilReferenceId = Shader.PropertyToID("_StencilReference");
    private static readonly int StencilCompareId = Shader.PropertyToID("_StencilCompare");

    public PixelateNormalEdgePass(Material material,
                                  PixelateNormalEdgeFeature.Settings settings,
                                  PixelationSettings sharedPixelationSettings)
    {
        _material = material;
        _settings = settings;
        _sharedPixelationSettings = sharedPixelationSettings;
        profilingSampler = new ProfilingSampler(nameof(PixelateNormalEdgePass));
        ConfigureInput(ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth);
    }

    public void Dispose() { }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_material == null) return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        if (resourceData == null || resourceData.isActiveTargetBackBuffer) return;

        TextureHandle srcColor = resourceData.activeColorTexture;
        TextureHandle normalTexture = resourceData.cameraNormalsTexture;
        TextureHandle depthTexture = resourceData.cameraDepthTexture;

        if (!normalTexture.IsValid() || !depthTexture.IsValid()) return;

        TextureDesc desc = renderGraph.GetTextureDesc(srcColor);
        desc.filterMode = FilterMode.Point;
        TextureHandle dstColor = renderGraph.CreateTexture(desc);

        int pixelSize = (_sharedPixelationSettings != null) ? _sharedPixelationSettings.pixelSize : _settings.pixelSize;

        _material.SetFloat(PixelSizeId, pixelSize);
        _material.SetFloat(NormalBiasId, _settings.normalBias);
        _material.SetFloat(DepthThresholdId, _settings.depthThreshold);
        _material.SetFloat(BrightenAmountId, _settings.brightenAmount);
        _material.SetFloat(EdgeWidthId, _settings.edgeWidth);
        _material.SetFloat(ModulateByLuminanceId, _settings.modulateByLuminance ? 1f : 0f);
        _material.SetFloat(LuminanceContributionId, _settings.luminanceContribution);
        _material.SetFloat(MinLuminanceId, _settings.minLuminance);
        _material.SetFloat(MaxLuminanceId, _settings.maxLuminance);
        _material.SetFloat(UseDepthFilterId, _settings.useDepthFilter ? 1f : 0f);
        _material.SetFloat(UseStencilFilterId, _settings.useStencilFilter ? 1f : 0f);
        _material.SetInt(StencilReferenceId, _settings.stencilReference);
        _material.SetInt(StencilCompareId, (int)_settings.stencilCompare);

        if (_settings.normalPreview)
            _material.EnableKeyword("NORMAL_PREVIEW");
        else
            _material.DisableKeyword("NORMAL_PREVIEW");

        using (var builder = renderGraph.AddUnsafePass<PassData>("NormalEdge", out var passData))
        {
            passData.source = srcColor;
            passData.destination = dstColor;
            passData.normalTexture = normalTexture;
            passData.depthTexture = depthTexture;
            passData.material = _material;

            builder.UseTexture(srcColor, AccessFlags.Read);
            builder.UseTexture(normalTexture, AccessFlags.Read);
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
        material.SetTexture(NormalsTextureId, data.normalTexture);
        material.SetTexture(DepthTextureId, data.depthTexture);

        cmd.SetRenderTarget(data.destination);
        cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1);
    }

    private class PassData
    {
        public TextureHandle source;
        public TextureHandle destination;
        public TextureHandle normalTexture;
        public TextureHandle depthTexture;
        public Material material;
    }
}