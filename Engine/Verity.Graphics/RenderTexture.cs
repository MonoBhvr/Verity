using Irodori.Texture;
using Irodori.Backend.OpenGL;

namespace Verity.Graphics;

public abstract class RenderTexture : IDisposable
{
    public abstract int Width { get; }
    public abstract int Height { get; }
    public abstract nint ImGuiTextureId { get; }
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
    public override nint ImGuiTextureId => Resource is OpenGlTexture glTexture ? (nint)glTexture.Id : 0;
    public override void Dispose() => Resource.Dispose();
}
