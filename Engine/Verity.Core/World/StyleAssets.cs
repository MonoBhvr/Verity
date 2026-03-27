using System.Numerics;
using System.Text.Json;
using Verity.Core.Serialization;

namespace Verity.Core;

public struct ShaderAsset : IPathAsset
{
    public string Path { get; set; }
    public string Guid { get; set; }
    public ShaderAsset(string path) { Path = AssetPathUtility.Normalize(path); Guid = System.IO.Path.IsPathRooted(path) ? AssetPathUtility.EnsureMetaAndGetGuid(path) : string.Empty; }
    public ShaderAsset(string path, string guid) { Path = AssetPathUtility.Normalize(path); Guid = guid ?? string.Empty; }
    public static implicit operator ShaderAsset(string path) => new ShaderAsset(path);
    public static implicit operator string(ShaderAsset asset) => asset.Path ?? string.Empty;
    public override string ToString() => Path ?? "None";
}

public struct StyleAsset : IPathAsset
{
    public string Path { get; set; }
    public string Guid { get; set; }
    public StyleAsset(string path) { Path = AssetPathUtility.Normalize(path); Guid = System.IO.Path.IsPathRooted(path) ? AssetPathUtility.EnsureMetaAndGetGuid(path) : string.Empty; }
    public StyleAsset(string path, string guid) { Path = AssetPathUtility.Normalize(path); Guid = guid ?? string.Empty; }
    public static implicit operator StyleAsset(string path) => new StyleAsset(path);
    public static implicit operator string(StyleAsset asset) => asset.Path ?? string.Empty;
    public override string ToString() => Path ?? "None";
}

public class StyleData
{
    public string? ShaderPath { get; set; }
    public Dictionary<string, float> Floats { get; set; } = new();
    public Dictionary<string, Vector2> Vector2s { get; set; } = new();
    public Dictionary<string, Vector3> Vector3s { get; set; } = new();
    public Dictionary<string, Vector4> Vector4s { get; set; } = new();
    public Dictionary<string, Color> Colors { get; set; } = new();
    public Dictionary<string, string> Textures { get; set; } = new(); 

    private static readonly JsonSerializerOptions _options = new() {
        Converters = { new Vector2Converter(), new Vector3Converter(), new Vector4Converter(), new SpriteConverter(), new StyleAssetConverter(), new ShaderAssetConverter(), new ColorConverter() },
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static StyleData? FromJson(string json) => JsonSerializer.Deserialize<StyleData>(json, _options);
    public string ToJson() => JsonSerializer.Serialize(this, _options);
}
