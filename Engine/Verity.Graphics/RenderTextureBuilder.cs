using Irodori.Texture;

namespace Verity.Graphics;

public abstract class RenderTextureBuilder
{
    public abstract RenderTextureBuilder WithSize(int width, int height);
    public abstract RenderTextureBuilder WithRgba8();
    public abstract RenderTextureBuilder WithFilter(RenderTextureFilter filter);
    public abstract RenderTexture UploadRgba(byte[] pixels);
    public abstract RenderTexture UploadEmpty();
}

internal sealed class NativeRenderTextureBuilder : RenderTextureBuilder
{
    public NativeRenderTextureBuilder(TextureObjectUnuploaded resource)
    {
        Resource = resource;
    }

    internal TextureObjectUnuploaded Resource { get; }

    public override RenderTextureBuilder WithSize(int width, int height)
    {
        Resource.WithSize(width, height);
        return this;
    }

    public override RenderTextureBuilder WithRgba8()
    {
        Resource.WithTextureType(ETextureInternalType.Rgba8);
        return this;
    }

    public override RenderTextureBuilder WithFilter(RenderTextureFilter filter)
    {
        var textureFilter = filter == RenderTextureFilter.Linear ? ETextureFilter.Linear : ETextureFilter.Nearest;
        Resource.WithFilter(textureFilter, textureFilter);
        return this;
    }

    public override unsafe RenderTexture UploadRgba(byte[] pixels)
    {
        fixed (byte* ptr = pixels)
        {
            return new NativeRenderTexture(Resource.Upload(TextureData.Create(ptr)).Unwrap());
        }
    }

    public override unsafe RenderTexture UploadEmpty()
    {
        return new NativeRenderTexture(Resource.Upload(TextureData.Create((void*)null)).Unwrap());
    }
}
