using Irodori.Texture;

namespace Verity.Graphics;

public static class DefaultSprites
{
    public const string SquareKey = "__builtin_square__";
    public const string CircleKey = "__builtin_circle__";

    public static TextureObjectUploaded? Square { get; private set; }
    public static TextureObjectUploaded? Circle { get; private set; }

    public static void Initialize(TextureManager textureManager)
    {
        Square = textureManager.CreateFromRgba(GenerateSquare(32, 32), 32, 32, SquareKey);
        Circle = textureManager.CreateFromRgba(GenerateCircle(32, 32), 32, 32, CircleKey);
    }

    private static byte[] GenerateSquare(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }
        return pixels;
    }

    private static byte[] GenerateCircle(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        float cx = width * 0.5f;
        float cy = height * 0.5f;
        float radius = Math.Min(cx, cy);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = x + 0.5f - cx;
                float dy = y + 0.5f - cy;
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                int idx = (y * width + x) * 4;
                pixels[idx] = 255;
                pixels[idx + 1] = 255;
                pixels[idx + 2] = 255;

                float alpha = Math.Clamp(radius - dist, 0f, 1f);
                pixels[idx + 3] = (byte)(alpha * 255);
            }
        }
        return pixels;
    }
}
