using UnityEngine;
using UnityEngine.Rendering.Universal;

public partial class ExclusionMaskFeature
{
    [System.Serializable]
    public class Settings
    {
        public LayerMask excludedLayers = 0;
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
    }
}
