using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PixelateNormalEdgeFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Pixelation")]
        [Range(1, 64)] public int pixelSize = 8;

        [Header("Edge Detection")]
        [Range(0.01f, 0.5f)] public float normalBias = 0.3f;
        [Range(0.0001f, 0.01f)] public float depthThreshold = 0.001f;
        public bool useDepthFilter = true;

        [Header("Edge Rendering")]
        [Range(0f, 1f)] public float brightenAmount = 0.3f;
        [Tooltip("Толщина линии в пикселях (на экране, до пикселизации)")]
        [Range(1, 10)] public int edgeWidth = 1;

        [Header("Luminance Modulation (based on final pixel brightness)")]
        public bool modulateByLuminance = true;
        [Range(0f, 1f)] public float luminanceContribution = 1f;
        [Tooltip("Минимальная яркость пикселя, при которой линия начинает появляться")]
        [Range(0f, 1f)] public float minLuminance = 0.2f;
        [Tooltip("Максимальная яркость пикселя, при которой линия достигает полной яркости")]
        [Range(0f, 1f)] public float maxLuminance = 0.8f;

        [Header("Stencil Filtering (exclude objects)")]
        public bool useStencilFilter = false;
        [Range(0, 255)]
        public int stencilReference = 0;
        public CompareFunction stencilCompare = CompareFunction.Equal;

        [Header("Debug")]
        public bool normalPreview = false;
        public bool showInSceneView = false;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public Settings settings = new();
    public PixelationSettings sharedPixelationSettings;

    private PixelateNormalEdgePass _renderPass;
    private Material _material;

    public override void Create()
    {
        if (_material == null)
        {
            Shader shader = Shader.Find("Hidden/PixelateNormalEdge");
            if (shader == null) shader = CreateShader();
            _material = new Material(shader);
        }
        _renderPass = new PixelateNormalEdgePass(_material, settings, sharedPixelationSettings)
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
Shader ""Hidden/PixelateNormalEdge""
{
    Properties
    {
        _BlitTexture (""Texture"", 2D) = ""white"" {}
        _NormalsTexture (""Normals"", 2D) = ""white"" {}
        _DepthTexture (""Depth"", 2D) = ""white"" {}
        _PixelSize (""Pixel Size"", Float) = 8
        _NormalBias (""Normal Bias"", Float) = 0.3
        _DepthThreshold (""Depth Threshold"", Float) = 0.001
        _BrightenAmount (""Brighten Amount"", Float) = 0.3
        _EdgeWidth (""Edge Width"", Float) = 1
        _ModulateByLuminance (""Modulate by Luminance"", Float) = 1
        _LuminanceContribution (""Luminance Contribution"", Float) = 1
        _MinLuminance (""Min Luminance"", Float) = 0.2
        _MaxLuminance (""Max Luminance"", Float) = 0.8
        _UseDepthFilter (""Use Depth Filter"", Float) = 1
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
            Name ""NormalEdge""
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ NORMAL_PREVIEW
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_BlitTexture); SAMPLER(sampler_BlitTexture);
            TEXTURE2D(_NormalsTexture); SAMPLER(sampler_NormalsTexture);
            TEXTURE2D(_DepthTexture); SAMPLER(sampler_DepthTexture);
            float _PixelSize; float4 _BlitTexture_TexelSize;
            float _NormalBias; float _DepthThreshold; float _BrightenAmount;
            float _EdgeWidth; float _ModulateByLuminance; float _LuminanceContribution;
            float _MinLuminance; float _MaxLuminance; float _UseDepthFilter;
            float _UseStencilFilter; int _StencilReference; int _StencilCompare;

            Varyings Vert(Attributes input) {
                Varyings o;
                o.pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return o;
            }

            float3 GetNormal(float2 uv) {
                float3 n = SAMPLE_TEXTURE2D(_NormalsTexture, sampler_NormalsTexture, uv).xyz;
                return normalize(n * 2.0 - 1.0);
            }

            float GetDepth(float2 uv) {
                return SAMPLE_TEXTURE2D(_DepthTexture, sampler_DepthTexture, uv).r;
            }

            bool IsValidNormal(float3 n) {
                return dot(n, n) > 0.5;
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

            bool IsEdge(float2 uv, float2 texelSize) {
                float centerDepth = GetDepth(uv);
                if (centerDepth >= 0.9999) return false;

                float3 centerNormal = GetNormal(uv);
                if (!IsValidNormal(centerNormal)) return false;

                int radius = max(1, (int)_EdgeWidth);
                [loop]
                for (int dy = -radius; dy <= radius; dy++) {
                    [loop]
                    for (int dx = -radius; dx <= radius; dx++) {
                        if (dx == 0 && dy == 0) continue;
                        float2 offset = float2(dx * texelSize.x, dy * texelSize.y);
                        float2 neighborUV = uv + offset;
                        if (neighborUV.x < 0 || neighborUV.x > 1 || neighborUV.y < 0 || neighborUV.y > 1) continue;
                        float neighborDepth = GetDepth(neighborUV);
                        if (neighborDepth >= 0.9999) continue;
                        if (_UseDepthFilter > 0.5 && abs(centerDepth - neighborDepth) > _DepthThreshold) continue;
                        float3 neighborNormal = GetNormal(neighborUV);
                        if (!IsValidNormal(neighborNormal)) continue;
                        float normalDiff = 1.0 - saturate(dot(centerNormal, neighborNormal));
                        if (normalDiff > _NormalBias) return true;
                    }
                }
                return false;
            }

            float4 Frag(Varyings i) : SV_Target {
                // Проверка Stencil
                if (!StencilCompare(GetStencilValue(i.uv))) {
                    return SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, i.uv);
                }

                float4 original = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, i.uv);

                #if NORMAL_PREVIEW
                    float3 normal = GetNormal(i.uv);
                    float3 color = normal * 0.5 + 0.5;
                    return float4(color, 1.0);
                #else
                    float2 texelCount = _BlitTexture_TexelSize.zw;
                    float2 stepUV = 1.0 / (texelCount / _PixelSize);
                    float2 pixelatedUV = floor(i.uv / stepUV) * stepUV;
                    pixelatedUV = min(pixelatedUV, 1.0 - stepUV);
                    float2 centerUV = pixelatedUV + stepUV * 0.5;

                    float2 texelSize = _BlitTexture_TexelSize.xy;
                    bool isEdge = IsEdge(centerUV, texelSize);

                    float4 result = original;
                    if (isEdge) {
                        float luminance = GetLuminance(original.rgb);
                        float t = saturate((luminance - _MinLuminance) / (_MaxLuminance - _MinLuminance));
                        float addAmount = _BrightenAmount;
                        if (_ModulateByLuminance > 0.5) {
                            float modulation = lerp(1.0, t, _LuminanceContribution);
                            addAmount *= modulation;
                        } else {
                            addAmount *= t;
                        }
                        result.rgb += addAmount;
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