using Irodori.Framebuffer;

namespace Verity.Graphics;

public abstract class RenderTargetBuilder
{
    public abstract RenderTargetBuilder WithColorAttachment(RenderTexture texture);
    public abstract RenderTarget Upload();
}

internal sealed class NativeRenderTargetBuilder : RenderTargetBuilder
{
    public NativeRenderTargetBuilder(FramebufferObject.Unuploaded resource)
    {
        Resource = resource;
    }

    internal FramebufferObject.Unuploaded Resource { get; }

    public override RenderTargetBuilder WithColorAttachment(RenderTexture texture)
    {
        Resource.WithColorAttachment(((NativeRenderTexture)texture).Resource);
        return this;
    }

    public override RenderTarget Upload()
    {
        return new NativeRenderTarget(Resource.Upload().Unwrap());
    }
}
