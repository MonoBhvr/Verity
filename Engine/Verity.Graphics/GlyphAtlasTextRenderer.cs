using System.Numerics;
using System.Text;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Irodori.Framebuffer;
using Irodori.Texture;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingStringFormat = System.Drawing.StringFormat;
using Verity.Core.World;

namespace Verity.Graphics;

public enum TextHorizontalAlignment
{
    Left,
    Center,
    Right
}

public enum TextVerticalAlignment
{
    Top,
    Middle,
    Bottom
}

public readonly record struct TextRenderOptions(
    string Text,
    System.Numerics.Vector2 Position,
    System.Numerics.Vector2 MaxSize,
    Color Color,
    float FontSize,
    bool WordWrap,
    string FontPath,
    string FontFamily,
    TextHorizontalAlignment HorizontalAlignment,
    TextVerticalAlignment VerticalAlignment);

public sealed class GlyphAtlasTextRenderer : IDisposable
{
    private const int AtlasPageSize = 1024;
    private const int GlyphPadding = 2;

    private readonly GraphicsDevice _device;
    private readonly TextureManager _textureManager;
    private readonly Shader2D _shader;
    private readonly Func<string, string?, string> _resolveAssetPath;
    private readonly Irodori.Buffer.VertexBuffer.Unuploaded _dynamicBuffer;
    private readonly Dictionary<FontKey, FontFace> _fontCache = new();
    private readonly List<AtlasPage> _atlasPages = [];

    public GlyphAtlasTextRenderer(GraphicsDevice device, TextureManager textureManager, Shader2D shader, Func<string, string?, string> resolveAssetPath)
    {
        _device = device;
        _textureManager = textureManager;
        _shader = shader;
        _resolveAssetPath = resolveAssetPath;

        var format = Irodori.Buffer.VertexBufferFormat.Create()
            .AddAttrib(Irodori.Buffer.VertexBufferFormat.Attrib.Vector2())
            .AddAttrib(Irodori.Buffer.VertexBufferFormat.Attrib.Vector2());
        _dynamicBuffer = _device.CreateVertexBuffer(format);
    }

    public void DrawText(TextRenderOptions options, Matrix4x4 projection, Matrix4x4 view, FramebufferObject.Uploaded? targetFbo = null)
    {
        if (string.IsNullOrEmpty(options.Text))
            return;

        var font = GetFontFace(options);
        if (font == null)
            return;

        var layout = LayoutText(options, font);
        if (layout.TotalGlyphCount == 0)
            return;

        foreach (var atlasGroup in layout.Glyphs.GroupBy(static glyph => glyph.AtlasIndex))
        {
            if (atlasGroup.Key < 0 || atlasGroup.Key >= _atlasPages.Count)
                continue;

            var data = Irodori.Buffer.IVertexData.Create<System.Numerics.Vector2, System.Numerics.Vector2>();
            var indices = new List<int>();
            int vertexBase = 0;

            foreach (var glyph in atlasGroup)
            {
                data.AddVertex(new System.Numerics.Vector2(glyph.Position.X, glyph.Position.Y), glyph.UvMin);
                data.AddVertex(new System.Numerics.Vector2(glyph.Position.X + glyph.Size.X, glyph.Position.Y), new System.Numerics.Vector2(glyph.UvMax.X, glyph.UvMin.Y));
                data.AddVertex(new System.Numerics.Vector2(glyph.Position.X, glyph.Position.Y + glyph.Size.Y), new System.Numerics.Vector2(glyph.UvMin.X, glyph.UvMax.Y));
                data.AddVertex(new System.Numerics.Vector2(glyph.Position.X + glyph.Size.X, glyph.Position.Y + glyph.Size.Y), glyph.UvMax);

                indices.Add(vertexBase + 0);
                indices.Add(vertexBase + 2);
                indices.Add(vertexBase + 1);
                indices.Add(vertexBase + 1);
                indices.Add(vertexBase + 2);
                indices.Add(vertexBase + 3);
                vertexBase += 4;
            }

            if (vertexBase == 0)
                continue;

            _shader.SetProjection(projection);
            _shader.SetView(view);
            _shader.SetModel(Matrix4x4.Identity);
            _shader.SetUvRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One);
            _shader.SetTexture(_atlasPages[atlasGroup.Key].Texture);
            _shader.SetColor(options.Color);

            using var uploaded = _dynamicBuffer.Upload(data, indices.ToArray()).Unwrap();
            uploaded.Draw(_shader.Program, targetFbo).Unwrap();
        }
    }

    public void Dispose()
    {
        foreach (var font in _fontCache.Values)
            font.Dispose();

        foreach (var atlas in _atlasPages)
            atlas.Dispose();

        _fontCache.Clear();
        _atlasPages.Clear();
    }

    private TextLayoutResult LayoutText(TextRenderOptions options, FontFace font)
    {
        float maxWidth = options.MaxSize.X > 0f ? options.MaxSize.X : float.PositiveInfinity;
        float maxHeight = options.MaxSize.Y > 0f ? options.MaxSize.Y : float.PositiveInfinity;
        float lineHeight = font.LineHeight;
        float penX = 0f;
        int lineStartGlyphIndex = 0;

        var glyphs = new List<PositionedGlyph>();
        var lines = new List<LayoutLine>();

        foreach (var rune in options.Text.EnumerateRunes())
        {
            if (rune.Value == '\r')
                continue;

            if (rune.Value == '\n')
            {
                lines.Add(new LayoutLine(lineStartGlyphIndex, glyphs.Count, penX));
                penX = 0f;
                lineStartGlyphIndex = glyphs.Count;
                continue;
            }

            if (rune.Value == '\t')
            {
                penX += font.SpaceAdvance * 4f;
                continue;
            }

            var glyph = GetGlyph(font, rune);
            if (glyph == null)
                continue;

            float advance = glyph.Advance <= 0f ? font.SpaceAdvance : glyph.Advance;
            if (options.WordWrap && !float.IsInfinity(maxWidth) && penX > 0f && penX + advance > maxWidth)
            {
                lines.Add(new LayoutLine(lineStartGlyphIndex, glyphs.Count, penX));
                penX = 0f;
                lineStartGlyphIndex = glyphs.Count;
            }

            float lineY = lines.Count * lineHeight;
            if (glyph.HasTexture)
            {
                glyphs.Add(new PositionedGlyph(
                    glyph.AtlasIndex,
                    new System.Numerics.Vector2(penX + glyph.OffsetX, lineY + glyph.OffsetY),
                    new System.Numerics.Vector2(glyph.Width, glyph.Height),
                    glyph.UvMin,
                    glyph.UvMax));
            }

            penX += advance;
        }

        lines.Add(new LayoutLine(lineStartGlyphIndex, glyphs.Count, penX));

        float totalHeight = lines.Count * lineHeight;
        float yOffset = ComputeVerticalOffset(options.VerticalAlignment, maxHeight, totalHeight);

        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            float xOffset = ComputeHorizontalOffset(options.HorizontalAlignment, maxWidth, line.Width);
            for (int i = line.StartGlyphIndex; i < line.EndGlyphIndex; i++)
            {
                var positioned = glyphs[i];
                glyphs[i] = positioned with
                {
                    Position = positioned.Position + new System.Numerics.Vector2(options.Position.X + xOffset, options.Position.Y + yOffset)
                };
            }
        }

        return new TextLayoutResult(glyphs, glyphs.Count, totalHeight);
    }

    private static float ComputeHorizontalOffset(TextHorizontalAlignment alignment, float maxWidth, float lineWidth)
    {
        if (float.IsInfinity(maxWidth))
            return 0f;

        return alignment switch
        {
            TextHorizontalAlignment.Center => MathF.Max(0f, (maxWidth - lineWidth) * 0.5f),
            TextHorizontalAlignment.Right => MathF.Max(0f, maxWidth - lineWidth),
            _ => 0f
        };
    }

    private static float ComputeVerticalOffset(TextVerticalAlignment alignment, float maxHeight, float totalHeight)
    {
        if (float.IsInfinity(maxHeight))
            return 0f;

        return alignment switch
        {
            TextVerticalAlignment.Middle => MathF.Max(0f, (maxHeight - totalHeight) * 0.5f),
            TextVerticalAlignment.Bottom => MathF.Max(0f, maxHeight - totalHeight),
            _ => 0f
        };
    }

    private FontFace? GetFontFace(TextRenderOptions options)
    {
        float fontSize = MathF.Max(1f, options.FontSize);
        string resolvedSource = ResolveFontSource(options.FontPath, options.FontFamily);
        var key = new FontKey(resolvedSource, fontSize);

        if (_fontCache.TryGetValue(key, out var cached))
            return cached;

        FontFace? created = FontFace.Create(resolvedSource, fontSize);
        if (created == null)
            return null;

        _fontCache[key] = created;
        return created;
    }

    private string ResolveFontSource(string fontPath, string fontFamily)
    {
        if (!string.IsNullOrWhiteSpace(fontPath))
        {
            try
            {
                string resolved = _resolveAssetPath(fontPath, null);
                if (File.Exists(resolved))
                    return resolved;
            }
            catch
            {
            }

            if (Path.IsPathRooted(fontPath) && File.Exists(fontPath))
                return Path.GetFullPath(fontPath);
        }

        if (!string.IsNullOrWhiteSpace(fontFamily))
            return $"family:{fontFamily}";

        return $"family:{System.Drawing.SystemFonts.MessageBoxFont?.FontFamily.Name ?? "Arial"}";
    }

    private GlyphEntry? GetGlyph(FontFace font, Rune rune)
    {
        if (font.Glyphs.TryGetValue(rune.Value, out var cached))
            return cached;

        var rendered = RasterizeGlyph(font, rune);
        font.Glyphs[rune.Value] = rendered;
        return rendered;
    }

    private GlyphEntry RasterizeGlyph(FontFace font, Rune rune)
    {
        string glyphText = rune.ToString();
        float advance = MeasureAdvance(font, glyphText);
        advance = advance <= 0f && rune.Value == ' ' ? font.SpaceAdvance : advance;

        int canvasWidth = Math.Max(4, (int)MathF.Ceiling(MathF.Max(advance, font.SpaceAdvance)) + GlyphPadding * 4);
        int canvasHeight = Math.Max(4, (int)MathF.Ceiling(font.LineHeight) + GlyphPadding * 4);

        using var bitmap = new System.Drawing.Bitmap(canvasWidth, canvasHeight, PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        ConfigureGraphics(graphics);
        graphics.Clear(System.Drawing.Color.Transparent);

        using var drawFont = font.CreateFont();
        using var format = CreateMeasureFormat();
        graphics.DrawString(glyphText, drawFont, System.Drawing.Brushes.White, new System.Drawing.PointF(GlyphPadding, GlyphPadding), format);

        if (!TryExtractGlyphPixels(bitmap, out var bounds, out var pixels))
            return GlyphEntry.Empty(advance);

        var atlasRect = AllocateGlyph(bounds.Width, bounds.Height, out int atlasIndex);
        _atlasPages[atlasIndex].UploadGlyph(atlasRect, pixels);

        float uvMinX = atlasRect.X / (float)_atlasPages[atlasIndex].Width;
        float uvMaxX = (atlasRect.X + atlasRect.Width) / (float)_atlasPages[atlasIndex].Width;
        float uvTop = atlasRect.Y / (float)_atlasPages[atlasIndex].Height;
        float uvBottom = (atlasRect.Y + atlasRect.Height) / (float)_atlasPages[atlasIndex].Height;

        return new GlyphEntry(
            atlasIndex,
            bounds.Width,
            bounds.Height,
            advance,
            bounds.Left - GlyphPadding,
            bounds.Top - GlyphPadding,
            new System.Numerics.Vector2(uvMinX, uvBottom),
            new System.Numerics.Vector2(uvMaxX, uvTop));
    }

    private static float MeasureAdvance(FontFace font, string glyphText)
    {
        using var measureBitmap = new System.Drawing.Bitmap(4, 4, PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(measureBitmap);
        ConfigureGraphics(graphics);
        using var drawFont = font.CreateFont();
        using var format = CreateMeasureFormat();
        var size = graphics.MeasureString(glyphText, drawFont, int.MaxValue, format);
        return size.Width;
    }

    private DrawingRectangle AllocateGlyph(int width, int height, out int atlasIndex)
    {
        for (int i = 0; i < _atlasPages.Count; i++)
        {
            if (_atlasPages[i].TryAllocate(width, height, out var rect))
            {
                atlasIndex = i;
                return rect;
            }
        }

        var page = new AtlasPage(_textureManager, _atlasPages.Count);
        _atlasPages.Add(page);
        if (!page.TryAllocate(width, height, out var pageRect))
            throw new InvalidOperationException("Failed to allocate glyph in a new atlas page.");

        atlasIndex = _atlasPages.Count - 1;
        return pageRect;
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

    private static bool TryExtractGlyphPixels(System.Drawing.Bitmap bitmap, out DrawingRectangle bounds, out byte[] pixels)
    {
        bounds = DrawingRectangle.Empty;
        pixels = Array.Empty<byte>();

        var rect = new DrawingRectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            int byteCount = stride * bitmap.Height;
            byte[] raw = new byte[byteCount];
            Marshal.Copy(data.Scan0, raw, 0, byteCount);

            int minX = bitmap.Width;
            int minY = bitmap.Height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < bitmap.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int index = row + x * 4;
                    byte alpha = raw[index + 3];
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
            pixels = new byte[bounds.Width * bounds.Height * 4];

            for (int y = 0; y < bounds.Height; y++)
            {
                for (int x = 0; x < bounds.Width; x++)
                {
                    int srcIndex = ((bounds.Y + y) * stride) + ((bounds.X + x) * 4);
                    int dstIndex = (y * bounds.Width + x) * 4;
                    byte alpha = raw[srcIndex + 3];
                    pixels[dstIndex + 0] = 255;
                    pixels[dstIndex + 1] = 255;
                    pixels[dstIndex + 2] = 255;
                    pixels[dstIndex + 3] = alpha;
                }
            }

            return true;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private readonly record struct FontKey(string Source, float Size);

    private sealed class FontFace : IDisposable
    {
        private readonly PrivateFontCollection? _privateCollection;

        public System.Drawing.FontFamily Family { get; }
        public float PixelSize { get; }
        public float LineHeight { get; }
        public float SpaceAdvance { get; }
        public Dictionary<int, GlyphEntry> Glyphs { get; } = new();

        private FontFace(System.Drawing.FontFamily family, float pixelSize, float lineHeight, float spaceAdvance, PrivateFontCollection? privateCollection)
        {
            Family = family;
            PixelSize = pixelSize;
            LineHeight = lineHeight;
            SpaceAdvance = spaceAdvance;
            _privateCollection = privateCollection;
        }

        public static FontFace? Create(string source, float pixelSize)
        {
            try
            {
                PrivateFontCollection? collection = null;
                System.Drawing.FontFamily family;

                if (source.StartsWith("family:", StringComparison.OrdinalIgnoreCase))
                {
                    family = new System.Drawing.FontFamily(source["family:".Length..]);
                }
                else
                {
                    collection = new PrivateFontCollection();
                    collection.AddFontFile(source);
                    family = collection.Families[0];
                }

                using var bitmap = new System.Drawing.Bitmap(4, 4, PixelFormat.Format32bppArgb);
                using var graphics = System.Drawing.Graphics.FromImage(bitmap);
                ConfigureGraphics(graphics);
                using var font = new System.Drawing.Font(family, pixelSize, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
                float lineHeight = font.GetHeight(graphics);
                float spaceAdvance = graphics.MeasureString(" ", font, int.MaxValue, CreateMeasureFormat()).Width;
                return new FontFace(family, pixelSize, lineHeight, MathF.Max(spaceAdvance, pixelSize * 0.33f), collection);
            }
            catch
            {
                return null;
            }
        }

        public System.Drawing.Font CreateFont()
        {
            return new System.Drawing.Font(Family, PixelSize, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
        }

        public void Dispose()
        {
            _privateCollection?.Dispose();
        }
    }

    private sealed class AtlasPage : IDisposable
    {
        private readonly byte[] _pixels;
        private int _cursorX = GlyphPadding;
        private int _cursorY = GlyphPadding;
        private int _rowHeight;

        public TextureObjectUploaded Texture { get; }
        public int Width { get; } = AtlasPageSize;
        public int Height { get; } = AtlasPageSize;

        public AtlasPage(TextureManager textureManager, int index)
        {
            _pixels = new byte[Width * Height * 4];
            Texture = textureManager.CreateFromRgba(_pixels, Width, Height, $"__font_atlas_{index}__", SpriteTextureFilter.Linear);
        }

        public bool TryAllocate(int width, int height, out DrawingRectangle rect)
        {
            int paddedWidth = width + GlyphPadding * 2;
            int paddedHeight = height + GlyphPadding * 2;

            if (_cursorX + paddedWidth > Width)
            {
                _cursorX = GlyphPadding;
                _cursorY += _rowHeight + GlyphPadding;
                _rowHeight = 0;
            }

            if (_cursorY + paddedHeight > Height)
            {
                rect = DrawingRectangle.Empty;
                return false;
            }

            rect = new DrawingRectangle(_cursorX + GlyphPadding, _cursorY + GlyphPadding, width, height);
            _cursorX += paddedWidth + GlyphPadding;
            _rowHeight = Math.Max(_rowHeight, paddedHeight);
            return true;
        }

        public void UploadGlyph(DrawingRectangle rect, byte[] rgbaPixels)
        {
            for (int y = 0; y < rect.Height; y++)
            {
                int srcRow = y * rect.Width * 4;
                int dstRow = ((rect.Y + y) * Width + rect.X) * 4;
                Array.Copy(rgbaPixels, srcRow, _pixels, dstRow, rect.Width * 4);
            }

            Texture.UpdatePartial(PartialTextureData.Create(rgbaPixels), rect.X, rect.Y, rect.Width, rect.Height).Unwrap();
        }

        public void Dispose()
        {
            Texture.Dispose();
        }
    }

    private sealed record GlyphEntry(
        int AtlasIndex,
        int Width,
        int Height,
        float Advance,
        float OffsetX,
        float OffsetY,
        System.Numerics.Vector2 UvMin,
        System.Numerics.Vector2 UvMax)
    {
        public bool HasTexture => AtlasIndex >= 0 && Width > 0 && Height > 0;

        public static GlyphEntry Empty(float advance) =>
            new(-1, 0, 0, advance, 0f, 0f, System.Numerics.Vector2.Zero, System.Numerics.Vector2.Zero);
    }

    private readonly record struct PositionedGlyph(
        int AtlasIndex,
        System.Numerics.Vector2 Position,
        System.Numerics.Vector2 Size,
        System.Numerics.Vector2 UvMin,
        System.Numerics.Vector2 UvMax);

    private readonly record struct LayoutLine(int StartGlyphIndex, int EndGlyphIndex, float Width);

    private readonly record struct TextLayoutResult(IReadOnlyList<PositionedGlyph> Glyphs, int TotalGlyphCount, float TotalHeight);
}
