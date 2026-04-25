using Irodori.Framebuffer;

namespace Verity.Graphics;

public abstract class RenderTarget : IDisposable
{
    public abstract void Dispose();
}

internal sealed class NativeRenderTarget : RenderTarget
{
    public NativeRenderTarget(FramebufferObject.Uploaded resource)
    {
        Resource = resource;
    }

    internal FramebufferObject.Uploaded Resource { get; }
    public override void Dispose() => Resource.Dispose();
}
