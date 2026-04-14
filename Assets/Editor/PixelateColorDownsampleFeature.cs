using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PixelateRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Range(1, 64)] public int pixelSize = 8;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        public bool showInSceneView = false; // Новый параметр для управления отображением в сцене
    }

    public Settings settings = new();
    private PixelateRenderPass _renderPass;
    private Material _pixelateMaterial;

    public override void Create()
    {
        if (_pixelateMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/Pixelate");
            if (shader == null)
                shader = CreatePixelateShader();
            _pixelateMaterial = new Material(shader);
        }

        _renderPass = new PixelateRenderPass(_pixelateMaterial, settings)
        {
            renderPassEvent = settings.renderPassEvent
        };
    }

    protected override void Dispose(bool disposing)
    {
        _renderPass?.Dispose();
        if (_pixelateMaterial != null)
            CoreUtils.Destroy(_pixelateMaterial);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Не добавляем проход для камеры сцены, если showInSceneView = false
        if (!settings.showInSceneView && renderingData.cameraData.cameraType == CameraType.SceneView)
            return;

        if (renderingData.cameraData.cameraType == CameraType.Game ||
            renderingData.cameraData.cameraType == CameraType.SceneView)
            renderer.EnqueuePass(_renderPass);
    }

    private Shader CreatePixelateShader()
    {
        const string shaderCode = @"
Shader ""Hidden/Pixelate""
{
    Properties
    {
        _BlitTexture (""Texture"", 2D) = ""white"" {}
        _PixelSize (""Pixel Size"", Float) = 8
    }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" ""RenderPipeline"" = ""UniversalPipeline"" }
        LOD 100
        ZWrite Off
        Cull Off

        Pass
        {
            Name ""Pixelate""

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            float _PixelSize;
            float4 _BlitTexture_TexelSize;

            // Определяем сэмплер с точечной фильтрацией для жестких пикселей
            SamplerState sampler_point_clamp
            {
                Filter = MIN_MAG_MIP_POINT;
                AddressU = Clamp;
                AddressV = Clamp;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 texelCount = _BlitTexture_TexelSize.zw;
                float2 stepUV = 1.0 / (texelCount / _PixelSize);
                float2 pixelatedUV = floor(input.uv / stepUV) * stepUV;
                pixelatedUV = min(pixelatedUV, 1.0 - stepUV);
                float2 sampleUV = pixelatedUV + stepUV * 0.5;
                // Используем точечную выборку для жестких границ
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_point_clamp, sampleUV);
            }
            ENDHLSL
        }
    }
}
        ";
        return ShaderUtil.CreateShaderAsset(shaderCode);
    }
}