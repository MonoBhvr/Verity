using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingStringFormat = System.Drawing.StringFormat;
using Verity.Core.World;

namespace Verity.Graphics;

[SupportedOSPlatform("windows")]
public static class SdfFontAssetGenerator
{
    private const int PageGap = 2;
    private const float DistanceTransformInfinity = 1e20f;

    public static SdfFontAsset Generate(string sourceFontPath, string outputAssetPath, SdfFontGenerationOptions? generationOptions = null)
    {
        if (string.IsNullOrWhiteSpace(sourceFontPath))
            throw new ArgumentException("A source font path is required.", nameof(sourceFontPath));
        if (string.IsNullOrWhiteSpace(outputAssetPath))
            throw new ArgumentException("An output asset path is required.", nameof(outputAssetPath));

        string sourceFullPath = Path.GetFullPath(sourceFontPath);
        if (!File.Exists(sourceFullPath))
            throw new FileNotFoundException("The source font file could not be found.", sourceFullPath);

        string outputFullPath = Path.GetFullPath(outputAssetPath);
        if (!SdfFontAsset.IsFontAssetPath(outputFullPath))
            outputFullPath += SdfFontAsset.PrimaryExtension;

        var options = generationOptions ?? new SdfFontGenerationOptions();
        options.Normalize();

        EnsureWritable(outputFullPath, options);

        using var collection = new PrivateFontCollection();
        collection.AddFontFile(sourceFullPath);
        if (collection.Families.Length == 0)
            throw new InvalidOperationException("The source font did not expose any font families.");

        using var baseFont = new System.Drawing.Font(collection.Families[0], options.PointSize, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
        using var hiFont = new System.Drawing.Font(collection.Families[0], options.PointSize * options.Supersample, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
        using var measureBitmap = new System.Drawing.Bitmap(4, 4, PixelFormat.Format32bppArgb);
        using var measureGraphics = System.Drawing.Graphics.FromImage(measureBitmap);
        ConfigureGraphics(measureGraphics);

        float lineHeight = baseFont.GetHeight(measureGraphics);
        float spaceAdvance = MeasureAdvance(measureGraphics, baseFont, " ");
        int emHeight = collection.Families[0].GetEmHeight(System.Drawing.FontStyle.Regular);
        float ascent = options.PointSize * collection.Families[0].GetCellAscent(System.Drawing.FontStyle.Regular) / Math.Max(1, emHeight);
        float descent = options.PointSize * collection.Families[0].GetCellDescent(System.Drawing.FontStyle.Regular) / Math.Max(1, emHeight);

        var pages = new List<AtlasPageBuilder> { new(options.AtlasWidth, options.AtlasHeight) };
        var glyphs = new List<SdfFontGlyph>();

        foreach (string runeText in SdfFontGenerationOptions.EnumerateDistinctRunes(options.Characters))
        {
            int runeValue = runeText.EnumerateRunes().First().Value;
            float advance = MeasureAdvance(measureGraphics, baseFont, runeText);
            if (advance <= 0f && runeValue == ' ')
                advance = MathF.Max(spaceAdvance, options.PointSize * 0.33f);

            GeneratedGlyph glyph = GenerateGlyphBitmap(runeText, runeValue, advance, hiFont, lineHeight, options);
            if (!glyph.HasTexture)
            {
                glyphs.Add(new SdfFontGlyph
                {
                    Unicode = runeValue,
                    AtlasIndex = -1,
                    Advance = glyph.Advance,
                    OffsetX = glyph.OffsetX,
                    OffsetY = glyph.OffsetY
                });
                continue;
            }

            AtlasAllocation allocation = AllocateGlyph(pages, glyph.Width, glyph.Height, options);
            allocation.Page.WriteGlyph(allocation.X, allocation.Y, glyph.Pixels, glyph.Width, glyph.Height);

            glyphs.Add(new SdfFontGlyph
            {
                Unicode = runeValue,
                AtlasIndex = allocation.PageIndex,
                X = allocation.X,
                Y = allocation.Y,
                Width = glyph.Width,
                Height = glyph.Height,
                Advance = glyph.Advance,
                OffsetX = glyph.OffsetX,
                OffsetY = glyph.OffsetY
            });
        }

        string outputDirectory = Path.GetDirectoryName(outputFullPath)!;
        Directory.CreateDirectory(outputDirectory);

        string baseName = Path.GetFileNameWithoutExtension(outputFullPath);
        var asset = new SdfFontAsset
        {
            SourceFontPath = sourceFullPath,
            FamilyName = collection.Families[0].Name,
            SamplingPointSize = options.PointSize,
            LineHeight = lineHeight,
            Ascent = ascent,
            Descent = descent,
            SpaceAdvance = MathF.Max(spaceAdvance, options.PointSize * 0.33f),
            Padding = options.Padding,
            Spread = options.Spread,
            Supersample = options.Supersample,
            Filter = options.Filter,
            Glyphs = glyphs
        };

        for (int i = 0; i < pages.Count; i++)
        {
            string pageFileName = $"{baseName}_{i}.png";
            string pageFullPath = Path.Combine(outputDirectory, pageFileName);
            SaveRgbaBitmap(pageFullPath, pages[i].Pixels, pages[i].Width, pages[i].Height, overwrite: options.OverwriteExistingFiles);
            asset.AtlasPages.Add(new SdfFontAtlasPage
            {
                Path = pageFileName,
                Width = pages[i].Width,
                Height = pages[i].Height
            });
        }

        asset.Save(outputFullPath);
        return asset;
    }

    private static GeneratedGlyph GenerateGlyphBitmap(string glyphText, int runeValue, float advance, System.Drawing.Font hiFont, float lineHeight, SdfFontGenerationOptions options)
    {
        int hiPadding = options.Padding * options.Supersample;
        int canvasWidth = Math.Max(32, (int)MathF.Ceiling(MathF.Max(advance, options.PointSize * 0.33f) * options.Supersample) + (hiPadding * 2) + 16);
        int canvasHeight = Math.Max(32, (int)MathF.Ceiling(lineHeight * options.Supersample) + (hiPadding * 2) + 16);

        using var bitmap = new System.Drawing.Bitmap(canvasWidth, canvasHeight, PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        ConfigureGraphics(graphics);
        graphics.Clear(System.Drawing.Color.Transparent);
        using var format = CreateMeasureFormat();
        graphics.DrawString(glyphText, hiFont, System.Drawing.Brushes.White, new System.Drawing.PointF(hiPadding, hiPadding), format);

        if (!TryExtractVisibleBounds(bitmap, out var visibleBounds, out var alphaPixels))
        {
            return new GeneratedGlyph(Array.Empty<byte>(), 0, 0, advance, 0f, 0f);
        }

        DrawingRectangle inflatedBounds = InflateAndClamp(visibleBounds, hiPadding, bitmap.Width, bitmap.Height);
        bool[] inside = ExtractInsideMask(alphaPixels, bitmap.Width, inflatedBounds);
        float[] signedDistance = ComputeSignedDistance(inside, inflatedBounds.Width, inflatedBounds.Height);
        byte[] sdfPixels = DownsampleSignedDistance(signedDistance, inflatedBounds.Width, inflatedBounds.Height, options);

        int glyphWidth = Math.Max(1, (inflatedBounds.Width + options.Supersample - 1) / options.Supersample);
        int glyphHeight = Math.Max(1, (inflatedBounds.Height + options.Supersample - 1) / options.Supersample);
        float offsetX = (inflatedBounds.Left / (float)options.Supersample) - options.Padding;
        float offsetY = (inflatedBounds.Top / (float)options.Supersample) - options.Padding;

        return new GeneratedGlyph(sdfPixels, glyphWidth, glyphHeight, advance, offsetX, offsetY);
    }

    private static AtlasAllocation AllocateGlyph(List<AtlasPageBuilder> pages, int width, int height, SdfFontGenerationOptions options)
    {
        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i].TryAllocate(width, height, out int x, out int y))
                return new AtlasAllocation(pages[i], i, x, y);
        }

        var newPage = new AtlasPageBuilder(options.AtlasWidth, options.AtlasHeight);
        pages.Add(newPage);
        if (!newPage.TryAllocate(width, height, out int pageX, out int pageY))
            throw new InvalidOperationException("The configured atlas size is too small for the generated glyph.");

        return new AtlasAllocation(newPage, pages.Count - 1, pageX, pageY);
    }

    private static void EnsureWritable(string outputAssetPath, SdfFontGenerationOptions options)
    {
        string directory = Path.GetDirectoryName(outputAssetPath)!;
        Directory.CreateDirectory(directory);

        if (!options.OverwriteExistingFiles && File.Exists(outputAssetPath))
            throw new IOException($"The output asset already exists: {outputAssetPath}");
    }

    private static DrawingRectangle InflateAndClamp(DrawingRectangle source, int padding, int maxWidth, int maxHeight)
    {
        int left = Math.Max(0, source.Left - padding);
        int top = Math.Max(0, source.Top - padding);
        int right = Math.Min(maxWidth, source.Right + padding);
        int bottom = Math.Min(maxHeight, source.Bottom + padding);
        return DrawingRectangle.FromLTRB(left, top, right, bottom);
    }

    private static bool[] ExtractInsideMask(byte[] alphaPixels, int sourceWidth, DrawingRectangle bounds)
    {
        var inside = new bool[bounds.Width * bounds.Height];
        for (int y = 0; y < bounds.Height; y++)
        {
            for (int x = 0; x < bounds.Width; x++)
            {
                int sourceIndex = ((bounds.Y + y) * sourceWidth) + bounds.X + x;
                inside[(y * bounds.Width) + x] = alphaPixels[sourceIndex] >= 128;
            }
        }

        return inside;
    }

    private static byte[] DownsampleSignedDistance(float[] signedDistance, int sourceWidth, int sourceHeight, SdfFontGenerationOptions options)
    {
        int targetWidth = Math.Max(1, (sourceWidth + options.Supersample - 1) / options.Supersample);
        int targetHeight = Math.Max(1, (sourceHeight + options.Supersample - 1) / options.Supersample);
        byte[] rgba = new byte[targetWidth * targetHeight * 4];
        float spread = Math.Max(1f, options.Spread * options.Supersample);
        int halfSample = options.Supersample / 2;

        for (int y = 0; y < targetHeight; y++)
        {
            int sampleY = Math.Min(sourceHeight - 1, (y * options.Supersample) + halfSample);
            for (int x = 0; x < targetWidth; x++)
            {
                int sampleX = Math.Min(sourceWidth - 1, (x * options.Supersample) + halfSample);
                float distance = signedDistance[(sampleY * sourceWidth) + sampleX];
                float normalized = Math.Clamp(0.5f + (distance / (2f * spread)), 0f, 1f);
                byte encoded = (byte)Math.Clamp((int)MathF.Round(normalized * 255f), 0, 255);
                int pixelIndex = ((y * targetWidth) + x) * 4;
                rgba[pixelIndex + 0] = encoded;
                rgba[pixelIndex + 1] = encoded;
                rgba[pixelIndex + 2] = encoded;
                rgba[pixelIndex + 3] = 255;
            }
        }

        return rgba;
    }

    private static float[] ComputeSignedDistance(bool[] inside, int width, int height)
    {
        bool[] outside = new bool[inside.Length];
        for (int i = 0; i < inside.Length; i++)
            outside[i] = !inside[i];

        float[] distanceToInside = ComputeDistanceTransform(inside, width, height);
        float[] distanceToOutside = ComputeDistanceTransform(outside, width, height);
        float[] signed = new float[inside.Length];

        for (int i = 0; i < signed.Length; i++)
            signed[i] = MathF.Sqrt(distanceToOutside[i]) - MathF.Sqrt(distanceToInside[i]);

        return signed;
    }

    private static float[] ComputeDistanceTransform(bool[] features, int width, int height)
    {
        int max = Math.Max(width, height);
        var source = new float[width * height];
        var intermediate = new float[width * height];
        var output = new float[width * height];
        var line = new float[max];
        var distance = new float[max];
        var vertices = new int[max];
        var boundaries = new float[max + 1];

        for (int i = 0; i < source.Length; i++)
            source[i] = features[i] ? 0f : DistanceTransformInfinity;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
                line[y] = source[(y * width) + x];

            DistanceTransform1D(line, height, distance, vertices, boundaries);

            for (int y = 0; y < height; y++)
                intermediate[(y * width) + x] = distance[y];
        }

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * width;
            for (int x = 0; x < width; x++)
                line[x] = intermediate[rowStart + x];

            DistanceTransform1D(line, width, distance, vertices, boundaries);

            for (int x = 0; x < width; x++)
                output[rowStart + x] = distance[x];
        }

        return output;
    }

    private static void DistanceTransform1D(float[] values, int length, float[] distances, int[] vertices, float[] boundaries)
    {
        int k = 0;
        vertices[0] = 0;
        boundaries[0] = float.NegativeInfinity;
        boundaries[1] = float.PositiveInfinity;

        for (int q = 1; q < length; q++)
        {
            float intersection;
            do
            {
                int vertex = vertices[k];
                intersection = ((values[q] + (q * q)) - (values[vertex] + (vertex * vertex))) / (2f * (q - vertex));
                if (intersection <= boundaries[k])
                    k--;
                else
                    break;
            } while (k >= 0);

            k++;
            vertices[k] = q;
            boundaries[k] = intersection;
            boundaries[k + 1] = float.PositiveInfinity;
        }

        k = 0;
        for (int q = 0; q < length; q++)
        {
            while (boundaries[k + 1] < q)
                k++;

            float delta = q - vertices[k];
            distances[q] = (delta * delta) + values[vertices[k]];
        }
    }

    private static float MeasureAdvance(System.Drawing.Graphics graphics, System.Drawing.Font font, string glyphText)
    {
        using var format = CreateMeasureFormat();
        return graphics.MeasureString(glyphText, font, int.MaxValue, format).Width;
    }

    private static void ConfigureGraphics(System.Drawing.Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    private static DrawingStringFormat CreateMeasureFormat()
    {
        return new DrawingStringFormat(System.Drawing.StringFormat.GenericTypographic)
        {
            FormatFlags = System.Drawing.StringFormatFlags.NoClip | System.Drawing.StringFormatFlags.MeasureTrailingSpaces
        };
    }

    private static bool TryExtractVisibleBounds(System.Drawing.Bitmap bitmap, out DrawingRectangle bounds, out byte[] alphaPixels)
    {
        bounds = DrawingRectangle.Empty;
        alphaPixels = Array.Empty<byte>();

        var rect = new DrawingRectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            byte[] raw = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);
            alphaPixels = new byte[bitmap.Width * bitmap.Height];

            int minX = bitmap.Width;
            int minY = bitmap.Height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < bitmap.Height; y++)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    byte alpha = raw[rowOffset + (x * 4) + 3];
                    alphaPixels[(y * bitmap.Width) + x] = alpha;
                    if (alpha == 0)
                        continue;

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
                return false;

            bounds = DrawingRectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            return true;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void SaveRgbaBitmap(string path, byte[] rgbaPixels, int width, int height, bool overwrite)
    {
        if (File.Exists(path))
        {
            if (!overwrite)
                throw new IOException($"The output atlas already exists: {path}");

            File.Delete(path);
        }

        using var bitmap = new System.Drawing.Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rect = new DrawingRectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            int stride = data.Stride;
            byte[] raw = new byte[stride * height];
            for (int y = 0; y < height; y++)
            {
                int srcRow = y * width * 4;
                int dstRow = y * stride;
                Array.Copy(rgbaPixels, srcRow, raw, dstRow, width * 4);
            }

            Marshal.Copy(raw, 0, data.Scan0, raw.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private readonly record struct GeneratedGlyph(byte[] Pixels, int Width, int Height, float Advance, float OffsetX, float OffsetY)
    {
        public bool HasTexture => Pixels.Length > 0 && Width > 0 && Height > 0;
    }

    private readonly record struct AtlasAllocation(AtlasPageBuilder Page, int PageIndex, int X, int Y);

    private sealed class AtlasPageBuilder
    {
        private int _cursorX = PageGap;
        private int _cursorY = PageGap;
        private int _rowHeight;

        public AtlasPageBuilder(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }

        public bool TryAllocate(int width, int height, out int x, out int y)
        {
            int paddedWidth = width + PageGap;
            int paddedHeight = height + PageGap;

            if ((_cursorX + paddedWidth) > Width)
            {
                _cursorX = PageGap;
                _cursorY += _rowHeight + PageGap;
                _rowHeight = 0;
            }

            if ((_cursorY + paddedHeight) > Height)
            {
                x = 0;
                y = 0;
                return false;
            }

            x = _cursorX;
            y = _cursorY;
            _cursorX += paddedWidth + PageGap;
            _rowHeight = Math.Max(_rowHeight, paddedHeight);
            return true;
        }

        public void WriteGlyph(int x, int y, byte[] glyphPixels, int width, int height)
        {
            for (int row = 0; row < height; row++)
            {
                int src = row * width * 4;
                int dst = ((y + row) * Width + x) * 4;
                Array.Copy(glyphPixels, src, Pixels, dst, width * 4);
            }
        }
    }
}
