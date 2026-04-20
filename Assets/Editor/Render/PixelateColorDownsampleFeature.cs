using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PixelateRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Pixelation (local override)")]
        public bool enablePixelate = true;
        [Range(1, 64)] public int pixelSize = 8;

        [Header("Quantization")]
        public bool enableQuantization = false;
        // legacy colors – используются только если colorPaletteSO == null
        public Color[] colorPalette = new Color[] { Color.black, Color.white };
        public bool useLABDistance = true;

        [Header("Visibility")]
        public bool showInSceneView = false;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public Settings settings = new();

    [Tooltip("Общие настройки пикселизации (если назначены, переопределяют локальный pixelSize)")]
    public PixelationSettings sharedPixelationSettings;

    [Tooltip("Палитра цветов (если назначена, переопределяет локальный массив цветов)")]
    public ColorPaletteSO colorPaletteSO;

    private PixelateRenderPass _renderPass;
    private Material _pixelateMaterial;
    private Material _quantizeMaterial;

    public override void Create()
    {
        if (_pixelateMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/Pixelate");
            if (shader == null) shader = CreatePixelateShader();
            _pixelateMaterial = new Material(shader);
        }
        if (_quantizeMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/Quantize");
            if (shader == null) shader = CreateQuantizeShader();
            _quantizeMaterial = new Material(shader);
        }

        _renderPass = new PixelateRenderPass(_pixelateMaterial, _quantizeMaterial, settings, sharedPixelationSettings, colorPaletteSO)
        {
            renderPassEvent = settings.renderPassEvent
        };
    }

    protected override void Dispose(bool disposing)
    {
        _renderPass?.Dispose();
        if (_pixelateMaterial != null) CoreUtils.Destroy(_pixelateMaterial);
        if (_quantizeMaterial != null) CoreUtils.Destroy(_quantizeMaterial);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cameraType = renderingData.cameraData.cameraType;
        bool shouldRender = cameraType == CameraType.Game ||
                           (cameraType == CameraType.SceneView && settings.showInSceneView);
        if (shouldRender) renderer.EnqueuePass(_renderPass);
    }

    private Shader CreatePixelateShader()
    {
        const string code = @"
Shader ""Hidden/Pixelate""
{
    Properties { _BlitTexture (""Texture"", 2D) = ""white"" {} _PixelSize (""Pixel Size"", Float) = 8 }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" ""RenderPipeline""=""UniversalPipeline"" }
        LOD 100 ZWrite Off Cull Off
        Pass
        {
            Name ""Pixelate""
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_BlitTexture); SAMPLER(sampler_BlitTexture);
            float _PixelSize; float4 _BlitTexture_TexelSize;
            Varyings Vert(Attributes input) {
                Varyings o;
                o.pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return o;
            }
            float4 Frag(Varyings i) : SV_Target {
                float2 texelCount = _BlitTexture_TexelSize.zw;
                float2 stepUV = 1.0 / (texelCount / _PixelSize);
                float2 pixelatedUV = floor(i.uv / stepUV) * stepUV;
                pixelatedUV = min(pixelatedUV, 1.0 - stepUV);
                float2 sampleUV = pixelatedUV + stepUV * 0.5;
                float4 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, sampleUV);
                col.a = 1.0;
                return col;
            }
            ENDHLSL
        }
    }
}";
        return ShaderUtil.CreateShaderAsset(code);
    }

    private Shader CreateQuantizeShader()
    {
        const string code = @"
Shader ""Hidden/Quantize""
{
    Properties
    {
        _BlitTexture (""Texture"", 2D) = ""white"" {}
        _PaletteSize (""Palette Size"", Float) = 2
    }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" ""RenderPipeline""=""UniversalPipeline"" }
        LOD 100 ZWrite Off Cull Off
        Pass
        {
            Name ""Quantize""
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ DISTANCE_LAB_DELTAE
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_BlitTexture); SAMPLER(sampler_BlitTexture);
            float4 _BlitTexture_TexelSize;

            uniform float4 _PaletteColors[256];
            uniform float _PaletteSize;

            Varyings Vert(Attributes input) {
                Varyings o;
                o.pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return o;
            }

            #if defined(DISTANCE_LAB_DELTAE)
            float3 rgb2xyz(float3 rgb) {
                rgb = pow(rgb, 2.2);
                float3x3 m = float3x3(
                    0.4124564, 0.3575761, 0.1804375,
                    0.2126729, 0.7151522, 0.0721750,
                    0.0193339, 0.1191920, 0.9503041
                );
                return mul(m, rgb);
            }
            float3 xyz2lab(float3 xyz) {
                xyz /= float3(0.95047, 1.0, 1.08883);
                float3 f = xyz > 0.008856 ? pow(xyz, 1.0/3.0) : (7.787 * xyz + 16.0/116.0);
                float L = 116.0 * f.y - 16.0;
                float a = 500.0 * (f.x - f.y);
                float b = 200.0 * (f.y - f.z);
                return float3(L, a, b);
            }
            float deltaE(float3 lab1, float3 lab2) {
                float3 d = lab1 - lab2;
                return sqrt(dot(d,d));
            }
            #endif

            float4 Frag(Varyings i) : SV_Target {
                float4 original = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, i.uv);
                int paletteCount = (int)_PaletteSize;
                if (paletteCount <= 0) return original;

                float bestDist = 1e10;
                float3 bestColor = original.rgb;

                #if defined(DISTANCE_LAB_DELTAE)
                    float3 labOrig = xyz2lab(rgb2xyz(original.rgb));
                #endif

                for (int idx = 0; idx < paletteCount; idx++) {
                    float3 palColor = _PaletteColors[idx].rgb;

                    #if defined(DISTANCE_LAB_DELTAE)
                        float3 labPal = xyz2lab(rgb2xyz(palColor));
                        float dist = deltaE(labOrig, labPal);
                    #else
                        float dist = distance(original.rgb, palColor);
                    #endif

                    if (dist < bestDist) {
                        bestDist = dist;
                        bestColor = palColor;
                    }
                }
                return float4(bestColor, original.a);
            }
            ENDHLSL
        }
    }
}";
        return ShaderUtil.CreateShaderAsset(code);
    }
}