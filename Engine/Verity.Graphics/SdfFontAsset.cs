using System.Text.Json;
using System.Text.Json.Serialization;
using Verity.Core.World;

namespace Verity.Graphics;

public sealed class SdfFontAsset
{
    public const string PrimaryExtension = ".fontasset";
    public const string LegacyExtension = ".sdfont";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public int Version { get; set; } = 2;
    public string SourceFontPath { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public float SamplingPointSize { get; set; } = 48f;
    public float LineHeight { get; set; }
    public float Ascent { get; set; }
    public float Descent { get; set; }
    public float SpaceAdvance { get; set; }
    public int Padding { get; set; } = 12;
    public int Spread { get; set; } = 8;
    public int Supersample { get; set; } = 4;
    public SpriteTextureFilter Filter { get; set; } = SpriteTextureFilter.Linear;
    public List<SdfFontAtlasPage> AtlasPages { get; set; } = [];
    public List<SdfFontGlyph> Glyphs { get; set; } = [];

    public static bool IsFontAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string extension = Path.GetExtension(path);
        return string.Equals(extension, PrimaryExtension, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, LegacyExtension, StringComparison.OrdinalIgnoreCase);
    }

    public static SdfFontAsset Load(string assetPath)
    {
        string fullPath = Path.GetFullPath(assetPath);
        return JsonSerializer.Deserialize<SdfFontAsset>(File.ReadAllText(fullPath), SerializerOptions) ?? new SdfFontAsset();
    }

    public void Save(string assetPath)
    {
        string fullPath = Path.GetFullPath(assetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(this, SerializerOptions));
    }
}

public sealed class SdfFontAtlasPage
{
    public string Path { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class SdfFontGlyph
{
    public int Unicode { get; set; }
    public int AtlasIndex { get; set; } = -1;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public float Advance { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
}

public sealed class SdfFontGenerationOptions
{
    public const string DefaultCharacterSet =
        " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    public float PointSize { get; set; } = 48f;
    public int AtlasWidth { get; set; } = 1024;
    public int AtlasHeight { get; set; } = 1024;
    public int Padding { get; set; } = 12;
    public int Spread { get; set; } = 8;
    public int Supersample { get; set; } = 4;
    public string Characters { get; set; } = DefaultCharacterSet;
    public SpriteTextureFilter Filter { get; set; } = SpriteTextureFilter.Linear;
    public bool OverwriteExistingFiles { get; set; } = true;

    public void Normalize()
    {
        PointSize = MathF.Max(1f, PointSize);
        AtlasWidth = Math.Max(64, AtlasWidth);
        AtlasHeight = Math.Max(64, AtlasHeight);
        Spread = Math.Max(1, Spread);
        Padding = Math.Max(Spread + 2, Padding);
        Supersample = Math.Clamp(Supersample, 1, 8);
        Characters = string.IsNullOrEmpty(Characters) ? DefaultCharacterSet : string.Concat(EnumerateDistinctRunes(Characters));
    }

    internal static IEnumerable<string> EnumerateDistinctRunes(string text)
    {
        var seen = new HashSet<int>();
        foreach (var rune in text.EnumerateRunes())
        {
            if (seen.Add(rune.Value))
                yield return rune.ToString();
        }
    }
}
