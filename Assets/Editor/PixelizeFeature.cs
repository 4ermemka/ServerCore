using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using static UnityEngine.Rendering.RenderGraphModule.Util.RenderGraphUtils;

public class PixelateRendererFeature : ScriptableRendererFeature
{
    [Range(1, 16)]
    public int pixelDensity = 4;

    // Шейдер пикселизации, встроенный прямо в код
    private const string PIXELATE_SHADER_CODE = @"
    Shader ""Custom/PixelShader""
    {   
        SubShader
        {
            HLSLINCLUDE
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
            #include ""Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl""
            ENDHLSL
    
            Tags { ""RenderType""=""Opaque"" }
            LOD 100
            ZWrite Off Cull Off
            Pass
            {
                Name ""PixelShader""
    
                HLSLPROGRAM
                
                #pragma vertex Vert
                #pragma fragment Frag
    
                float4 Frag (Varyings input) : SV_Target
                {
                    float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.texcoord).rgba;
                    return color;
                }
                
                ENDHLSL
            }
        }
    }
    ";

    class PixelateRenderPass : ScriptableRenderPass
    {
        private readonly Material material;
        private readonly int pixelDensity;

        public PixelateRenderPass(Material pixelateMaterial, int density)
        {
            material = pixelateMaterial;
            pixelDensity = Mathf.Max(1, density);
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            var desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.width = Mathf.Max(1, cameraData.camera.pixelWidth / pixelDensity);
            desc.height = Mathf.Max(1, cameraData.camera.pixelHeight / pixelDensity);

            TextureHandle pixelatedTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_PixelatedTex", false);

            var blitParams1 = new BlitMaterialParameters(
                resourceData.activeColorTexture,
                pixelatedTexture,
                material,
                0);
            renderGraph.AddBlitPass(blitParams1, "Pixelate Pass");

            var blitParams2 = new BlitMaterialParameters(
                pixelatedTexture,
                resourceData.cameraColor,
                Blitter.GetBlitMaterial(TextureDimension.Tex2D),
                0);
            renderGraph.AddBlitPass(blitParams2, "Copy Back to Camera");
        }
    }

    // Кэш для созданного материала
    private Material m_PixelateMaterial;

    public override void Create()
    {
        // Создаём шейдер из строкового кода и материал из него
        Shader pixelateShader = Shader.Find("Hidden/Pixelate");
        if (pixelateShader == null)
        {
            pixelateShader = ShaderUtil.CreateShaderAsset(PIXELATE_SHADER_CODE);
        }
        m_PixelateMaterial = new Material(pixelateShader);
        m_PixelateMaterial.hideFlags = HideFlags.HideAndDontSave; // Чтобы не засорять проект
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.SceneView)
            return;

        if (m_PixelateMaterial == null)
        {
            Create(); // На всякий случай пересоздадим
        }

        var pass = new PixelateRenderPass(m_PixelateMaterial, pixelDensity);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        // Чистим за собой материал при удалении фичи
        if (m_PixelateMaterial != null)
        {
            DestroyImmediate(m_PixelateMaterial);
        }
    }
}