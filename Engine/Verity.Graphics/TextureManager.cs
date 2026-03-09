using Irodori.Texture;
using StbImageSharp;

namespace Verity.Graphics;

public class TextureManager : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Dictionary<string, TextureObjectUploaded> _cache = new();

    public TextureManager(GraphicsDevice device)
    {
        _device = device;
    }

    public TextureObjectUploaded Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (_cache.TryGetValue(fullPath, out var cached))
            return cached;

        using var stream = File.OpenRead(fullPath);
        var imageResult = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        var pixels = FlipImageY(imageResult.Data, imageResult.Width, imageResult.Height);
        var uploaded = UploadPixels(pixels, imageResult.Width, imageResult.Height);
        _cache[fullPath] = uploaded;
        return uploaded;
    }

    public TextureObjectUploaded LoadFromMemory(byte[] imageBytes, string cacheKey)
    {
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var imageResult = ImageResult.FromMemory(imageBytes, ColorComponents.RedGreenBlueAlpha);
        var pixels = FlipImageY(imageResult.Data, imageResult.Width, imageResult.Height);
        var uploaded = UploadPixels(pixels, imageResult.Width, imageResult.Height);
        _cache[cacheKey] = uploaded;
        return uploaded;
    }

    public unsafe TextureObjectUploaded CreateFromRgba(byte[] pixels, int width, int height, string? cacheKey = null)
    {
        if (cacheKey != null && _cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var uploaded = UploadPixels(pixels, width, height);

        if (cacheKey != null)
            _cache[cacheKey] = uploaded;

        return uploaded;
    }

    private unsafe TextureObjectUploaded UploadPixels(byte[] pixels, int width, int height)
    {
        fixed (byte* ptr = pixels)
        {
            var textureData = TextureData.Create(ptr);
            return _device.CreateTexture()
                .WithSize(width, height)
                .WithTextureType(ETextureInternalType.Rgba8)
                .WithFilter(ETextureFilter.Nearest, ETextureFilter.Nearest)
                .Upload(textureData)
                .Unwrap();
        }
    }

    private static byte[] FlipImageY(byte[] data, int width, int height)
    {
        byte[] flipped = new byte[data.Length];
        int stride = width * 4;
        for (int y = 0; y < height; y++)
        {
            Array.Copy(data, y * stride, flipped, (height - 1 - y) * stride, stride);
        }
        return data.Length > 0 ? flipped : data;
    }

    public TextureObjectUploaded CreateWhitePixel()
    {
        return CreateFromRgba([255, 255, 255, 255], 1, 1, "__white_pixel__");
    }

    public (byte[] Pixels, int Width, int Height) GetRawPixels(string path, bool flipY = false)
    {
        using var stream = File.OpenRead(Path.GetFullPath(path));
        var imageResult = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        var pixels = flipY ? FlipImageY(imageResult.Data, imageResult.Width, imageResult.Height) : imageResult.Data;
        return (pixels, imageResult.Width, imageResult.Height);
    }

    public void Unload(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (_cache.Remove(fullPath, out var tex))
            tex.Dispose();
    }

    public void Dispose()
    {
        foreach (var tex in _cache.Values)
            tex.Dispose();
        _cache.Clear();
    }
}
