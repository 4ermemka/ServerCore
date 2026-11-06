using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

public partial class PixelateNormalEdgePass
{
    private class PassData
    {
        public TextureHandle source;
        public TextureHandle destination;
        public TextureHandle normalTexture;
        public TextureHandle depthTexture;
        public Material material;
    }
}
