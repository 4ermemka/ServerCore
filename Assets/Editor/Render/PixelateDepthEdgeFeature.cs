using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PixelateDepthEdgeFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("Локальный размер пикселя (игнорируется, если назначен общий PixelationSettings)")]
        [Range(1, 64)]
        public int pixelSize = 8;

        [Tooltip("Порог разницы глубины (0.0001–0.01)")]
        [Range(0.0001f, 0.01f)]
        public float depthBias = 0.005f;

        [Range(0f, 1f)]
        public float darkenAmount = 0.3f;

        public EdgeSide edgeSide = EdgeSide.Foreground;
        public bool depthPreview = false;

        [Range(0.1f, 10f)]
        public float depthContrast = 1f;

        [Range(-1f, 1f)]
        public float depthBrightness = 0f;

        [Header("Lighting Modulation")]
        public bool modulateByLighting = true;
        [Range(0f, 1f)]
        public float lightingModulationStrength = 1f;

        public bool showInSceneView = false;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public enum EdgeSide { Foreground, Background }

    public Settings settings = new();

    [Tooltip("Общие настройки пикселизации (переопределяют локальный pixelSize)")]
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
        _DepthBias (""Depth Bias"", Float) = 0.005
        _DarkenAmount (""Darken Amount"", Float) = 0.3
        _EdgeSide (""Edge Side"", Float) = 0
        _DepthContrast (""Depth Contrast"", Float) = 1
        _DepthBrightness (""Depth Brightness"", Float) = 0
        _ModulateByLighting (""Modulate by Lighting"", Float) = 1
        _LightingStrength (""Lighting Strength"", Float) = 1
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
            float _DepthBias; float _DarkenAmount; float _EdgeSide;
            float _DepthContrast; float _DepthBrightness;
            float _ModulateByLighting; float _LightingStrength;

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

            float4 Frag(Varyings i) : SV_Target {
                float4 original = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, i.uv);

                #if DEPTH_PREVIEW
                    float depth = GetDepth(i.uv);
                    float gray = depth * _DepthContrast + _DepthBrightness;
                    gray = saturate(gray);
                    gray = 1.0 - gray;
                    return float4(gray.xxx, 1.0);
                #else
                    float2 texelCount = _BlitTexture_TexelSize.zw;
                    float2 stepUV = 1.0 / (texelCount / _PixelSize);
                    float2 pixelatedUV = floor(i.uv / stepUV) * stepUV;
                    pixelatedUV = min(pixelatedUV, 1.0 - stepUV);
                    float2 centerUV = pixelatedUV + stepUV * 0.5;

                    float centerDepth = GetDepth(centerUV);
                    if (centerDepth >= 0.9999) return original;

                    bool isEdge = false;
                    float2 offsets[4] = {
                        float2(stepUV.x, 0), float2(-stepUV.x, 0),
                        float2(0, stepUV.y), float2(0, -stepUV.y)
                    };

                    for (int j = 0; j < 4; j++) {
                        float2 neighborUV = centerUV + offsets[j];
                        float neighborDepth = GetDepth(neighborUV);
                        float depthDiff = neighborDepth - centerDepth;

                        if ((int)_EdgeSide == 0) {
                            if (depthDiff > _DepthBias) { isEdge = true; break; }
                        } else {
                            if (-depthDiff > _DepthBias) { isEdge = true; break; }
                        }
                    }

                    float4 result = original;
                    if (isEdge) {
                        float luminance = GetLuminance(original.rgb);
                        float modulation = _ModulateByLighting > 0.5 ? lerp(1.0, luminance, _LightingStrength) : 1.0;
                        float darken = _DarkenAmount * modulation;
                        result.rgb *= (1.0 - darken);
                    }
                    return result;
                #endif
            }
            ENDHLSL
        }
    }
}";
        return ShaderUtil.CreateShaderAsset(code);
    }
}