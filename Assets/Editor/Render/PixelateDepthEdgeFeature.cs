using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PixelateDepthEdgeFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("Размер пикселя (используется, если не задан общий PixelationSettings)")]
        [Range(1, 64)]
        public int pixelSize = 8;

        [Header("Depth Difference")]
        [Tooltip("Минимальная разница глубины (0..1) для начала затемнения")]
        [Range(0.0f, 1.0f)]
        public float minDepthDifference = 0.002f;
        [Tooltip("Максимальная разница глубины (0..1) – при ней достигается максимальное затемнение")]
        [Range(0.0f, 1.0f)]
        public float maxDepthDifference = 0.01f;

        [Header("Darkening")]
        [Tooltip("Минимальное затемнение (0 = без изменений)")]
        [Range(0f, 1f)]
        public float minDarkening = 0f;
        [Tooltip("Максимальное затемнение (1 = полностью чёрный)")]
        [Range(0f, 1f)]
        public float maxDarkening = 0.5f;

        [Header("Edge Side")]
        public EdgeSide edgeSide = EdgeSide.Foreground; // Foreground = граница объекта на фоне, Background = фон перед объектом

        [Header("Luminance Modulation (optional)")]
        public bool modulateByLuminance = false; // теперь опционально, можно отключить
        [Range(0f, 1f)]
        public float luminanceModulationStrength = 1f;
        [Range(0f, 1f)]
        public float minLuminance = 0.2f;
        [Range(0f, 1f)]
        public float maxLuminance = 0.8f;

        [Header("Stencil Filtering (exclude objects)")]
        public bool useStencilFilter = false;
        [Range(0, 255)]
        public int stencilReference = 0;
        public CompareFunction stencilCompare = CompareFunction.Equal;

        [Header("Debug")]
        public bool depthPreview = false;
        public bool showInSceneView = false;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public enum EdgeSide { Foreground, Background }

    public Settings settings = new();
    public PixelationSettings sharedPixelationSettings;

    private PixelateDepthEdgePass _renderPass;
    private Material _material;

    public override void Create()
    {
        if (_material == null)
        {
            Shader shader = Shader.Find("Hidden/PixelateDepthEdge");
            if (shader == null) shader = CreateShader();
            _material = new Material(shader);
        }
        _renderPass = new PixelateDepthEdgePass(_material, settings, sharedPixelationSettings)
        {
            renderPassEvent = settings.renderPassEvent
        };
    }

    protected override void Dispose(bool disposing)
    {
        _renderPass?.Dispose();
        if (_material != null) CoreUtils.Destroy(_material);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cameraType = renderingData.cameraData.cameraType;
        bool shouldRender = cameraType == CameraType.Game ||
                           (cameraType == CameraType.SceneView && settings.showInSceneView);
        if (shouldRender) renderer.EnqueuePass(_renderPass);
    }

    private Shader CreateShader()
    {
        const string code = @"
Shader ""Hidden/PixelateDepthEdge""
{
    Properties
    {
        _BlitTexture (""Texture"", 2D) = ""white"" {}
        _DepthTexture (""Depth"", 2D) = ""white"" {}
        _PixelSize (""Pixel Size"", Float) = 8
        _MinDepthDiff (""Min Depth Difference"", Float) = 0.002
        _MaxDepthDiff (""Max Depth Difference"", Float) = 0.01
        _MinDarkening (""Min Darkening"", Float) = 0
        _MaxDarkening (""Max Darkening"", Float) = 0.5
        _EdgeSide (""Edge Side"", Float) = 0
        _ModulateByLuminance (""Modulate by Luminance"", Float) = 0
        _LuminanceModStrength (""Luminance Modulation Strength"", Float) = 1
        _MinLuminance (""Min Luminance"", Float) = 0.2
        _MaxLuminance (""Max Luminance"", Float) = 0.8
        _UseStencilFilter (""Use Stencil Filter"", Float) = 0
        _StencilReference (""Stencil Reference"", Int) = 0
        _StencilCompare (""Stencil Compare"", Int) = 2
    }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" ""RenderPipeline""=""UniversalPipeline"" }
        LOD 100 ZWrite Off Cull Off
        Pass
        {
            Name ""DepthEdge""
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ DEPTH_PREVIEW
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_BlitTexture); SAMPLER(sampler_BlitTexture);
            TEXTURE2D(_DepthTexture); SAMPLER(sampler_DepthTexture);
            float _PixelSize; float4 _BlitTexture_TexelSize;
            float _MinDepthDiff; float _MaxDepthDiff;
            float _MinDarkening; float _MaxDarkening;
            float _EdgeSide;
            float _ModulateByLuminance; float _LuminanceModStrength;
            float _MinLuminance; float _MaxLuminance;
            float _UseStencilFilter; int _StencilReference; int _StencilCompare;

            Varyings Vert(Attributes input) {
                Varyings o;
                o.pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return o;
            }

            float GetDepth(float2 uv) {
                return SAMPLE_TEXTURE2D(_DepthTexture, sampler_DepthTexture, uv).r;
            }

            float GetLuminance(float3 color) {
                return dot(color, float3(0.2126, 0.7152, 0.0722));
            }

            float GetStencilValue(float2 uv) {
                return SAMPLE_TEXTURE2D(_DepthTexture, sampler_DepthTexture, uv).y * 255.0;
            }

            bool StencilCompare(float stencilVal) {
                if (_UseStencilFilter < 0.5) return true;
                int s = (int)round(stencilVal);
                if (_StencilCompare == 0) return true;
                if (_StencilCompare == 1) return false;
                if (_StencilCompare == 2) return s == _StencilReference;
                if (_StencilCompare == 3) return s != _StencilReference;
                return true;
            }

            // Возвращает минимальную глубину в блоке (ближайшую к камере)
            float GetBlockDepth(float2 blockMin, float2 stepUV) {
                float2 halfStep = stepUV * 0.5;
                float2 corners[4] = {
                    blockMin,
                    blockMin + float2(stepUV.x, 0),
                    blockMin + float2(0, stepUV.y),
                    blockMin + stepUV
                };
                float minDepth = 1.0;
                for (int i = 0; i < 4; i++) {
                    float d = GetDepth(corners[i]);
                    if (d < minDepth) minDepth = d;
                }
                return minDepth;
            }

            float4 Frag(Varyings i) : SV_Target {
                if (!StencilCompare(GetStencilValue(i.uv))) {
                    return SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, i.uv);
                }

                float4 original = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, i.uv);

                #if DEPTH_PREVIEW
                    float depth = GetDepth(i.uv);
                    float gray = saturate(depth);
                    return float4(gray.xxx, 1.0);
                #else
                    // Пикселизация: определяем границы блока
                    float2 texelCount = _BlitTexture_TexelSize.zw;
                    float2 stepUV = 1.0 / (texelCount / _PixelSize);
                    float2 blockMin = floor(i.uv / stepUV) * stepUV;
                    float2 blockCenter = blockMin + stepUV * 0.5;

                    // Цвет блока (из центра)
                    float4 blockColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, blockCenter);

                    // Глубина текущего блока (минимальная по 4 углам)
                    float blockDepth = GetBlockDepth(blockMin, stepUV);
                    if (blockDepth >= 0.9999) return blockColor;  // фон

                    // Глубины соседних блоков
                    float2 stepX = float2(stepUV.x, 0);
                    float2 stepY = float2(0, stepUV.y);
                    float leftDepth = GetBlockDepth(blockMin - stepX, stepUV);
                    float rightDepth = GetBlockDepth(blockMin + stepX, stepUV);
                    float downDepth = GetBlockDepth(blockMin - stepY, stepUV);
                    float upDepth = GetBlockDepth(blockMin + stepY, stepUV);

                    // Максимальная разница глубины с учётом EdgeSide
                    float maxDiff = 0.0;
                    int side = (int)_EdgeSide;
                    float diffs[4] = {
                        (side == 0) ? (blockDepth - leftDepth) : (leftDepth - blockDepth),
                        (side == 0) ? (blockDepth - rightDepth) : (rightDepth - blockDepth),
                        (side == 0) ? (blockDepth - downDepth) : (downDepth - blockDepth),
                        (side == 0) ? (blockDepth - upDepth) : (upDepth - blockDepth)
                    };
                    for (int i = 0; i < 4; i++) {
                        if (diffs[i] > 0.0 && diffs[i] > maxDiff) maxDiff = diffs[i];
                    }

                    // Интенсивность затемнения
                    float darken = 0.0;
                    if (maxDiff > 0.0) {
                        float t = (maxDiff - _MinDepthDiff) / (_MaxDepthDiff - _MinDepthDiff);
                        t = saturate(t);
                        darken = lerp(_MinDarkening, _MaxDarkening, t);
                    }

                    // Модуляция яркостью (опционально)
                    if (_ModulateByLuminance > 0.5) {
                        float lum = GetLuminance(blockColor.rgb);
                        float lumT = saturate((lum - _MinLuminance) / (_MaxLuminance - _MinLuminance));
                        float lumMod = lerp(1.0, lumT, _LuminanceModStrength);
                        darken *= lumMod;
                    }

                    float3 finalColor = blockColor.rgb * (1.0 - darken);
                    return float4(finalColor, blockColor.a);
                #endif
            }
            ENDHLSL
        }
    }
}";
        return ShaderUtil.CreateShaderAsset(code);
    }
}