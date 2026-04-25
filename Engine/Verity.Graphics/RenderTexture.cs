using Irodori.Texture;

namespace Verity.Graphics;

public abstract class RenderTexture : IDisposable
{
    public abstract int Width { get; }
    public abstract int Height { get; }
    public abstract void Dispose();
}

internal sealed class NativeRenderTexture : RenderTexture
{
    public NativeRenderTexture(TextureObjectUploaded resource)
    {
        Resource = resource;
    }

    internal TextureObjectUploaded Resource { get; }

    public override int Width => Resource.Width;
    public override int Height => Resource.Height;
    public override void Dispose() => Resource.Dispose();
}
