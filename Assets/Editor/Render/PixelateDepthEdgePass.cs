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
    private static readonly int MinDepthDiffId = Shader.PropertyToID("_MinDepthDiff");
    private static readonly int MaxDepthDiffId = Shader.PropertyToID("_MaxDepthDiff");
    private static readonly int MinDarkeningId = Shader.PropertyToID("_MinDarkening");
    private static readonly int MaxDarkeningId = Shader.PropertyToID("_MaxDarkening");
    private static readonly int EdgeSideId = Shader.PropertyToID("_EdgeSide");
    private static readonly int ModulateByLuminanceId = Shader.PropertyToID("_ModulateByLuminance");
    private static readonly int LuminanceModStrengthId = Shader.PropertyToID("_LuminanceModStrength");
    private static readonly int MinLuminanceId = Shader.PropertyToID("_MinLuminance");
    private static readonly int MaxLuminanceId = Shader.PropertyToID("_MaxLuminance");
    private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
    private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
    private static readonly int UseStencilFilterId = Shader.PropertyToID("_UseStencilFilter");
    private static readonly int StencilReferenceId = Shader.PropertyToID("_StencilReference");
    private static readonly int StencilCompareId = Shader.PropertyToID("_StencilCompare");

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
        _material.SetFloat(MinDepthDiffId, _settings.minDepthDifference);
        _material.SetFloat(MaxDepthDiffId, _settings.maxDepthDifference);
        _material.SetFloat(MinDarkeningId, _settings.minDarkening);
        _material.SetFloat(MaxDarkeningId, _settings.maxDarkening);
        _material.SetFloat(EdgeSideId, (float)_settings.edgeSide);
        _material.SetFloat(ModulateByLuminanceId, _settings.modulateByLuminance ? 1f : 0f);
        _material.SetFloat(LuminanceModStrengthId, _settings.luminanceModulationStrength);
        _material.SetFloat(MinLuminanceId, _settings.minLuminance);
        _material.SetFloat(MaxLuminanceId, _settings.maxLuminance);
        _material.SetFloat(UseStencilFilterId, _settings.useStencilFilter ? 1f : 0f);
        _material.SetInt(StencilReferenceId, _settings.stencilReference);
        _material.SetInt(StencilCompareId, (int)_settings.stencilCompare);

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