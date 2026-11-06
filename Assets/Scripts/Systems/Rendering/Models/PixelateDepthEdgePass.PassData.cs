using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

public partial class PixelateDepthEdgePass
{
    private class PassData
    {
        public TextureHandle source;
        public TextureHandle destination;
        public TextureHandle depthTexture;
        public Material material;
    }
}
