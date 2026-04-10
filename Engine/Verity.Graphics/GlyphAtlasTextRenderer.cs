using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
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

[SupportedOSPlatform("windows")]
public sealed class GlyphAtlasTextRenderer : IDisposable
{
    private const int DynamicAtlasPageSize = 1024;
    private const int DynamicGlyphPadding = 2;

    private const string SdfFragmentSource = @"#version 330 core
in vec2 vTexCoord;
uniform sampler2D uTexture;
uniform vec4 uColor;
uniform float uScreenPxRange;
out vec4 FragColor;

void main()
{
    float distanceValue = texture(uTexture, vTexCoord).r;
    float screenPxDistance = uScreenPxRange * (distanceValue - 0.5);
    float alpha = clamp(screenPxDistance + 0.5, 0.0, 1.0);
    FragColor = vec4(uColor.rgb, uColor.a * alpha);
}";

    private readonly GraphicsDevice _device;
    private readonly TextureManager _textureManager;
    private readonly Shader2D _shader;
    private readonly Shader2D _sdfShader;
    private readonly Func<string, string?, string> _resolveAssetPath;
    private readonly Irodori.Buffer.VertexBuffer.Unuploaded _dynamicBuffer;
    private readonly Dictionary<BitmapFontKey, BitmapFontFace> _bitmapFontCache = new();
    private readonly Dictionary<string, CachedSdfFontFace> _sdfFontCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DynamicAtlasPage> _dynamicAtlasPages = [];
    private string? _cachedDefaultBitmapFontSource;

    public GlyphAtlasTextRenderer(GraphicsDevice device, TextureManager textureManager, Shader2D shader, Func<string, string?, string> resolveAssetPath)
    {
        _device = device;
        _textureManager = textureManager;
        _shader = shader;
        _sdfShader = Shader2D.Create(device, fragmentSource: SdfFragmentSource);
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

        if (TryGetSdfFontFace(options.FontPath, out var sdfFont))
        {
            DrawTextInternal(options, sdfFont, _sdfShader, projection, view, targetFbo);
            return;
        }

        var bitmapFont = GetBitmapFontFace(options);
        if (bitmapFont == null)
            return;

        DrawTextInternal(options, bitmapFont, _shader, projection, view, targetFbo);
    }

    public void Dispose()
    {
        foreach (var font in _bitmapFontCache.Values)
            font.Dispose();

        foreach (var font in _sdfFontCache.Values)
            font.Face.Dispose();

        foreach (var atlas in _dynamicAtlasPages)
            atlas.Dispose();

        _bitmapFontCache.Clear();
        _sdfFontCache.Clear();
        _dynamicAtlasPages.Clear();
        _sdfShader.Dispose();
    }

    private void DrawTextInternal(TextRenderOptions options, ITextFontFace font, Shader2D shader, Matrix4x4 projection, Matrix4x4 view, FramebufferObject.Uploaded? targetFbo)
    {
        var layout = LayoutText(options, font);
        if (layout.TotalGlyphCount == 0)
            return;

        foreach (var atlasGroup in layout.Glyphs.GroupBy(static glyph => glyph.AtlasIndex))
        {
            if (atlasGroup.Key < 0 || atlasGroup.Key >= font.AtlasTextures.Count)
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

            shader.SetProjection(projection);
            shader.SetView(view);
            shader.SetModel(Matrix4x4.Identity);
            shader.SetUvRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One);
            shader.SetTexture(font.AtlasTextures[atlasGroup.Key]);
            shader.SetColor(options.Color);
            if (ReferenceEquals(shader, _sdfShader) && font is SdfFontFace sdfFont)
                shader.SetFloat("uScreenPxRange", ComputeScreenPxRange(options.FontSize, sdfFont));

            using var uploaded = _dynamicBuffer.Upload(data, indices.ToArray()).Unwrap();
            uploaded.Draw(shader.Program, targetFbo).Unwrap();
        }
    }

    private TextLayoutResult LayoutText(TextRenderOptions options, ITextFontFace font)
    {
        float maxWidth = options.MaxSize.X > 0f ? options.MaxSize.X : float.PositiveInfinity;
        float maxHeight = options.MaxSize.Y > 0f ? options.MaxSize.Y : float.PositiveInfinity;
        float requestedFontSize = options.FontSize > 0f ? options.FontSize : font.ReferenceFontSize;
        float scale = requestedFontSize / MathF.Max(1f, font.ReferenceFontSize);
        float lineHeight = font.LineHeight * scale;
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
                penX += font.SpaceAdvance * scale * 4f;
                continue;
            }

            if (!font.TryGetGlyph(rune, out var glyph))
                continue;

            float advance = (glyph.Advance <= 0f ? font.SpaceAdvance : glyph.Advance) * scale;
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
                    new System.Numerics.Vector2(penX + (glyph.OffsetX * scale), lineY + (glyph.OffsetY * scale)),
                    new System.Numerics.Vector2(glyph.Width * scale, glyph.Height * scale),
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
                    Position = SnapToPixel(positioned.Position + new System.Numerics.Vector2(options.Position.X + xOffset, options.Position.Y + yOffset))
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

    private static System.Numerics.Vector2 SnapToPixel(System.Numerics.Vector2 position)
    {
        return new System.Numerics.Vector2(MathF.Round(position.X), MathF.Round(position.Y));
    }

    private static float ComputeScreenPxRange(float requestedFontSize, SdfFontFace font)
    {
        float fontSize = MathF.Max(1f, requestedFontSize);
        float scale = fontSize / MathF.Max(1f, font.ReferenceFontSize);
        return MathF.Max(1f, font.Spread * scale);
    }

    private BitmapFontFace? GetBitmapFontFace(TextRenderOptions options)
    {
        float fontSize = MathF.Max(1f, options.FontSize);
        string resolvedSource = ResolveBitmapFontSource(options.FontPath, options.FontFamily);
        var key = new BitmapFontKey(resolvedSource, fontSize);

        if (_bitmapFontCache.TryGetValue(key, out var cached))
            return cached;

        BitmapFontFace? created = BitmapFontFace.Create(this, resolvedSource, fontSize);
        if (created == null)
            return null;

        _bitmapFontCache[key] = created;
        return created;
    }

    private bool TryGetSdfFontFace(string fontPath, out SdfFontFace face)
    {
        face = null!;

        string resolvedAssetPath = ResolveSdfFontAssetPath(fontPath);
        if (string.IsNullOrWhiteSpace(resolvedAssetPath))
            return false;

        long versionToken = File.GetLastWriteTimeUtc(resolvedAssetPath).Ticks;
        if (_sdfFontCache.TryGetValue(resolvedAssetPath, out var cached))
        {
            if (cached.VersionToken == versionToken)
            {
                face = cached.Face;
                return true;
            }

            cached.Face.Dispose();
            _sdfFontCache.Remove(resolvedAssetPath);
        }

        var created = SdfFontFace.Create(resolvedAssetPath, _resolveAssetPath, _textureManager);
        if (created == null)
            return false;

        _sdfFontCache[resolvedAssetPath] = new CachedSdfFontFace(created, versionToken);
        face = created;
        return true;
    }

    private string ResolveBitmapFontSource(string fontPath, string fontFamily)
    {
        if (!string.IsNullOrWhiteSpace(fontPath))
        {
            try
            {
                string resolved = _resolveAssetPath(fontPath, null);
                if (File.Exists(resolved) && !SdfFontAsset.IsFontAssetPath(resolved))
                    return resolved;
            }
            catch
            {
            }

            if (Path.IsPathRooted(fontPath) && File.Exists(fontPath) && !SdfFontAsset.IsFontAssetPath(fontPath))
                return Path.GetFullPath(fontPath);
        }

        if (!string.IsNullOrWhiteSpace(fontFamily))
            return $"family:{fontFamily}";

        return _cachedDefaultBitmapFontSource ??= FindDefaultBitmapFontSource();
    }

    private string FindDefaultBitmapFontSource()
    {
        foreach (string candidateFamily in GetCandidateFontFamilies())
        {
            try
            {
                using var family = new System.Drawing.FontFamily(candidateFamily);
                return $"family:{family.Name}";
            }
            catch
            {
            }
        }

        foreach (string candidatePath in GetCandidateFontPaths())
        {
            if (File.Exists(candidatePath))
                return Path.GetFullPath(candidatePath);
        }

        return $"family:{System.Drawing.SystemFonts.MessageBoxFont?.FontFamily.Name ?? "Arial"}";
    }

    private IEnumerable<string> GetCandidateFontPaths()
    {
        string windowsFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!string.IsNullOrWhiteSpace(windowsFonts))
        {
            yield return Path.Combine(windowsFonts, "malgun.ttf");
            yield return Path.Combine(windowsFonts, "malgunbd.ttf");
            yield return Path.Combine(windowsFonts, "arialuni.ttf");
        }
    }

    private static IEnumerable<string> GetCandidateFontFamilies()
    {
        yield return "Malgun Gothic";
        yield return "Noto Sans KR";
        yield return "Noto Sans CJK KR";
        yield return "Microsoft YaHei UI";
        yield return "Gulim";
        yield return "Batang";
        yield return "Segoe UI";
        yield return "Arial Unicode MS";
        yield return "Arial";
    }

    private string ResolveSdfFontAssetPath(string fontPath)
    {
        if (string.IsNullOrWhiteSpace(fontPath))
            return string.Empty;

        try
        {
            string resolved = _resolveAssetPath(fontPath, null);
            if (File.Exists(resolved) && SdfFontAsset.IsFontAssetPath(resolved))
                return Path.GetFullPath(resolved);
        }
        catch
        {
        }

        if (Path.IsPathRooted(fontPath) && File.Exists(fontPath) && SdfFontAsset.IsFontAssetPath(fontPath))
            return Path.GetFullPath(fontPath);

        return string.Empty;
    }

    private GlyphEntry RasterizeGlyph(BitmapFontFace font, Rune rune)
    {
        string glyphText = rune.ToString();
        float advance = MeasureAdvance(font, glyphText);
        advance = advance <= 0f && rune.Value == ' ' ? font.SpaceAdvance : advance;

        int canvasWidth = Math.Max(4, (int)MathF.Ceiling(MathF.Max(advance, font.SpaceAdvance)) + DynamicGlyphPadding * 4);
        int canvasHeight = Math.Max(4, (int)MathF.Ceiling(font.LineHeight) + DynamicGlyphPadding * 4);

        using var bitmap = new System.Drawing.Bitmap(canvasWidth, canvasHeight, PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        ConfigureGraphics(graphics);
        graphics.Clear(System.Drawing.Color.Transparent);

        using var drawFont = font.CreateFont();
        using var format = CreateMeasureFormat();
        graphics.DrawString(glyphText, drawFont, System.Drawing.Brushes.White, new System.Drawing.PointF(DynamicGlyphPadding, DynamicGlyphPadding), format);

        if (!TryExtractGlyphPixels(bitmap, out var bounds, out var pixels))
            return GlyphEntry.Empty(advance);

        var atlasRect = AllocateDynamicGlyph(bounds.Width, bounds.Height, out int atlasIndex);
        _dynamicAtlasPages[atlasIndex].UploadGlyph(atlasRect, pixels);

        float uvMinX = atlasRect.X / (float)_dynamicAtlasPages[atlasIndex].Width;
        float uvMaxX = (atlasRect.X + atlasRect.Width) / (float)_dynamicAtlasPages[atlasIndex].Width;
        float uvTop = atlasRect.Y / (float)_dynamicAtlasPages[atlasIndex].Height;
        float uvBottom = (atlasRect.Y + atlasRect.Height) / (float)_dynamicAtlasPages[atlasIndex].Height;

        return new GlyphEntry(
            atlasIndex,
            bounds.Width,
            bounds.Height,
            advance,
            bounds.Left - DynamicGlyphPadding,
            bounds.Top - DynamicGlyphPadding,
            new System.Numerics.Vector2(uvMinX, uvTop),
            new System.Numerics.Vector2(uvMaxX, uvBottom));
    }

    private float MeasureAdvance(BitmapFontFace font, string glyphText)
    {
        using var measureBitmap = new System.Drawing.Bitmap(4, 4, PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(measureBitmap);
        ConfigureGraphics(graphics);
        using var drawFont = font.CreateFont();
        using var format = CreateMeasureFormat();
        var size = graphics.MeasureString(glyphText, drawFont, int.MaxValue, format);
        return size.Width;
    }

    private DrawingRectangle AllocateDynamicGlyph(int width, int height, out int atlasIndex)
    {
        for (int i = 0; i < _dynamicAtlasPages.Count; i++)
        {
            if (_dynamicAtlasPages[i].TryAllocate(width, height, out var rect))
            {
                atlasIndex = i;
                return rect;
            }
        }

        var page = new DynamicAtlasPage(_textureManager, _dynamicAtlasPages.Count);
        _dynamicAtlasPages.Add(page);
        if (!page.TryAllocate(width, height, out var pageRect))
            throw new InvalidOperationException("Failed to allocate glyph in a new atlas page.");

        atlasIndex = _dynamicAtlasPages.Count - 1;
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

    private readonly record struct BitmapFontKey(string Source, float Size);
    private readonly record struct CachedSdfFontFace(SdfFontFace Face, long VersionToken);

    private interface ITextFontFace
    {
        float ReferenceFontSize { get; }
        float LineHeight { get; }
        float SpaceAdvance { get; }
        IReadOnlyList<TextureObjectUploaded> AtlasTextures { get; }
        bool TryGetGlyph(Rune rune, out GlyphEntry glyph);
    }

    private sealed class BitmapFontFace : ITextFontFace, IDisposable
    {
        private readonly PrivateFontCollection? _privateCollection;
        private readonly GlyphAtlasTextRenderer _owner;

        public BitmapFontFace(GlyphAtlasTextRenderer owner, System.Drawing.FontFamily family, float pixelSize, float lineHeight, float spaceAdvance, PrivateFontCollection? privateCollection)
        {
            _owner = owner;
            Family = family;
            PixelSize = pixelSize;
            LineHeight = lineHeight;
            SpaceAdvance = spaceAdvance;
            _privateCollection = privateCollection;
        }

        public System.Drawing.FontFamily Family { get; }
        public float PixelSize { get; }
        public float ReferenceFontSize => PixelSize;
        public float LineHeight { get; }
        public float SpaceAdvance { get; }
        public Dictionary<int, GlyphEntry> Glyphs { get; } = new();
        public IReadOnlyList<TextureObjectUploaded> AtlasTextures => _owner._dynamicAtlasPages.Select(static page => page.Texture).ToList();

        public static BitmapFontFace? Create(GlyphAtlasTextRenderer owner, string source, float pixelSize)
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
                return new BitmapFontFace(owner, family, pixelSize, lineHeight, MathF.Max(spaceAdvance, pixelSize * 0.33f), collection);
            }
            catch
            {
                return null;
            }
        }

        public bool TryGetGlyph(Rune rune, out GlyphEntry glyph)
        {
            if (Glyphs.TryGetValue(rune.Value, out GlyphEntry? cached) && cached != null)
            {
                glyph = cached;
                return true;
            }

            glyph = _owner.RasterizeGlyph(this, rune);
            Glyphs[rune.Value] = glyph;
            return true;
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

    private sealed class SdfFontFace : ITextFontFace, IDisposable
    {
        private readonly TextureManager _textureManager;
        private readonly string[] _atlasPaths;
        private readonly Dictionary<int, GlyphEntry> _glyphs;

        private SdfFontFace(TextureManager textureManager, string[] atlasPaths, IReadOnlyList<TextureObjectUploaded> textures, Dictionary<int, GlyphEntry> glyphs, float referenceFontSize, float lineHeight, float spaceAdvance, int spread)
        {
            _textureManager = textureManager;
            _atlasPaths = atlasPaths;
            AtlasTextures = textures;
            _glyphs = glyphs;
            ReferenceFontSize = MathF.Max(1f, referenceFontSize);
            LineHeight = MathF.Max(1f, lineHeight);
            SpaceAdvance = MathF.Max(1f, spaceAdvance);
            Spread = Math.Max(1, spread);
        }

        public float ReferenceFontSize { get; }
        public float LineHeight { get; }
        public float SpaceAdvance { get; }
        public int Spread { get; }
        public IReadOnlyList<TextureObjectUploaded> AtlasTextures { get; }

        public static SdfFontFace? Create(string assetPath, Func<string, string?, string> resolveAssetPath, TextureManager textureManager)
        {
            try
            {
                var asset = SdfFontAsset.Load(assetPath);
                string assetDirectory = Path.GetDirectoryName(assetPath) ?? AppContext.BaseDirectory;
                var atlasPaths = new string[asset.AtlasPages.Count];
                var textures = new List<TextureObjectUploaded>(asset.AtlasPages.Count);

                for (int i = 0; i < asset.AtlasPages.Count; i++)
                {
                    string pagePath = asset.AtlasPages[i].Path;
                    string fullAtlasPath;

                    if (Path.IsPathRooted(pagePath))
                    {
                        fullAtlasPath = Path.GetFullPath(pagePath);
                    }
                    else
                    {
                        string combinedPath = Path.Combine(assetDirectory, pagePath);
                        fullAtlasPath = File.Exists(combinedPath)
                            ? Path.GetFullPath(combinedPath)
                            : Path.GetFullPath(resolveAssetPath(pagePath, null));
                    }

                    atlasPaths[i] = fullAtlasPath;
                    textures.Add(textureManager.Load(fullAtlasPath, asset.Filter, flipY: false));
                }

                var glyphs = new Dictionary<int, GlyphEntry>();
                foreach (var glyph in asset.Glyphs)
                {
                    System.Numerics.Vector2 uvMin = System.Numerics.Vector2.Zero;
                    System.Numerics.Vector2 uvMax = System.Numerics.Vector2.Zero;

                    if (glyph.AtlasIndex >= 0 && glyph.AtlasIndex < asset.AtlasPages.Count && glyph.Width > 0 && glyph.Height > 0)
                    {
                        var page = asset.AtlasPages[glyph.AtlasIndex];
                        float uvMinX = glyph.X / (float)page.Width;
                        float uvMaxX = (glyph.X + glyph.Width) / (float)page.Width;
                        float uvTop = glyph.Y / (float)page.Height;
                        float uvBottom = (glyph.Y + glyph.Height) / (float)page.Height;
                        uvMin = new System.Numerics.Vector2(uvMinX, uvTop);
                        uvMax = new System.Numerics.Vector2(uvMaxX, uvBottom);
                    }

                    glyphs[glyph.Unicode] = new GlyphEntry(
                        glyph.AtlasIndex,
                        glyph.Width,
                        glyph.Height,
                        glyph.Advance,
                        glyph.OffsetX,
                        glyph.OffsetY,
                        uvMin,
                        uvMax);
                }

                return new SdfFontFace(
                    textureManager,
                    atlasPaths,
                    textures,
                    glyphs,
                    asset.SamplingPointSize,
                    asset.LineHeight,
                    asset.SpaceAdvance,
                    asset.Spread);
            }
            catch
            {
                return null;
            }
        }

        public bool TryGetGlyph(Rune rune, out GlyphEntry glyph)
        {
            if (_glyphs.TryGetValue(rune.Value, out GlyphEntry? cached) && cached != null)
            {
                glyph = cached;
                return true;
            }

            glyph = GlyphEntry.Empty(SpaceAdvance);
            return false;
        }

        public void Dispose()
        {
            foreach (string atlasPath in _atlasPaths)
            {
                if (!string.IsNullOrWhiteSpace(atlasPath))
                    _textureManager.Unload(atlasPath);
            }
        }
    }

    private sealed class DynamicAtlasPage : IDisposable
    {
        private readonly TextureManager _textureManager;
        private readonly byte[] _pixels;
        private int _cursorX = DynamicGlyphPadding;
        private int _cursorY = DynamicGlyphPadding;
        private int _rowHeight;

        public DynamicAtlasPage(TextureManager textureManager, int index)
        {
            _textureManager = textureManager;
            Width = DynamicAtlasPageSize;
            Height = DynamicAtlasPageSize;
            _pixels = new byte[Width * Height * 4];
            Texture = CreateUploadedTexture();
        }

        public TextureObjectUploaded Texture { get; private set; }
        public int Width { get; }
        public int Height { get; }

        public bool TryAllocate(int width, int height, out DrawingRectangle rect)
        {
            int paddedWidth = width + DynamicGlyphPadding * 2;
            int paddedHeight = height + DynamicGlyphPadding * 2;

            if (_cursorX + paddedWidth > Width)
            {
                _cursorX = DynamicGlyphPadding;
                _cursorY += _rowHeight + DynamicGlyphPadding;
                _rowHeight = 0;
            }

            if (_cursorY + paddedHeight > Height)
            {
                rect = DrawingRectangle.Empty;
                return false;
            }

            rect = new DrawingRectangle(_cursorX + DynamicGlyphPadding, _cursorY + DynamicGlyphPadding, width, height);
            _cursorX += paddedWidth + DynamicGlyphPadding;
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

            var previous = Texture;
            Texture = CreateUploadedTexture();
            previous.Dispose();
        }

        public void Dispose()
        {
            Texture.Dispose();
        }

        private TextureObjectUploaded CreateUploadedTexture()
        {
            byte[] flipped = FlipPixelsY(_pixels, Width, Height);
            return _textureManager.CreateFromRgba(flipped, Width, Height, filter: SpriteTextureFilter.Linear);
        }

        private static byte[] FlipPixelsY(byte[] pixels, int width, int height)
        {
            byte[] flipped = new byte[pixels.Length];
            int stride = width * 4;
            for (int y = 0; y < height; y++)
            {
                Array.Copy(pixels, y * stride, flipped, (height - 1 - y) * stride, stride);
            }

            return flipped;
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
