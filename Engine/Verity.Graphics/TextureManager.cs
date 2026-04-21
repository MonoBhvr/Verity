using Irodori.Texture;
using StbImageSharp;
using System.Linq;
using Verity.Core.Collections;
using Verity.Core.World;

namespace Verity.Graphics;

public class TextureManager : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly LruCache<string, TextureObjectUploaded> _cache;

    public TextureManager(GraphicsDevice device)
    {
        _device = device;
        _cache = new LruCache<string, TextureObjectUploaded>(256);
    }

    public TextureObjectUploaded Load(string path, SpriteTextureFilter filter = SpriteTextureFilter.Point, bool flipY = true)
    {
        var fullPath = Path.GetFullPath(path);
        string cacheKey = BuildCacheKey(fullPath, filter, flipY);
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        using var stream = File.OpenRead(fullPath);
        var imageResult = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        var pixels = flipY ? FlipImageY(imageResult.Data, imageResult.Width, imageResult.Height) : imageResult.Data;
        var uploaded = UploadPixels(pixels, imageResult.Width, imageResult.Height, filter);
        _cache.Set(cacheKey, uploaded);
        return uploaded;
    }

    public TextureObjectUploaded LoadFromMemory(byte[] imageBytes, string cacheKey, SpriteTextureFilter filter = SpriteTextureFilter.Point, bool flipY = true)
    {
        string actualCacheKey = BuildCacheKey(cacheKey, filter, flipY);
        if (_cache.TryGetValue(actualCacheKey, out var cached))
            return cached;

        var imageResult = ImageResult.FromMemory(imageBytes, ColorComponents.RedGreenBlueAlpha);
        var pixels = flipY ? FlipImageY(imageResult.Data, imageResult.Width, imageResult.Height) : imageResult.Data;
        var uploaded = UploadPixels(pixels, imageResult.Width, imageResult.Height, filter);
        _cache.Set(actualCacheKey, uploaded);
        return uploaded;
    }

    public unsafe TextureObjectUploaded CreateFromRgba(byte[] pixels, int width, int height, string? cacheKey = null, SpriteTextureFilter filter = SpriteTextureFilter.Point)
    {
        string? actualCacheKey = cacheKey != null ? BuildCacheKey(cacheKey, filter, flipY: false) : null;
        if (actualCacheKey != null && _cache.TryGetValue(actualCacheKey, out var cached))
            return cached;

        var uploaded = UploadPixels(pixels, width, height, filter);

        if (actualCacheKey != null)
            _cache.Set(actualCacheKey, uploaded);

        return uploaded;
    }

    private unsafe TextureObjectUploaded UploadPixels(byte[] pixels, int width, int height, SpriteTextureFilter filter)
    {
        fixed (byte* ptr = pixels)
        {
            var textureData = TextureData.Create(ptr);
            var textureFilter = filter == SpriteTextureFilter.Linear ? ETextureFilter.Linear : ETextureFilter.Nearest;
            return _device.CreateTexture()
                .WithSize(width, height)
                .WithTextureType(ETextureInternalType.Rgba8)
                .WithFilter(textureFilter, textureFilter)
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
        var keys = _cache.Keys.Where(key => key.StartsWith(fullPath + "|", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var key in keys)
        {
            if (_cache.TryGetValue(key, out var tex) && _cache.Remove(key))
                tex.Dispose();
        }
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    private static string BuildCacheKey(string baseKey, SpriteTextureFilter filter, bool flipY) => $"{baseKey}|{filter}|flip:{flipY}";
}
