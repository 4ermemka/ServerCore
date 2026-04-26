using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class ExclusionMaskFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public LayerMask excludedLayers = 0;
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
    }

    public Settings settings = new();
    private ExclusionMaskPass _renderPass;

    public override void Create()
    {
        _renderPass = new ExclusionMaskPass(settings) { renderPassEvent = settings.renderPassEvent };
    }

    protected override void Dispose(bool disposing)
    {
        _renderPass?.Dispose();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.excludedLayers != 0)
            renderer.EnqueuePass(_renderPass);
    }
}

public class ExclusionMaskPass : ScriptableRenderPass
{
    private ExclusionMaskFeature.Settings _settings;
    private Material _material;
    private static readonly int ExclusionMaskId = Shader.PropertyToID("_ExclusionMask");
    private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");

    public ExclusionMaskPass(ExclusionMaskFeature.Settings settings)
    {
        _settings = settings;
        profilingSampler = new ProfilingSampler(nameof(ExclusionMaskPass));
        _material = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/Internal-ClearToWhite"));
        if (_material == null)
        {
            // Создаём простой шейдер, рисующий белый цвет
            Shader shader = ShaderUtil.CreateShaderAsset(@"
Shader ""Hidden/Internal-ClearToWhite""
{
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input) {
                Varyings o;
                o.pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return o;
            }
            float4 Frag(Varyings i) : SV_Target { return 1.0; }
            ENDHLSL
        }
    }
}");
            _material = new Material(shader);
        }
    }

    public void Dispose() { CoreUtils.Destroy(_material); }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_material == null) return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        if (resourceData == null || cameraData == null) return;

        // Создаём текстуру маски (один раз на кадр)
        TextureDesc desc = new TextureDesc(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height)
        {
            colorFormat = GraphicsFormat.R8_SRGB,
            clearBuffer = true,
            clearColor = Color.black,
            filterMode = FilterMode.Point,
            name = "_ExclusionMask"
        };
        TextureHandle maskTexture = renderGraph.CreateTexture(desc);

        // Рендерим только объекты на исключённых слоях, заливая белым
        // Используем RTHandles? В RenderGraph нужно передать маску слоёв через параметры рендера
        // Упрощённо: используем AddUnsafePass и рисуем fullscreen quad с проверкой глубины
        // Но поскольку объекты на исключённых слоях уже отрендерены в цветовую текстуру, мы можем просто скопировать их в маску?
        // Нет, нам нужна именно маска, где белым помечены все пиксели исключённых объектов.

        // Самый простой способ: создать временный RenderTexture, отрендерить сцену с маской слоёв и culling mask,
        // затем объединить. Но это тяжело.

        // Вместо этого используем подход: создаём маску, очищаем чёрным, затем рендерим все объекты на исключённых слоях через отдельный DrawSettings
        // В RenderGraph для этого нужно создать отдельный проход с собственным контекстом рендеринга.
        // Сложность: в URP 17+ RenderGraph не поддерживает прямой рендеринг объектов по слоям.

        // Альтернатива (предлагаемая): пользователь сам может настроить глобальную текстуру маски через Custom Pass.
        // Вместо сложной реализации внутри фичи, дадим пользователю простой инструкцию:
        // "Создайте Render Objects Feature, который рендерит объекты на указанных слоях в отдельный RenderTexture,
        // и затем используйте этот RT в наших шейдерах через глобальную переменную _ExclusionMask"

        // Однако, чтобы ответ был полным, я реализую упрощённый вариант: маска создаётся путём копирования буфера глубины,
        // где объекты на исключённых слоях имеют определённый stencil. Но stencil нужно ещё настроить.

        // Наиболее практичное решение: **добавить в каждую фичу параметр LayerMask excludedLayers,
        // и в шейдере эффекта проверять принадлежность пикселя к этим слоям**, но для доступа к слою нужна отдельная текстура.

        // Пойдём по пути создания маски через дополнительный проход рендеринга с Camera.Render с изменённой cullingMask.
        // Это потребовало бы отдельную камеру, что неоптимально.

        // Учитывая сложность полной реализации в рамках ответа, я предлагаю простой и эффективный метод:
        // **Фильтрация по материалам через Stencil Buffer**, который настраивается в материалах объектов.

        // Далее представлю реализацию фильтрации через Stencil – это надёжно, гибко и не требует дополнительных текстур.
    }
}